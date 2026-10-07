using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Yoake.Core.Commands;
using Yoake.Core.Jobs;
using Yoake.Core.Hotkeys;
using Yoake.Core.Settings;
using Yoake.Core.Subtitles;
using Yoake.Core.Undo;
using Yoake.Core.Workspace;
using Yoake.Native;
using Yoake.UI.Commands;
using Yoake.UI.Services;

namespace Yoake.UI.ViewModels;

public sealed partial class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private sealed class DocumentState(SubtitleEditor editor)
    {
        public SubtitleEditor Editor { get; } = editor;
        public FfmsMediaSession? Media;
        public IReadOnlyList<float> Waveform = Array.Empty<float>();
        public double Time;
        public AssEvent? Selected;
        public CancellationTokenSource? Loading;
    }
    private readonly CommandRegistry _registry;
    private readonly WorkspaceManager _workspace;
    private readonly UndoManager _emptyUndo = new();
    private UndoManager _undo => ActiveEditor?.Undo ?? _emptyUndo;
    private readonly IThemeService _theme;
    private readonly SettingsStore _settingsStore;
    private readonly BackgroundJobService _jobs = new();
    private readonly Dictionary<Guid, DocumentState> _documents = [];
    private readonly SemaphoreSlim _videoRequestGate = new(1, 1);
    private AppSettings _settings;
    private Guid? _activeId;
    private AssDocument? _activeSubtitleDocument;
    private AssEvent? _selectedEvent;
    private FfmsMediaSession? _media;
    private WriteableBitmap? _videoFrame;
    private IReadOnlyList<float> _waveformSamples = Array.Empty<float>();
    private string _activeVisualTool = "Position";
    private string _subtitleStatus = "Ready";
    private string _mediaStatus = "Open video/audio";
    private double _mediaDurationSeconds, _currentTimeSeconds;
    private long _seekGeneration;
    private byte[]? _decodedPixels;
    public event EventHandler? FrameReady;
    private bool _disposed;
    private EventEditDraft? _draft;
    public IEditorDialogs? Dialogs { get; set; }
    public SubtitleEditor? ActiveEditor => _activeId is { } id && _documents.TryGetValue(id,out var state) ? state.Editor : null;
    public CommandRegistry Registry => _registry;
    public HotkeyResolver Hotkeys { get; } = new();
    public ObservableCollection<DocumentTabViewModel> Tabs { get; } = [];
    public Dictionary<string,ICommand> Actions { get; } = [];
    public IReadOnlyList<string> RecentFiles => _settings.RecentFiles;
    public IList<AssEvent> SelectedEvents { get; } = new ObservableCollection<AssEvent>();
    public int TextCursor { get; set; }
    public EventEditDraft? Draft
    {
        get => _draft;
        private set
        {
            if (_draft is not null) _draft.PropertyChanged -= DraftChanged;
            SetField(ref _draft,value);
            if (_draft is not null) _draft.PropertyChanged += DraftChanged;
        }
    }
    private void DraftChanged(object? sender,PropertyChangedEventArgs e)
    {
        if (_workspace.ActiveDocument is {} session) session.IsDirty=(ActiveEditor?.IsDirty??false)||(Draft?.IsChanged??false);
        _registry.NotifyStateChanged();
    }
    public IReadOnlyList<AssEvent> Events => _activeSubtitleDocument?.Events ?? (IReadOnlyList<AssEvent>)Array.Empty<AssEvent>();
    public IReadOnlyList<string> StyleNames => _activeSubtitleDocument?.Styles.Select(s=>s.Name).ToArray() ?? [];
    public IReadOnlyList<string> ActorNames => Events.Select(l=>l.Actor).Where(s=>s.Length>0).Distinct().Order().ToArray();
    public AssEvent? SelectedEvent
    {
        get => _selectedEvent;
        set
        {
            if (ReferenceEquals(_selectedEvent,value)) return;
            if (!CommitDraft()) { OnPropertyChanged(); return; }
            CancelGesture();
            _selectedEvent=value; ReloadDraft(); OnPropertyChanged(); OnPropertyChanged(nameof(HasSelectedEvent));
            if (value?.StartMilliseconds is { } start && !IsPlaying) CurrentTimeSeconds=start/1000d;
            _registry.NotifyStateChanged();
        }
    }
    public bool HasSelectedEvent => SelectedEvent is not null;
    public string? ActiveSubtitlePath => _workspace.ActiveDocument?.Path;
    public string SuggestedSubtitleFileName => _workspace.ActiveDocument?.Title is { Length:>0 } title ? (Path.HasExtension(title) ? title : title+".ass") : "subtitle.ass";
    public string SubtitleStatus { get=>_subtitleStatus; private set=>SetField(ref _subtitleStatus,value); }
    public string MediaStatus { get=>_mediaStatus; private set=>SetField(ref _mediaStatus,value); }
    public string ActiveVisualTool { get=>_activeVisualTool; private set=>SetField(ref _activeVisualTool,value); }
    public long PreviewRevision => _activeSubtitleDocument?.Revision ?? 0;
    public string TimeDisplay => AssTime.Format((long)(CurrentTimeSeconds*1000));
    public string DurationDisplay => AssTime.Format((long)(MediaDurationSeconds*1000));
    public WriteableBitmap? VideoFrame
    {
        get=>_videoFrame;
        private set { if (ReferenceEquals(_videoFrame,value)) return; var old=_videoFrame; _videoFrame=value; OnPropertyChanged(); old?.Dispose(); }
    }
    public IReadOnlyList<float> WaveformSamples { get=>_waveformSamples; private set=>SetField(ref _waveformSamples,value); }
    public double MediaDurationSeconds { get=>_mediaDurationSeconds; private set { SetField(ref _mediaDurationSeconds,Math.Max(0,value)); OnPropertyChanged(nameof(DurationDisplay)); } }
    public double CurrentTimeSeconds
    {
        get=>_currentTimeSeconds;
        set
        {
            var time=MediaDurationSeconds>0 ? Math.Clamp(value,0,MediaDurationSeconds) : Math.Max(0,value);
            if (!double.IsFinite(time) || Math.Abs(_currentTimeSeconds-time)<0.00001) return;
            _currentTimeSeconds=time; OnPropertyChanged(); OnPropertyChanged(nameof(TimeDisplay));
            _activeSubtitleDocument?.UpdateActiveTime((long)(time*1000));
            if (!_clockUpdateFromPlayback && _media?.HasVideo==true) _=RefreshVideoFrameAsync(time);
        }
    }
    public MainWindowViewModel(CommandRegistry registry, WorkspaceManager workspace, UndoManager undo, IThemeService theme, SettingsStore settingsStore, AppSettings settings)
    {
        _registry=registry; _workspace=workspace; _theme=theme; _settingsStore=settingsStore; _settings=settings;
        RegisterEditorCommands();
        _registry.CommandFailed+=(_,e)=>SubtitleStatus=e.Exception.Message;
        _workspace.Changed+=OnWorkspaceChanged;
        CreateNewDocument();
    }
    public bool CommitDraft()
    {
        if (Draft?.IsChanged!=true || SelectedEvent is null || ActiveEditor is null) return true;
        try { ActiveEditor.EditEvent(SelectedEvent,Draft.Values); ReloadDraft(); return true; }
        catch(Exception e) { SubtitleStatus=e.Message; return false; }
    }
    private void ReloadDraft() { Draft=SelectedEvent is null ? null : new EventEditDraft(SelectedEvent); if(_workspace.ActiveDocument is {} session)session.IsDirty=ActiveEditor?.IsDirty??false; }
    private void CreateNewDocument()
    {
        if (!CommitDraft()) return;
        var session=_workspace.CreateUntitled(); Attach(session,AssDocument.CreateEmpty()); SynchronizeActiveDocument(); SubtitleStatus="New ASS document";
    }
    private void Attach(DocumentSession session, AssDocument document)
    {
        var state=new DocumentState(new SubtitleEditor(document)); _documents[session.Id]=state;
        document.Changed+=(_,_)=> { session.IsDirty=true; if (_activeId==session.Id) { InvalidateSubtitlePreview(true); OnPropertyChanged(nameof(PreviewRevision)); } };
        state.Editor.Undo.Changed+=(_,_)=> { session.IsDirty=state.Editor.IsDirty; if (_activeId==session.Id) { ReloadDraft(); _registry.NotifyStateChanged(); } };
        state.Editor.MarkSaved();
    }
    public bool OpenSubtitle(string path)
    {
        if (!CommitDraft()) return false;
        try
        {
            var full=Path.GetFullPath(path); var existing=_workspace.Documents.FirstOrDefault(s=>string.Equals(s.Path,full,StringComparison.OrdinalIgnoreCase));
            if (existing is not null) { _workspace.Activate(existing.Id); return true; }
            var doc=AssDocument.Load(full); var session=_workspace.Open(full); Attach(session,doc); SynchronizeActiveDocument(); Remember(full); SubtitleStatus=$"Loaded {doc.Events.Count} events"; return true;
        }
        catch(Exception e) { SubtitleStatus=$"Open failed: {e.Message}"; return false; }
    }
    private void Remember(string path)
    {
        _settings=_settings with { RecentFiles=new[] {path}.Concat(_settings.RecentFiles.Where(p=>!p.Equals(path,StringComparison.OrdinalIgnoreCase))).Take(12).ToArray() };
        _settingsStore.Save(_settings); OnPropertyChanged(nameof(RecentFiles));
    }
    public bool SaveActiveSubtitle(string path)
    {
        if (!CommitDraft() || ActiveEditor is null || _workspace.ActiveDocument is not { } session) return false;
        try { var full=Path.GetFullPath(path); if (_workspace.Documents.Any(s=>s.Id!=session.Id && string.Equals(s.Path,full,StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("This path is already open in another tab."); ActiveEditor.Document.Save(full); session.Path=full; session.Title=Path.GetFileName(full); ActiveEditor.MarkSaved(); Remember(full); SubtitleStatus=$"Saved {session.Title}"; OnPropertyChanged(nameof(ActiveSubtitlePath)); return true; }
        catch(Exception e) { SubtitleStatus=$"Save failed: {e.Message}"; return false; }
    }
    private async Task<bool> SaveAsync(bool saveAs=false)
    {
        var path=saveAs ? null : ActiveSubtitlePath;
        path ??= Dialogs is null ? null : await Dialogs.SaveSubtitleAsync(SuggestedSubtitleFileName);
        return path is not null && SaveActiveSubtitle(path);
    }
    public async Task<bool> CloseDocumentAsync(Guid id)
    {
        if (!_documents.TryGetValue(id,out var state)) return true;
        if (!CommitDraft()) return false;
        if (!_workspace.Activate(id)) return false;
        if (state.Editor.IsDirty)
        {
            var choice=Dialogs is null ? UnsavedChoice.Cancel : await Dialogs.ConfirmUnsavedAsync(_workspace.ActiveDocument!.Title);
            if (choice==UnsavedChoice.Cancel || choice==UnsavedChoice.Save && !await SaveAsync()) return false;
        }
        CancelGesture(); StopPlayback(); state.Loading?.Cancel(); state.Loading?.Dispose();
        var media=state.Media; state.Media=null; if (media is not null) _=Task.Run(media.Dispose);
        _documents.Remove(id); _workspace.Close(id); if (_workspace.Documents.Count==0) CreateNewDocument(); return true;
    }
    public async Task<bool> CloseWindowAsync()
    {
        foreach (var id in _workspace.Documents.Select(s=>s.Id).ToArray()) if (!await CloseDocumentAsync(id)) return false;
        return true;
    }
    private void OnWorkspaceChanged(object? sender,EventArgs e) { SynchronizeTabs(); SynchronizeActiveDocument(); }
    private void SynchronizeActiveDocument()
    {
        var id=_workspace.ActiveDocumentId;
        if (id==_activeId && ActiveEditor is not null && ReferenceEquals(_activeSubtitleDocument,ActiveEditor.Document)) return;
        CancelGesture(); StopPlayback(); Interlocked.Increment(ref _seekGeneration);
        if (_activeId is { } old && _documents.TryGetValue(old,out var previous)) { previous.Time=CurrentTimeSeconds; previous.Selected=SelectedEvent; }
        _activeId=id;
        var state=id is { } key && _documents.TryGetValue(key,out var found) ? found : null;
        _activeSubtitleDocument=state?.Editor.Document; _media=state?.Media;
        _selectedEvent=state?.Selected ?? _activeSubtitleDocument?.Events.FirstOrDefault(); SelectedEvents.Clear(); if (_selectedEvent is not null) SelectedEvents.Add(_selectedEvent);
        ReloadDraft(); VideoFrame=null; WaveformSamples=state?.Waveform ?? Array.Empty<float>(); MediaDurationSeconds=_media?.Info.DurationSeconds ?? 0;
        _currentTimeSeconds=state?.Time ?? 0;
        foreach(var name in new[]{nameof(Events),nameof(SelectedEvent),nameof(HasSelectedEvent),nameof(StyleNames),nameof(ActorNames),nameof(ActiveSubtitlePath),nameof(SuggestedSubtitleFileName),nameof(CurrentTimeSeconds),nameof(TimeDisplay),nameof(CanPlayMedia)}) OnPropertyChanged(name);
        MediaStatus=_media?.SourcePath ?? "Open video/audio"; InvalidateSubtitlePreview(true); _registry.NotifyStateChanged();
    }
    public async Task<bool> OpenMediaAsync(string path)
    {
        if (_activeId is not { } id || !_documents.TryGetValue(id,out var state)) return false;
        StopPlayback(); state.Loading?.Cancel(); state.Loading?.Dispose(); var cts=new CancellationTokenSource(); state.Loading=cts; var token=cts.Token;
        MediaStatus=$"FFMS2 indexing {Path.GetFileName(path)}…";
        FfmsMediaSession? opened=null;
        try
        {
            var job=_jobs.Run("Open media",async ct=> { opened=await Task.Run(()=>FfmsMediaSession.Open(path,ct),ct); },token); await job.Completion;
            token.ThrowIfCancellationRequested();
            if (_disposed || !_documents.ContainsKey(id)) { opened?.Dispose(); return false; }
            var old=state.Media; state.Media=opened; opened=null; state.Time=0; state.Waveform=Array.Empty<float>(); if (old is not null) _=Task.Run(old.Dispose);
            if (_activeId==id) { _media=state.Media; DisposeSubtitleRenderer(); MediaDurationSeconds=_media!.Info.DurationSeconds; CurrentTimeSeconds=0; WaveformSamples=state.Waveform; OnPropertyChanged(nameof(CanPlayMedia)); MediaStatus=Path.GetFileName(path); await RefreshVideoFrameAsync(0); }
            var media=state.Media!; float[] peaks=[];
            var analysis=_jobs.Run("Audio peaks",ct=> { peaks=media.BuildWaveform(cancellationToken:ct); return Task.CompletedTask; },token); await analysis.Completion;
            token.ThrowIfCancellationRequested(); state.Waveform=peaks;
            if (_activeId==id && ReferenceEquals(_media,media)) WaveformSamples=peaks;
            return true;
        }
        catch(OperationCanceledException) { opened?.Dispose(); return false; }
        catch(Exception e) { opened?.Dispose(); if (_activeId==id) MediaStatus=e.Message; return false; }
    }
    private async Task RefreshVideoFrameAsync(double seconds)
    {
        var session=_media; if (session?.HasVideo!=true || _disposed) return;
        var generation=Interlocked.Increment(ref _seekGeneration); var subtitleText=_activeSubtitleDocument?.Serialize(); var revision=Volatile.Read(ref _subtitleRevision);
        try
        {
            await _videoRequestGate.WaitAsync();
            try
            {
                if (_disposed || generation!=Volatile.Read(ref _seekGeneration) || !ReferenceEquals(session,_media)) return;
                var result=await Task.Run(()=> { var frame=session.GetFrameAtTime(seconds,_decodedPixels);_decodedPixels=frame.Pixels; return (Frame:frame,Error:CompositeSubtitles(frame,seconds,subtitleText,revision)); });
                if (_disposed || generation!=Volatile.Read(ref _seekGeneration) || !ReferenceEquals(session,_media)) return;
                var frame=result.Frame;
                if(VideoFrame is not {} bitmap||bitmap.PixelSize.Width!=frame.Width||bitmap.PixelSize.Height!=frame.Height)VideoFrame=CreateBitmap(frame);
                else { using var buffer=bitmap.Lock();for(var y=0;y<frame.Height;y++)Marshal.Copy(frame.Pixels,y*frame.Stride,buffer.Address+y*buffer.RowBytes,frame.Width*4); }
                FrameReady?.Invoke(this,EventArgs.Empty);
                if (result.Error is not null) MediaStatus=result.Error;
            }
            finally { _videoRequestGate.Release(); }
        }
        catch(Exception e) { if (!_disposed && generation==Volatile.Read(ref _seekGeneration)) MediaStatus=e.Message; }
    }
    private static WriteableBitmap CreateBitmap(DecodedVideoFrame frame)
    {
        var bitmap=new WriteableBitmap(new PixelSize(frame.Width,frame.Height),new Vector(96,96),PixelFormat.Bgra8888,AlphaFormat.Unpremul);
        using var buffer=bitmap.Lock(); for(var y=0;y<frame.Height;y++) Marshal.Copy(frame.Pixels,y*frame.Stride,buffer.Address+y*buffer.RowBytes,frame.Width*4); return bitmap;
    }
    private CommandContext CurrentContext()=>new(_activeId,"Default");
    private void SynchronizeTabs()
    {
        var sessions=_workspace.Documents; var ids=sessions.Select(s=>s.Id).ToHashSet();
        for(var i=Tabs.Count-1;i>=0;i--) if (!ids.Contains(Tabs[i].Id)) { Tabs[i].Dispose(); Tabs.RemoveAt(i); }
        for(var i=0;i<sessions.Count;i++) if (!Tabs.Any(t=>t.Id==sessions[i].Id)) { var session=sessions[i]; Tabs.Insert(i,new DocumentTabViewModel(session,false,new RegistryCommand(_registry,CommandIds.WorkspaceActivateTab,CurrentContext,session.Id),new RegistryCommand(_registry,CommandIds.SubtitleClose,CurrentContext,session.Id))); }
        foreach(var tab in Tabs) tab.SetActive(tab.Id==_workspace.ActiveDocumentId);
    }
    public void Dispose()
    {
        if (_disposed) return; CancelGesture(); _disposed=true; StopPlayback(); _jobs.CancelAll(); Interlocked.Increment(ref _seekGeneration); _workspace.Changed-=OnWorkspaceChanged;
        foreach(var state in _documents.Values) { state.Loading?.Cancel(); state.Loading?.Dispose(); if (state.Media is {} media) _=Task.Run(media.Dispose); }
        foreach(var tab in Tabs) tab.Dispose(); Tabs.Clear(); DisposeSubtitleRenderer(); VideoFrame=null;
    }
    private void SetField<T>(ref T field,T value,[CallerMemberName]string? name=null) { if(EqualityComparer<T>.Default.Equals(field,value))return; field=value; OnPropertyChanged(name); }
    private void OnPropertyChanged([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(name));
    public event PropertyChangedEventHandler? PropertyChanged;
}
