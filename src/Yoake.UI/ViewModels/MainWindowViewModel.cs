using Yoake.Core.Audio;
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
        public string[] StyleNames=editor.Document.Styles.Select(s=>s.Name).ToArray();
        public FfmsMediaSession? Media;
        public WaveformData? Waveform;
        public double Time;
        public AssEvent? Selected;
        public CancellationTokenSource? Loading;
        public double AudioStart,AudioSpan=20;
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
    private WaveformData? _waveformSamples;
    private string _activeVisualTool = "Position";
    private string _subtitleStatus = "Ready";
    private string _mediaStatus = "Open video/audio";
    private double _mediaDurationSeconds, _currentTimeSeconds;
    private long _seekGeneration;
    private long _previewSourceRevision=-1;
    private string? _previewSource;
    public long DisplayedPreviewRevision{get;private set;}
    public string? PreviewSource()
    {
        var revision=PreviewRevision;if(_previewSourceRevision==revision)return _previewSource;
        try{_previewSource=_activeSubtitleDocument?.SerializePreview(SelectedEvent,_gesture is null&&Draft?.IsChanged==true?Draft.Values:null);}
        catch(ArgumentException){_previewSource=_activeSubtitleDocument?.Serialize();}
        _previewSourceRevision=revision;return _previewSource;
    }
    private byte[]? _decodedPixels;
    public event EventHandler? FrameReady;
    private bool _disposed;
    private EventEditDraft? _draft;
    private readonly EventEditDraft _emptyDraft=new(AssDocument.CreateEmpty().NewEvent());
    public EventEditDraft EditorDraft=>Draft??_emptyDraft;
    public string VisualText=>_gesture is not null?SelectedEvent?.Text??"":Draft?.Text??"";
    private CancellationTokenSource? _editBurstDelay;
    public IEditorDialogs? Dialogs { get; set; }
    public SubtitleEditor? ActiveEditor => _activeId is { } id && _documents.TryGetValue(id,out var state) ? state.Editor : null;
    public CommandRegistry Registry => _registry;
    public (double Start,double Span) AudioViewport=>_activeId is {} id&&_documents.TryGetValue(id,out var state)?(state.AudioStart,state.AudioSpan):(0,20);
    public void RememberAudioViewport(double start,double span){if(_activeId is {} id&&_documents.TryGetValue(id,out var state)){state.AudioStart=start;state.AudioSpan=span;}}
    public HotkeyResolver Hotkeys { get; } = new();
    public ObservableCollection<DocumentTabViewModel> Tabs { get; } = [];
    public Dictionary<string,ICommand> Actions { get; } = [];
    public IReadOnlyList<string> RecentFiles => _settings.RecentFiles;
    private readonly EventSelection _selection=[];
    public IList<AssEvent> SelectedEvents=>_selection;
    public void SetSelectedEvents(IEnumerable<AssEvent> lines) { _selection.Replace(lines); if (_automationCatalog is not null) ScheduleAutomationValidation(); }
    public bool IsSynchronizingSelection { get; private set; }
    public int TextCursor { get; set; }
    public EventEditDraft? Draft
    {
        get => _draft;
        private set
        {
            if (_draft is not null) _draft.PropertyChanged -= DraftChanged;
            SetField(ref _draft,value);OnPropertyChanged(nameof(EditorDraft));
            if (_draft is not null) _draft.PropertyChanged += DraftChanged;
            RefreshFormatting();
        }
    }
    private void DraftChanged(object? sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName is nameof(EventEditDraft.IsChanged) or nameof(EventEditDraft.Duration))return;
        try{SelectedEvent?.ShowDraft(Draft?.Values);}catch(ArgumentException){/* Invalid metadata remains editable, never saved. */}
        InvalidateSubtitlePreview(true);OnPropertyChanged(nameof(PreviewRevision));
        _editBurstDelay?.Cancel();_editBurstDelay?.Dispose();var delay=new CancellationTokenSource();_editBurstDelay=delay;
        _=FinalizeBurstAsync(delay.Token);
        if (_workspace.ActiveDocument is {} session) session.IsDirty=(ActiveEditor?.IsDirty??false)||(Draft?.IsChanged??false);
        _registry.NotifyStateChanged();
        if(e.PropertyName is nameof(EventEditDraft.Text) or nameof(EventEditDraft.Style))RefreshFormatting();
    }
    private async Task FinalizeBurstAsync(CancellationToken token)
    {
        try{await Task.Delay(900,token);Avalonia.Threading.Dispatcher.UIThread.Post(()=>{if(!token.IsCancellationRequested&&!_disposed)CommitDraft();});}catch(OperationCanceledException){}
    }
    public IReadOnlyList<AssEvent> Events => _activeSubtitleDocument?.Events ?? (IReadOnlyList<AssEvent>)Array.Empty<AssEvent>();
    public IReadOnlyList<string> StyleNames => _activeId is {} id&&_documents.TryGetValue(id,out var state)?state.StyleNames:[];
    public IReadOnlyList<string> ActorNames => Events.Select(l=>l.Actor).Where(s=>s.Length>0).Distinct().Order().ToArray();
    public AssEvent? SelectedEvent
    {
        get => _selectedEvent;
        set
        {
            if (ReferenceEquals(_selectedEvent,value)) return;
            CancelGesture();
            if (!CommitDraft()) { OnPropertyChanged(); return; }
            _selectedEvent?.ShowDraft(null);_selectedEvent=value; _activeSubtitleDocument?.UpdateCurrentEvent(value); ReloadDraft();
            // A programmatic jump outside the bulk selection must select its
            // destination. Moving the current row inside that selection keeps it.
            if(value is null)SetSelectedEvents([]);
            else if(!SelectedEvents.Contains(value))SetSelectedEvents([value]);
            OnPropertyChanged(); OnPropertyChanged(nameof(HasSelectedEvent));OnPropertyChanged(nameof(SelectedIsComment));
            if (value?.StartMilliseconds is { } start && !IsPlaying) CurrentTimeSeconds=start/1000d;
            _registry.NotifyStateChanged();
        }
    }
    public bool HasSelectedEvent => SelectedEvent is not null;
    public bool SelectedIsComment=>SelectedEvent?.IsComment??false;
    public string? ActiveSubtitlePath => _workspace.ActiveDocument?.Path;
    public string SuggestedSubtitleFileName => _workspace.ActiveDocument?.Title is { Length:>0 } title ? (Path.HasExtension(title) ? title : title+".ass") : "subtitle.ass";
    public string SubtitleStatus { get=>_subtitleStatus; private set=>SetField(ref _subtitleStatus,value); }
    public string MediaStatus { get=>_mediaStatus; private set=>SetField(ref _mediaStatus,value); }
    public string ActiveVisualTool { get=>_activeVisualTool; private set{SetField(ref _activeVisualTool,value);OnPropertyChanged(nameof(IsPositionTool));OnPropertyChanged(nameof(IsClipTool));} }
    public bool IsPositionTool=>ActiveVisualTool=="Position";
    public bool IsClipTool=>ActiveVisualTool=="Clip";
    public long PreviewRevision => Volatile.Read(ref _subtitleRevision);
    public string TimeDisplay => AssTime.Format((long)(CurrentTimeSeconds*1000));
    public string DurationDisplay => AssTime.Format((long)(MediaDurationSeconds*1000));
    public WriteableBitmap? VideoFrame
    {
        get=>_videoFrame;
        private set { if (ReferenceEquals(_videoFrame,value)) return; var old=_videoFrame; _videoFrame=value; OnPropertyChanged(); old?.Dispose(); }
    }
    public WaveformData? WaveformSamples { get=>_waveformSamples; private set=>SetField(ref _waveformSamples,value); }
    public double MediaDurationSeconds { get=>_mediaDurationSeconds; private set { SetField(ref _mediaDurationSeconds,Math.Max(0,value)); OnPropertyChanged(nameof(DurationDisplay)); } }
    public double CurrentTimeSeconds
    {
        get=>_currentTimeSeconds;
        set
        {
            var time=MediaDurationSeconds>0 ? Math.Clamp(value,0,MediaDurationSeconds) : Math.Max(0,value);
            if (!double.IsFinite(time) || Math.Abs(_currentTimeSeconds-time)<0.00001) return;
            CancelGesture();
            _currentTimeSeconds=time; OnPropertyChanged(); OnPropertyChanged(nameof(TimeDisplay));OnPropertyChanged(nameof(FramePositionDisplay));OnPropertyChanged(nameof(RelativeTimingDisplay));
            _activeSubtitleDocument?.UpdateActiveTime((long)(time*1000));
            if (!_clockUpdateFromPlayback && _media?.HasVideo==true) _=RefreshVideoFrameAsync(time);
        }
    }
    public MainWindowViewModel(CommandRegistry registry, WorkspaceManager workspace, UndoManager undo, IThemeService theme, SettingsStore settingsStore, AppSettings settings)
    {
        _registry=registry; _workspace=workspace; _theme=theme; _settingsStore=settingsStore; _settings=settings.Normalize();
        RegisterEditorCommands();
        InitializeAutomationCommands();
        _registry.CommandFailed+=(_,e)=>SubtitleStatus=e.Exception.Message;
        _workspace.Changed+=OnWorkspaceChanged;
        CreateNewDocument();
    }
    public bool CommitDraft()
    {
        if(_gesture is not null){SubtitleStatus="Finish or cancel the current gesture first.";return false;}
        if (Draft?.IsChanged!=true || SelectedEvent is null || ActiveEditor is null) return true;
        try { ActiveEditor.EditEvent(SelectedEvent,Draft.Values); ReloadDraft(); return true; }
        catch(Exception e) { SubtitleStatus=e.Message; return false; }
    }
    private void ReloadDraft()
    {
        _editBurstDelay?.Cancel();_editBurstDelay?.Dispose();_editBurstDelay=null;
        SelectedEvent?.ShowDraft(null);Draft=SelectedEvent is {} line?new EventEditDraft(line):null;
        if(AssVisualTags.Clip(Draft?.Text??"") is {} clip)InverseClip=clip.Inverse;
        InvalidateSubtitlePreview(true);OnPropertyChanged(nameof(PreviewRevision));OnPropertyChanged(nameof(SelectedIsComment));
        if(_workspace.ActiveDocument is {} session)session.IsDirty=ActiveEditor?.IsDirty??false;
    }
    private void CreateNewDocument()
    {
        CancelGesture();
        if (!CommitDraft()) return;
        var session=_workspace.CreateUntitled(); Attach(session,AssDocument.CreateEmpty()); SynchronizeActiveDocument(); SubtitleStatus="New ASS document";
    }
    private void Attach(DocumentSession session, AssDocument document)
    {
        var state=new DocumentState(new SubtitleEditor(document)){AudioSpan=AudioWindowSeconds}; _documents[session.Id]=state;
        document.Changed+=(_,_)=> { session.IsDirty=true; if (_activeId==session.Id) { InvalidateSubtitlePreview(true); OnPropertyChanged(nameof(PreviewRevision)); } };
        state.Editor.Undo.Changed+=(_,_)=>
        {
            session.IsDirty=state.Editor.IsDirty;
            var names=document.Styles.Select(s=>s.Name).ToArray();var namesChanged=!names.SequenceEqual(state.StyleNames);
            if(namesChanged)state.StyleNames=names;
            if(_activeId==session.Id)
            {
                // Refresh choices before the draft binding selects the renamed
                // style. Otherwise ComboBox retains an absent/blank selection.
                if(namesChanged)OnPropertyChanged(nameof(StyleNames));
                ReloadDraft();_registry.NotifyStateChanged();
            }
        };
        state.Editor.MarkSaved();
    }
    public bool OpenSubtitle(string path)
    {
        CancelGesture();
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
        CancelGesture();
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
        CancelGesture();
        if (!CommitDraft()) return false;
        if (!_workspace.Activate(id)) return false;
        if (state.Editor.IsDirty)
        {
            var choice=Dialogs is null ? UnsavedChoice.Cancel : await Dialogs.ConfirmUnsavedAsync(_workspace.ActiveDocument!.Title);
            if (choice==UnsavedChoice.Cancel || choice==UnsavedChoice.Save && !await SaveAsync()) return false;
        }
        CancelGesture(); StopPlayback(); state.Loading?.Cancel(); state.Loading?.Dispose();
        var media=state.Media; state.Media=null; if (media is not null) _=Task.Run(media.Dispose);
        CloseAutomationDocument(id); _documents.Remove(id); _workspace.Close(id); if (_workspace.Documents.Count==0) CreateNewDocument(); return true;
    }
    public async Task<bool> CloseWindowAsync()
    {
        foreach (var id in _workspace.Documents.Select(s=>s.Id).ToArray()) if (!await CloseDocumentAsync(id)) return false;
        return true;
    }
    private void OnWorkspaceChanged(object? sender,EventArgs e) { SynchronizeTabs(); SynchronizeActiveDocument(); ScheduleAutomationValidation(); OnPropertyChanged(nameof(AutomationScripts)); OnPropertyChanged(nameof(AutomationMacros)); }
    private void SynchronizeActiveDocument()
    {
        var id=_workspace.ActiveDocumentId;
        if (id==_activeId && ActiveEditor is not null && ReferenceEquals(_activeSubtitleDocument,ActiveEditor.Document)) return;
        IsSynchronizingSelection=true;
        try
        {
            CancelGesture(); CancelVisualBounds(); StopPlayback(); Interlocked.Increment(ref _seekGeneration);
            if (_activeId is { } old && _documents.TryGetValue(old,out var previous)) { previous.Time=CurrentTimeSeconds; previous.Selected=SelectedEvent; }
            _activeId=id;
            var state=id is { } key && _documents.TryGetValue(key,out var found) ? found : null;
            _activeSubtitleDocument=state?.Editor.Document; _media=state?.Media;
            _selectedEvent=state?.Selected ?? _activeSubtitleDocument?.Events.FirstOrDefault(); SetSelectedEvents(_selectedEvent is null?Array.Empty<AssEvent>():new[]{_selectedEvent});
            _activeSubtitleDocument?.UpdateCurrentEvent(_selectedEvent); ReloadDraft(); VideoFrame=null; WaveformSamples=state?.Waveform; MediaDurationSeconds=_media?.Info.DurationSeconds ?? 0;
            _currentTimeSeconds=state?.Time ?? 0;
            foreach(var name in new[]{nameof(Events),nameof(SelectedEvent),nameof(HasSelectedEvent),nameof(StyleNames),nameof(ActorNames),nameof(ActiveSubtitlePath),nameof(SuggestedSubtitleFileName),nameof(CurrentTimeSeconds),nameof(TimeDisplay),nameof(CanPlayMedia),nameof(FramePositionDisplay),nameof(RelativeTimingDisplay),nameof(FrameTimes),nameof(Keyframes),nameof(AudioHorizontalZoom),nameof(AudioWindowSeconds)}) OnPropertyChanged(name);
            MediaStatus=_media?.SourcePath ?? "Open video/audio"; InvalidateSubtitlePreview(true); _registry.NotifyStateChanged();
        }
        finally{IsSynchronizingSelection=false;}
    }
    public async Task<bool> OpenMediaAsync(string path)
    {
        if (_activeId is not { } id || !_documents.TryGetValue(id,out var state)) return false;
        CancelGesture(); StopPlayback(); state.Loading?.Cancel(); state.Loading?.Dispose(); var cts=new CancellationTokenSource(); state.Loading=cts; var token=cts.Token;
        MediaStatus=$"FFMS2 indexing {Path.GetFileName(path)}窶ｦ";
        FfmsMediaSession? opened=null;
        try
        {
            var job=_jobs.Run("Open media",async ct=> { opened=await Task.Run(()=>FfmsMediaSession.Open(path,ct),ct); },token); await job.Completion;
            token.ThrowIfCancellationRequested();
            if (_disposed || !_documents.ContainsKey(id)) { opened?.Dispose(); return false; }
            if(_activeId==id){CancelGesture();CancelVisualBounds();}
            var old=state.Media; state.Media=opened; opened=null; state.Time=0; state.Waveform=null; if (old is not null) _=Task.Run(old.Dispose);
            if (_activeId==id) { _media=state.Media;if(!_media!.HasVideo)VideoFrame=null; MediaDurationSeconds=_media.Info.DurationSeconds; CurrentTimeSeconds=0; WaveformSamples=state.Waveform; OnPropertyChanged(nameof(CanPlayMedia));OnPropertyChanged(nameof(FrameTimes));OnPropertyChanged(nameof(Keyframes));OnPropertyChanged(nameof(FramePositionDisplay)); MediaStatus=Path.GetFileName(path); await RefreshVideoFrameAsync(0); }
            var media=state.Media!; WaveformData? peaks=null;
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
        var generation=Interlocked.Increment(ref _seekGeneration); var subtitleText=PreviewSource(); var revision=Volatile.Read(ref _subtitleRevision);
        try
        {
            await _videoRequestGate.WaitAsync();
            try
            {
                if (_disposed || generation!=Volatile.Read(ref _seekGeneration) || revision!=Volatile.Read(ref _subtitleRevision) || !ReferenceEquals(session,_media)) return;
                var result=await Task.Run(()=> { var frame=session.GetFrameAtTime(seconds,_decodedPixels);_decodedPixels=frame.Pixels; return (Frame:frame,Error:CompositeSubtitles(frame,seconds,subtitleText,revision)); });
                if (_disposed || generation!=Volatile.Read(ref _seekGeneration) || revision!=Volatile.Read(ref _subtitleRevision) || !ReferenceEquals(session,_media)) return;
                var frame=result.Frame;
                if(VideoFrame is not {} bitmap||bitmap.PixelSize.Width!=frame.Width||bitmap.PixelSize.Height!=frame.Height)VideoFrame=CreateBitmap(frame);
                else { using var buffer=bitmap.Lock();for(var y=0;y<frame.Height;y++)Marshal.Copy(frame.Pixels,y*frame.Stride,buffer.Address+y*buffer.RowBytes,frame.Width*4); }
                DisplayedPreviewRevision=revision;OnPropertyChanged(nameof(DisplayedPreviewRevision));FrameReady?.Invoke(this,EventArgs.Empty);
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
        if (!_disposed) DisposeAutomation();
        if (_disposed) return; CancelVisualBounds();_boundsCancellation?.Dispose();_=Task.Run(_geometryProvider.Dispose); _editBurstDelay?.Cancel();_editBurstDelay?.Dispose();CancelGesture(); _previewDelay?.Cancel();_previewDelay?.Dispose();_previewDelay=null;_spectrumCache.Clear(); _disposed=true; StopPlayback(); _jobs.CancelAll(); Interlocked.Increment(ref _seekGeneration); _workspace.Changed-=OnWorkspaceChanged;
        foreach(var state in _documents.Values) { state.Loading?.Cancel(); state.Loading?.Dispose(); if (state.Media is {} media) _=Task.Run(media.Dispose); }
        foreach(var tab in Tabs) tab.Dispose(); Tabs.Clear(); DisposeSubtitleRenderer(); VideoFrame=null;
    }
    private void SetField<T>(ref T field,T value,[CallerMemberName]string? name=null) { if(EqualityComparer<T>.Default.Equals(field,value))return; field=value; OnPropertyChanged(name); }
    private void OnPropertyChanged([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(name));
    public event PropertyChangedEventHandler? PropertyChanged;
}
