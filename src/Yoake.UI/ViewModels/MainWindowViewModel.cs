using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Yoake.Core.Commands;
using Yoake.Core.Settings;
using Yoake.Core.Subtitles;
using Yoake.Core.Undo;
using Yoake.Core.Workspace;
using Yoake.Native;
using Yoake.UI.Commands;
using Yoake.UI.Services;

namespace Yoake.UI.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly IReadOnlyList<AssEvent> EmptyEvents = Array.Empty<AssEvent>();

    private readonly CommandRegistry _registry;
    private readonly WorkspaceManager _workspace;
    private readonly UndoManager _undo;
    private readonly IThemeService _theme;
    private readonly SettingsStore _settingsStore;
    private readonly Dictionary<Guid, AssDocument> _subtitleDocuments = [];
    private readonly SemaphoreSlim _videoRequestGate = new(1, 1);
    private AppSettings _settings;
    private AssDocument? _activeSubtitleDocument;
    private AssEvent? _selectedEvent;
    private FfmsMediaSession? _media;
    private WriteableBitmap? _videoFrame;
    private IReadOnlyList<float> _waveformSamples = Array.Empty<float>();
    private string _activeVisualTool = "Position";
    private string _subtitleStatus = "Ready";
    private string _mediaStatus = "Open a video/audio file to start FFMS2.";
    private double _mediaDurationSeconds;
    private double _currentTimeSeconds;
    private long _mediaGeneration;
    private long _seekGeneration;
    private bool _disposed;

    public MainWindowViewModel(
        CommandRegistry registry,
        WorkspaceManager workspace,
        UndoManager undo,
        IThemeService theme,
        SettingsStore settingsStore,
        AppSettings settings)
    {
        _registry = registry;
        _workspace = workspace;
        _undo = undo;
        _theme = theme;
        _settingsStore = settingsStore;
        _settings = settings;

        RegisterCommands();
        NewDocumentCommand = new RegistryCommand(_registry, CommandIds.SubtitleNew, CurrentContext);
        UndoCommand = new RegistryCommand(_registry, CommandIds.EditUndo, CurrentContext);
        RedoCommand = new RegistryCommand(_registry, CommandIds.EditRedo, CurrentContext);
        ThemeCycleCommand = new RegistryCommand(_registry, CommandIds.ViewThemeCycle, CurrentContext);
        PositionToolCommand = new RegistryCommand(_registry, CommandIds.VideoToolPosition, CurrentContext);
        ClipToolCommand = new RegistryCommand(_registry, CommandIds.VideoToolClip, CurrentContext);

        RegisterWorkspaceCommands();
        _workspace.Changed += OnWorkspaceChanged;
        _undo.Changed += (_, _) => _registry.NotifyStateChanged();
        CreateNewDocument();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DocumentTabViewModel> Tabs { get; } = [];
    public ICommand NewDocumentCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand ThemeCycleCommand { get; }
    public ICommand PositionToolCommand { get; }
    public ICommand ClipToolCommand { get; }

    public IReadOnlyList<AssEvent> Events => _activeSubtitleDocument?.Events ?? EmptyEvents;

    public AssEvent? SelectedEvent
    {
        get => _selectedEvent;
        set
        {
            if (ReferenceEquals(_selectedEvent, value))
                return;
            _selectedEvent = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedEvent));
            if (value?.StartMilliseconds is { } start)
                CurrentTimeSeconds = start / 1000d;
        }
    }

    public bool HasSelectedEvent => SelectedEvent is not null;
    public string? ActiveSubtitlePath => _workspace.ActiveDocument?.Path;
    public string SuggestedSubtitleFileName => _workspace.ActiveDocument?.Title is { Length: > 0 } title
        ? (System.IO.Path.HasExtension(title) ? title : title + ".ass")
        : "subtitle.ass";

    public string SubtitleStatus
    {
        get => _subtitleStatus;
        private set => SetField(ref _subtitleStatus, value);
    }

    public string MediaStatus
    {
        get => _mediaStatus;
        private set => SetField(ref _mediaStatus, value);
    }

    public WriteableBitmap? VideoFrame
    {
        get => _videoFrame;
        private set
        {
            if (ReferenceEquals(_videoFrame, value))
                return;
            var old = _videoFrame;
            _videoFrame = value;
            OnPropertyChanged();
            old?.Dispose();
        }
    }

    public IReadOnlyList<float> WaveformSamples
    {
        get => _waveformSamples;
        private set => SetField(ref _waveformSamples, value);
    }

    public double MediaDurationSeconds
    {
        get => _mediaDurationSeconds;
        private set => SetField(ref _mediaDurationSeconds, Math.Max(0, value));
    }

    public double CurrentTimeSeconds
    {
        get => _currentTimeSeconds;
        set
        {
            var clamped = MediaDurationSeconds > 0
                ? Math.Clamp(value, 0, MediaDurationSeconds)
                : Math.Max(0, value);
            if (Math.Abs(_currentTimeSeconds - clamped) < 0.0005)
                return;
            _currentTimeSeconds = clamped;
            OnPropertyChanged();
            if (_media?.HasVideo == true)
                _ = RefreshVideoFrameAsync(clamped);
        }
    }

    public string ActiveVisualTool
    {
        get => _activeVisualTool;
        private set => SetField(ref _activeVisualTool, value);
    }

    public bool OpenSubtitle(string path)
    {
        ThrowIfDisposed();
        try
        {
            var fullPath = System.IO.Path.GetFullPath(path);
            var existing = _workspace.Documents.FirstOrDefault(document =>
                document.Path is not null && string.Equals(document.Path, fullPath, StringComparison.OrdinalIgnoreCase));
            if (existing is not null && _subtitleDocuments.ContainsKey(existing.Id))
            {
                _workspace.Activate(existing.Id);
                SubtitleStatus = $"Already open: {System.IO.Path.GetFileName(fullPath)}";
                return true;
            }

            var document = AssDocument.Load(fullPath);
            var session = _workspace.Open(fullPath);
            AttachSubtitleDocument(session, document);
            session.IsDirty = false;
            SynchronizeActiveDocument();
            SubtitleStatus = $"Loaded {document.Events.Count} events from {System.IO.Path.GetFileName(fullPath)}";
            OnPropertyChanged(nameof(ActiveSubtitlePath));
            OnPropertyChanged(nameof(SuggestedSubtitleFileName));
            return true;
        }
        catch (Exception exception)
        {
            SubtitleStatus = $"Open failed: {exception.Message}";
            return false;
        }
    }

    public bool SaveActiveSubtitle(string path)
    {
        ThrowIfDisposed();
        var session = _workspace.ActiveDocument;
        if (session is null || !_subtitleDocuments.TryGetValue(session.Id, out var document))
        {
            SubtitleStatus = "There is no active subtitle document to save.";
            return false;
        }

        try
        {
            var fullPath = System.IO.Path.GetFullPath(path);
            document.Save(fullPath);
            session.Path = fullPath;
            session.Title = System.IO.Path.GetFileName(fullPath);
            session.IsDirty = false;
            SubtitleStatus = $"Saved {document.Events.Count} events to {session.Title}";
            OnPropertyChanged(nameof(ActiveSubtitlePath));
            OnPropertyChanged(nameof(SuggestedSubtitleFileName));
            return true;
        }
        catch (Exception exception)
        {
            SubtitleStatus = $"Save failed: {exception.Message}";
            return false;
        }
    }

    public async Task<bool> OpenMediaAsync(string path)
    {
        ThrowIfDisposed();
        var generation = Interlocked.Increment(ref _mediaGeneration);
        MediaStatus = $"FFMS2 indexing {System.IO.Path.GetFileName(path)}…";

        FfmsMediaSession? session = null;
        try
        {
            var result = await Task.Run(() =>
            {
                var opened = FfmsMediaSession.Open(path);
                try
                {
                    var frame = opened.HasVideo ? opened.GetFrameAtTime(0) : null;
                    var waveform = opened.HasAudio ? opened.BuildWaveform() : Array.Empty<float>();
                    return (Session: opened, Frame: frame, Waveform: waveform);
                }
                catch
                {
                    opened.Dispose();
                    throw;
                }
            });
            session = result.Session;

            if (generation != Volatile.Read(ref _mediaGeneration) || _disposed)
            {
                session.Dispose();
                return false;
            }

            var oldMedia = _media;
            _media = session;
            session = null;
            oldMedia?.Dispose();

            MediaDurationSeconds = _media.Info.DurationSeconds;
            _currentTimeSeconds = 0;
            OnPropertyChanged(nameof(CurrentTimeSeconds));
            WaveformSamples = result.Waveform;
            VideoFrame = result.Frame is null ? null : CreateBitmap(result.Frame);

            var info = _media.Info;
            var videoPart = _media.HasVideo
                ? $"{info.Width}×{info.Height}, {info.FrameCount} frames @ {info.FramesPerSecond:0.###} fps"
                : "no video";
            var audioPart = _media.HasAudio
                ? $"{info.Channels}ch {info.SampleRate / 1000d:0.#} kHz"
                : "no audio";
            MediaStatus = $"{System.IO.Path.GetFileName(info.Path)} — {videoPart} — {audioPart}";
            return true;
        }
        catch (Exception exception)
        {
            session?.Dispose();
            if (generation == Volatile.Read(ref _mediaGeneration))
                MediaStatus = $"Media open failed: {exception.Message}";
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Interlocked.Increment(ref _mediaGeneration);
        Interlocked.Increment(ref _seekGeneration);
        _workspace.Changed -= OnWorkspaceChanged;
        foreach (var tab in Tabs)
            tab.Dispose();
        Tabs.Clear();
        _media?.Dispose();
        _media = null;
        VideoFrame = null;
        _videoRequestGate.Dispose();
    }

    private CommandContext CurrentContext() => new(_workspace.ActiveDocumentId, "Default");

    private void RegisterCommands()
    {
        _registry.Register(new AppCommand(
            new(CommandIds.SubtitleNew, "New subtitle", "Create a new subtitle document", "Subtitle", "new"),
            (_, _) => { CreateNewDocument(); return ValueTask.CompletedTask; }));

        _registry.Register(new AppCommand(
            new(CommandIds.SubtitleClose, "Close subtitle", "Close a subtitle document", "Subtitle", "close"),
            (invocation, _) =>
            {
                if (invocation.Parameter is Guid id)
                {
                    _subtitleDocuments.Remove(id);
                    _workspace.Close(id);
                    if (_workspace.Documents.Count == 0)
                        CreateNewDocument();
                }
                return ValueTask.CompletedTask;
            }));

        _registry.Register(new AppCommand(
            new(CommandIds.EditUndo, "Undo", "Undo the last editor operation", "Edit", "undo"),
            (_, _) => { _undo.Undo(); return ValueTask.CompletedTask; },
            _ => _undo.CanUndo));

        _registry.Register(new AppCommand(
            new(CommandIds.EditRedo, "Redo", "Redo the last editor operation", "Edit", "redo"),
            (_, _) => { _undo.Redo(); return ValueTask.CompletedTask; },
            _ => _undo.CanRedo));

        _registry.Register(new AppCommand(
            new(CommandIds.ViewThemeCycle, "Cycle theme", "Cycle System, Dark, and Light themes", "View", "theme"),
            (_, _) =>
            {
                _settings = _settings with { Theme = _theme.Next() };
                _settingsStore.Save(_settings);
                return ValueTask.CompletedTask;
            }));

        _registry.Register(new AppCommand(
            new(CommandIds.VideoToolPosition, "Position tool", "Select the position visual tool", "Visual Tools", "position"),
            (_, _) => { ActiveVisualTool = "Position"; return ValueTask.CompletedTask; }));

        _registry.Register(new AppCommand(
            new(CommandIds.VideoToolClip, "Clip tool", "Select the clip visual tool", "Visual Tools", "clip"),
            (_, _) => { ActiveVisualTool = "Clip"; return ValueTask.CompletedTask; }));
    }

    private void CreateNewDocument()
    {
        var session = _workspace.CreateUntitled();
        AttachSubtitleDocument(session, AssDocument.CreateEmpty());
        session.IsDirty = false;
        SynchronizeActiveDocument();
        SubtitleStatus = "New ASS document";
    }

    private void AttachSubtitleDocument(DocumentSession session, AssDocument document)
    {
        _subtitleDocuments[session.Id] = document;
        document.Changed += (_, _) => session.IsDirty = true;
    }

    private void OnWorkspaceChanged(object? sender, EventArgs e)
    {
        SynchronizeTabs();
        SynchronizeActiveDocument();
    }

    private void SynchronizeActiveDocument()
    {
        var id = _workspace.ActiveDocumentId;
        _activeSubtitleDocument = id is { } documentId && _subtitleDocuments.TryGetValue(documentId, out var document)
            ? document
            : null;
        OnPropertyChanged(nameof(Events));
        OnPropertyChanged(nameof(ActiveSubtitlePath));
        OnPropertyChanged(nameof(SuggestedSubtitleFileName));
        SelectedEvent = _activeSubtitleDocument?.Events.FirstOrDefault();
    }

    private async Task RefreshVideoFrameAsync(double seconds)
    {
        var session = _media;
        if (session?.HasVideo != true || _disposed)
            return;
        var seekGeneration = Interlocked.Increment(ref _seekGeneration);
        try
        {
            await _videoRequestGate.WaitAsync();
            try
            {
                if (_disposed || seekGeneration != Volatile.Read(ref _seekGeneration) || !ReferenceEquals(session, _media))
                    return;
                var frame = await Task.Run(() => session.GetFrameAtTime(seconds));
                if (_disposed || seekGeneration != Volatile.Read(ref _seekGeneration) || !ReferenceEquals(session, _media))
                    return;
                VideoFrame = CreateBitmap(frame);
            }
            finally
            {
                _videoRequestGate.Release();
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception exception)
        {
            if (!_disposed && seekGeneration == Volatile.Read(ref _seekGeneration))
                MediaStatus = $"Frame seek failed: {exception.Message}";
        }
    }

    private static WriteableBitmap CreateBitmap(DecodedVideoFrame frame)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(frame.Width, frame.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);
        using var framebuffer = bitmap.Lock();
        var rowBytes = checked(frame.Width * 4);
        for (var y = 0; y < frame.Height; y++)
            Marshal.Copy(frame.Pixels, y * frame.Stride, framebuffer.Address + y * framebuffer.RowBytes, rowBytes);
        return bitmap;
    }

    private void SynchronizeTabs()
    {
        var documents = _workspace.Documents;
        var liveIds = documents.Select(document => document.Id).ToHashSet();

        for (var i = Tabs.Count - 1; i >= 0; i--)
        {
            if (liveIds.Contains(Tabs[i].Id))
                continue;
            var removed = Tabs[i];
            Tabs.RemoveAt(i);
            removed.Dispose();
        }

        for (var index = 0; index < documents.Count; index++)
        {
            var document = documents[index];
            if (index < Tabs.Count && Tabs[index].Id == document.Id)
                continue;

            var existingIndex = -1;
            for (var candidate = index + 1; candidate < Tabs.Count; candidate++)
            {
                if (Tabs[candidate].Id == document.Id)
                {
                    existingIndex = candidate;
                    break;
                }
            }

            if (existingIndex >= 0)
            {
                Tabs.Move(existingIndex, index);
            }
            else
            {
                var id = document.Id;
                Tabs.Insert(index, new DocumentTabViewModel(
                    document,
                    _workspace.ActiveDocumentId == id,
                    new RegistryCommand(_registry, CommandIds.WorkspaceActivateTab, CurrentContext, id),
                    new RegistryCommand(_registry, CommandIds.SubtitleClose, CurrentContext, id)));
            }
        }

        foreach (var tab in Tabs)
            tab.SetActive(_workspace.ActiveDocumentId == tab.Id);
    }

    public void RegisterWorkspaceCommands()
    {
        if (_registry.TryGet(CommandIds.WorkspaceActivateTab, out _)) return;
        _registry.Register(new AppCommand(
            new(CommandIds.WorkspaceActivateTab, "Activate tab", "Activate an open document", "Workspace"),
            (invocation, _) =>
            {
                if (invocation.Parameter is Guid id) _workspace.Activate(id);
                return ValueTask.CompletedTask;
            }));
        SynchronizeTabs();
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        OnPropertyChanged(propertyName);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
