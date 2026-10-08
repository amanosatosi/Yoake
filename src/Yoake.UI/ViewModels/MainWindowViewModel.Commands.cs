using System.Windows.Input;
using Yoake.Core.Commands;
using Yoake.Core.Hotkeys;
using Yoake.Core.Subtitles;
using Yoake.Core.Undo;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed partial class MainWindowViewModel
{
    public ICommand NewDocumentCommand => Actions[CommandIds.SubtitleNew];
    public ICommand UndoCommand => Actions[CommandIds.EditUndo];
    public ICommand RedoCommand => Actions[CommandIds.EditRedo];
    public ICommand ThemeCycleCommand => Actions[CommandIds.ViewThemeCycle];
    public ICommand PositionToolCommand => Actions[CommandIds.VideoToolPosition];
    public ICommand ClipToolCommand => Actions[CommandIds.VideoToolClip];
    public IReadOnlyList<double> GridColumnWidths=>_settings.CompactGridColumnWidths??new[]{_settings.GridColumnWidths[0],36,_settings.GridColumnWidths[3],_settings.GridColumnWidths[4],_settings.GridColumnWidths[5],_settings.GridColumnWidths[6],_settings.GridColumnWidths[7]};
    public double AudioWindowSeconds {get=>_activeId is {} id&&_documents.TryGetValue(id,out var state)?state.AudioSpan:_settings.AudioWindowSeconds??20;set=>InvokeAudioSetting("audio/display/zoom",value);}
    public event EventHandler? AudioZoomChanged;
    private IUndoTransaction? _gesture;
    private SubtitleEditor? _gestureEditor;
    private AssEvent? _gestureLine;
    private string _gestureText = "";
    private long _gestureStart, _gestureEnd;
    private bool _inverseClip;
    public bool InverseClip {get=>_inverseClip;set=>SetField(ref _inverseClip,value);}
    public sealed record TimingGestureUpdate(int Part,double Delta);
    public sealed record PositionGestureUpdate(double X,double Y);
    public sealed record ClipGestureUpdate(int PointIndex,AssPoint Point,AssPoint Anchor);
    // Gesture commands are deliberately synchronous; pointer capture cannot
    // await mutations or permit deferred work to outlive its transaction.
    private void InvokeGesture(string id,object? parameter=null)
    {
        var result=_registry.InvokeAsync(id,CurrentContext(),parameter);
        if(!result.IsCompleted)throw new InvalidOperationException("Gesture command must complete synchronously.");
        result.GetAwaiter().GetResult();
    }
    public bool BeginGesture(string name){InvokeGesture(CommandIds.GestureBegin,name);return _gesture is not null;}
    public void UpdateTimingGesture(int part,double delta)=>InvokeGesture(CommandIds.GestureTiming,new TimingGestureUpdate(part,delta));
    public void UpdatePositionGesture(double x,double y)=>InvokeGesture(CommandIds.GesturePosition,new PositionGestureUpdate(x,y));
    public void UpdateClipGesture(int index,AssPoint point,AssPoint anchor)=>InvokeGesture(CommandIds.GestureClip,new ClipGestureUpdate(index,point,anchor));
    public void EndGesture()=>InvokeGesture(CommandIds.GestureCommit);
    public void CancelGesture()=>InvokeGesture(CommandIds.GestureCancel);
    private bool BeginGestureCore(string name)
    {
        CancelGesture(); if (!CommitDraft() || ActiveEditor is null || SelectedEvent is null) return false;
        _gestureEditor=ActiveEditor; _gestureLine=SelectedEvent; _gestureText=SelectedEvent.Text;
        _gestureStart=SelectedEvent.StartMilliseconds??0; _gestureEnd=SelectedEvent.EndMilliseconds??_gestureStart;
        _visualBaseline=VisibleVisualLines().ToArray();
        _gesture=_gestureEditor.Undo.BeginTransaction(name); return true;
    }
    private void UpdateTimingGestureCore(int part, double deltaSeconds)
    {
        if (_gesture is null || _gestureEditor is null || _gestureLine is null) return;
        var delta=(long)Math.Round(deltaSeconds*1000/10)*10;
        var start=part==1 ? _gestureStart : Math.Max(0,_gestureStart+delta);
        var end=part==0 ? _gestureEnd : Math.Max(start,_gestureEnd+delta);
        if (part==2 && _gestureStart+delta<0) end=_gestureEnd-_gestureStart;
        start=Math.Min(start,end); _gestureEditor.SetTiming(_gestureLine,start,end);GestureChanged();
    }
    private void UpdatePositionGestureCore(double x,double y)
    {
        if (_gesture is null || _gestureEditor is null || _gestureLine is null) return;
        _gestureEditor.SetField(_gestureLine,"Text",AssVisualTags.SetPosition(_gestureText,new(x,y)),"Position subtitle");GestureChanged();
    }
    private void UpdateClipGestureCore(int pointIndex,AssPoint point,AssPoint anchor)
    {
        if (_gesture is null || _gestureEditor is null || _gestureLine is null) return;
        var text=pointIndex==-2 ? AssVisualTags.TranslateClip(_gestureText,new(point.X-anchor.X,point.Y-anchor.Y)) : pointIndex>=0 ? AssVisualTags.MoveClipPoint(_gestureText,pointIndex,point) : AssVisualTags.SetRectangle(_gestureText,InverseClip,anchor,point);
        _gestureEditor.SetField(_gestureLine,"Text",text,"Edit subtitle clip");GestureChanged();
    }
    private void GestureChanged(){InvalidateSubtitlePreview(true);OnPropertyChanged(nameof(PreviewRevision));}
    private void EndGestureCore() { var gesture=_gesture; _gesture=null; try { gesture?.Commit(); } finally { gesture?.Dispose(); _visualBaseline=[]; ReloadDraft(); GestureEnded?.Invoke(this,EventArgs.Empty); } }
    private void CancelGestureCore() { var gesture=_gesture; _gesture=null; gesture?.Dispose(); _visualBaseline=[]; if(gesture is not null){ReloadDraft();GestureEnded?.Invoke(this,EventArgs.Empty);} }
    public (double Width,double Height) ScriptSize => (int.TryParse(_activeSubtitleDocument?.GetScriptInfo("PlayResX"),out var w)&&w>0?w:384,int.TryParse(_activeSubtitleDocument?.GetScriptInfo("PlayResY"),out var h)&&h>0?h:288);
    private AssEvent[] Selection() => SelectedEvents.Where(Events.Contains).DefaultIfEmpty(SelectedEvent).OfType<AssEvent>().Distinct().ToArray();
    private void Select(AssEvent? line) { SetSelectedEvents(line is null?Array.Empty<AssEvent>():new[]{line}); SelectedEvent=line; }
    private void Navigate(int delta)
    {
        if(!CommitDraft())return;
        var i=SelectedEvent is null ? -1 : Events.ToList().IndexOf(SelectedEvent);
        if(Events.Count>0)Select(Events[Math.Clamp(i+delta,0,Events.Count-1)]);
    }
    private void RunEdit(Action<SubtitleEditor> edit)
    {
        if(!CommitDraft() || ActiveEditor is null)return; CancelGesture();
        IsSynchronizingSelection=true;
        try{edit(ActiveEditor);}finally{IsSynchronizingSelection=false;}
        if(SelectedEvent is not null && !Events.Contains(SelectedEvent))Select(Events.FirstOrDefault());
        SetSelectedEvents(SelectedEvents.Where(Events.Contains).ToArray());
        ReloadDraft(); OnPropertyChanged(nameof(SelectedEvent)); OnPropertyChanged(nameof(ActorNames));
    }
    private void ApplyHistory(bool redo)
    {
        var previous=SelectedEvent is null?0:Events.ToList().IndexOf(SelectedEvent);
        IsSynchronizingSelection=true;
        try{if(redo)_undo.Redo();else _undo.Undo();}
        finally{IsSynchronizingSelection=false;}
        if(SelectedEvent is null||!Events.Contains(SelectedEvent))Select(Events.ElementAtOrDefault(Math.Max(0,Math.Min(previous,Events.Count-1))));
        SetSelectedEvents(SelectedEvents.Where(Events.Contains).ToArray());
        ReloadDraft();OnPropertyChanged(nameof(SelectedEvent));OnPropertyChanged(nameof(StyleNames));OnPropertyChanged(nameof(ActorNames));
    }
    private void RegisterEditorCommands()
    {
        void R(string id,string label,Func<CommandInvocation,ValueTask> action,Func<bool>? available=null)
        {
            _registry.Register(new AppCommand(new(id,label,label,id.Split('/')[0]),(i,_)=>action(i),_=>available?.Invoke()??true));
            Actions[id]=new RegistryCommand(_registry,id,CurrentContext);
        }
        void S(string id,string label,Action action,Func<bool>? available=null)=>R(id,label,_=>{action();return ValueTask.CompletedTask;},available);
        R("styles/layout","Remember style list proportions",i=>
        {
            if(i.Parameter is double[] weights&&weights.Length==3&&weights.All(w=>double.IsFinite(w)&&w>0))
            {_settings=(_settings with{StyleSplitWeights=(double[])weights.Clone()}).Normalize();_settingsStore.Save(_settings);}
            return ValueTask.CompletedTask;
        });
        bool HasLine()=>HasSelectedEvent && _gesture is null;
        foreach(var setting in new[]{"audio/volume","audio/mute","audio/display/height","audio/display/intensity","audio/display/zoom","audio/volume/link"})
        {var id=setting;R(id,id,i=>{SaveAudioSetting(id,i.Parameter);return ValueTask.CompletedTask;});}
        S(CommandIds.FormatBold,"Bold  Ctrl+B",()=>ToggleFormat("b"),HasLine);
        S(CommandIds.FormatItalic,"Italic  Ctrl+I",()=>ToggleFormat("i"),HasLine);
        S(CommandIds.FormatUnderline,"Underline  Ctrl+U",()=>ToggleFormat("u"),HasLine);
        S(CommandIds.FormatStrikeout,"Strikeout",()=>ToggleFormat("s"),HasLine);
        R(CommandIds.FormatFont,"Font family and size…",_=>FormatFontAsync(),HasLine);
        foreach(var item in new[]{(CommandIds.FormatPrimary,1),(CommandIds.FormatSecondary,2),(CommandIds.FormatOutline,3),(CommandIds.FormatShadow,4)})
        {var channel=item.Item2;R(item.Item1,"Choose subtitle color…",_=>FormatColorAsync(channel),HasLine);}
        S(CommandIds.FormatReset,"Reset to line style",()=>FormatText("Reset formatting",(text,start,end,style)=>AssFormatting.Reset(text,start,end,style,ResolveFormatStyle)),HasLine);
        R(CommandIds.EditComment,"Dialogue / Comment",invocation=>{if(invocation.Parameter is bool desired)RunEdit(e=>{if(SelectedEvent!.IsComment!=desired)e.ToggleComment([SelectedEvent]);});return ValueTask.CompletedTask;},HasLine);

        void GestureStep(Action action){try{action();}catch{CancelGestureCore();throw;}}
        R(CommandIds.GestureBegin,"Begin editor gesture",i=>{BeginGestureCore(i.Parameter as string??"Edit subtitle");return ValueTask.CompletedTask;});
        R(CommandIds.GestureTiming,"Drag subtitle timing",i=>{if(i.Parameter is TimingGestureUpdate p)GestureStep(()=>UpdateTimingGestureCore(p.Part,p.Delta));return ValueTask.CompletedTask;});
        R(CommandIds.GesturePosition,"Drag subtitle position",i=>{if(i.Parameter is PositionGestureUpdate p)GestureStep(()=>UpdatePositionGestureCore(p.X,p.Y));return ValueTask.CompletedTask;});
        R(CommandIds.GestureClip,"Drag subtitle clip",i=>{if(i.Parameter is ClipGestureUpdate p)GestureStep(()=>UpdateClipGestureCore(p.PointIndex,p.Point,p.Anchor));return ValueTask.CompletedTask;});
        R("video/visual/update","Update visual typesetting gesture",i=>{if(i.Parameter is Func<VisualLine,string> edit)GestureStep(()=>UpdateVisualGestureCore(edit));return ValueTask.CompletedTask;});
        S(CommandIds.GestureCommit,"Commit editor gesture",EndGestureCore);
        S(CommandIds.GestureCancel,"Cancel editor gesture",CancelGestureCore);
        R(CommandIds.GridColumnWidths,"Resize subtitle columns",i=>
        {
            if(i.Parameter is double[] widths&&widths.Length==7&&widths.All(w=>double.IsFinite(w)&&w>=24&&w<=600))
            {_settings=_settings with{CompactGridColumnWidths=(double[])widths.Clone()};_settingsStore.Save(_settings);}
            return ValueTask.CompletedTask;
        });
        S(CommandIds.SubtitleNew,"New  Ctrl+N",CreateNewDocument);
        R(CommandIds.SubtitleOpen,"Open…  Ctrl+O",async i=>{var path=i.Parameter as string ?? (Dialogs is null?null:await Dialogs.OpenSubtitleAsync()); if(path is not null&&OpenSubtitle(path)&&_media is null&&FindAssociatedMedia(path) is {} mediaPath)await OpenMediaAsync(mediaPath);});
        R(CommandIds.SubtitleSave,"Save  Ctrl+S",async _=>{await SaveAsync();});
        R(CommandIds.SubtitleSaveAs,"Save As…  Ctrl+Shift+S",async _=>{await SaveAsync(true);});
        R(CommandIds.SubtitleClose,"Close tab  Ctrl+W",async i=>{if(i.Parameter is Guid id)await CloseDocumentAsync(id);else if(_activeId is {} active)await CloseDocumentAsync(active);});
        R(CommandIds.SubtitleRevert,"Reload from disk…",async _=>{if(!CommitDraft() || ActiveSubtitlePath is not {} path || Dialogs is null || !await Dialogs.ConfirmRevertAsync())return; var doc=AssDocument.Load(path); var session=_workspace.ActiveDocument!; var state=_documents[session.Id]; Attach(session,doc); var replacement=_documents[session.Id]; replacement.Media=state.Media; replacement.Waveform=state.Waveform; replacement.Time=state.Time; state.Loading?.Cancel(); _activeId=null; SynchronizeActiveDocument();},()=>ActiveSubtitlePath is not null);
        R(CommandIds.WorkspaceActivateTab,"Activate tab",i=>{CancelGesture();if(i.Parameter is Guid id && CommitDraft())_workspace.Activate(id);return ValueTask.CompletedTask;});
        S(CommandIds.EditUndo,"Undo  Ctrl+Z",()=>{var wasGesture=_gesture is not null;CancelGesture();if(wasGesture)return; if(Draft?.IsChanged==true){ReloadDraft();return;} ApplyHistory(false);},()=>Draft?.IsChanged==true || _undo.CanUndo || _gesture is not null);
        S(CommandIds.EditRedo,"Redo  Ctrl+Y",()=>{CancelGesture(); if(CommitDraft())ApplyHistory(true);},()=>_undo.CanRedo);
        S(CommandIds.EditCommit,"Commit line  Ctrl+Enter",()=>CommitDraft(),HasLine);
        S(CommandIds.EditCommitNext,"Commit and next  Enter",()=>{if(CommitDraft()){if(SelectedEvent==Events.LastOrDefault())RunEdit(e=>Select(e.Insert(SelectedEvent,true,SelectedEvent?.EndMilliseconds??0)));else Navigate(1);}},HasLine);
        S(CommandIds.EditCancel,"Cancel edit / gesture  Esc",()=>{CancelGesture(); ReloadDraft();});
        S(CommandIds.GridLineNext,"Next subtitle  Alt+Down",()=>Navigate(1));
        S(CommandIds.GridLinePrevious,"Previous subtitle  Alt+Up",()=>Navigate(-1));
        S(CommandIds.GridInsertBefore,"Insert before  Ctrl+Insert",()=>RunEdit(e=>Select(e.Insert(SelectedEvent,false,(long)(CurrentTimeSeconds*1000)))));
        S(CommandIds.GridInsertAfter,"Insert after  Insert",()=>RunEdit(e=>Select(e.Insert(SelectedEvent,true,SelectedEvent?.EndMilliseconds??(long)(CurrentTimeSeconds*1000)))));
        S(CommandIds.GridDuplicate,"Duplicate  Ctrl+D",()=>RunEdit(e=>{var copies=e.Duplicate(Selection()); Select(copies.FirstOrDefault());}),HasLine);
        S(CommandIds.GridDelete,"Delete selected  Delete",()=>RunEdit(e=>{var index=SelectedEvent is null?0:Events.ToList().IndexOf(SelectedEvent); e.Delete(Selection());Select(Events.ElementAtOrDefault(Math.Min(index,Events.Count-1)));}),HasLine);
        S(CommandIds.GridToggleComment,"Dialogue / Comment",()=>RunEdit(e=>e.ToggleComment(Selection())),HasLine);
        S(CommandIds.GridSplit,"Split at cursor  Ctrl+Shift+D",()=>RunEdit(e=>Select(e.Split(SelectedEvent!,TextCursor))),HasLine);
        S(CommandIds.GridJoin,"Join selected lines",()=>RunEdit(e=>e.Join(Selection())),HasLine);
        S(CommandIds.GridMoveUp,"Move up  Alt+Shift+Up",()=>RunEdit(e=>e.Move(Selection(),-1)),HasLine);
        S(CommandIds.GridMoveDown,"Move down  Alt+Shift+Down",()=>RunEdit(e=>e.Move(Selection(),1)),HasLine);
        S(CommandIds.GridSelectAll,"Select all  Ctrl+A",()=>SetSelectedEvents(Events));
        R(CommandIds.GridCopy,"Copy lines  Ctrl+C",async _=>{if(CommitDraft()&&Dialogs is not null&&ActiveEditor is {} e)await Dialogs.WriteClipboardAsync(e.Copy(Selection()));},HasLine);
        R(CommandIds.GridCut,"Cut lines  Ctrl+X",async _=>{if(CommitDraft()&&Dialogs is not null&&ActiveEditor is {} e){await Dialogs.WriteClipboardAsync(e.Copy(Selection()));RunEdit(editor=>editor.Delete(Selection()));}},HasLine);
        R(CommandIds.GridPaste,"Paste lines  Ctrl+V",async _=>{if(Dialogs is not null&&await Dialogs.ReadClipboardAsync() is {} text)RunEdit(e=>Select(e.Paste(text,SelectedEvent).FirstOrDefault()));});
        R(CommandIds.MediaOpen,"Open video/audio…",async _=>{var path=Dialogs is null?null:await Dialogs.OpenMediaAsync();if(path is not null)await OpenMediaAsync(path);});
        // Playback is a continuing activity; registry invocation completes once started.
        S(CommandIds.VideoPlay,"Play / Pause  Space",()=>{if(IsPlaying)StopPlayback();else _=StartPlaybackAsync();},()=>CanPlayMedia);
        S(CommandIds.VideoStop,"Stop  Escape",StopPlayback,()=>CanPlayMedia);
        S(CommandIds.AudioPlayCursor,"Play from cursor",()=>{_ = StartPlaybackAsync();},()=>CanPlayMedia);
        S(CommandIds.AudioPlaySelection,"Play current line  R",()=>{if(!CommitDraft()||SelectedEvent is null)return;StopPlayback();CurrentTimeSeconds=(SelectedEvent.StartMilliseconds??0)/1000d;_=StartPlaybackAsync((SelectedEvent.EndMilliseconds??0)/1000d);},()=>CanPlayMedia&&HasLine());
        R(CommandIds.VideoFrameNext,"Next frame  Right",async _=>await StepFrameAsync(1),()=>_media?.HasVideo==true);
        R("video/seek/frame","Seek to frame",async i=>{if(i.Parameter is int frame)await SeekFrameAsync(frame);},()=>_media?.HasVideo==true);
        foreach(var delta in new[]{-1,1}){var step=delta;R(step>0?"video/frame/next-keyframe":"video/frame/previous-keyframe","Step keyframe",async _=>await SeekFrameAsync(Yoake.Core.Media.FrameNavigation.StepKeyframe(Keyframes,CurrentFrame,step)),()=>_media?.HasVideo==true);}
        R(CommandIds.VideoFramePrevious,"Previous frame  Left",async _=>await StepFrameAsync(-1),()=>_media?.HasVideo==true);
        R(CommandIds.VideoSeek,"Seek to exact time",async i=>{if(i.Parameter is string text&&AssTime.TryParse(text,out var ms))await SeekPlaybackAsync(ms/1000d);else throw new ArgumentException("Enter h:mm:ss.cc");},()=>CanPlayMedia);
        S(CommandIds.TimingSetStart,"Set start to current time  Ctrl+3",()=>RunEdit(e=>{var line=SelectedEvent!;using var t=e.Undo.BeginTransaction("Set line start");e.SetTiming(line,(long)(CurrentTimeSeconds*1000),Math.Max((long)(CurrentTimeSeconds*1000),line.EndMilliseconds??0));t.Commit();}),HasLine);
        S(CommandIds.TimingSetEnd,"Set end to current time  Ctrl+4",()=>RunEdit(e=>{var line=SelectedEvent!;using var t=e.Undo.BeginTransaction("Set line end");e.SetTiming(line,Math.Min((long)(CurrentTimeSeconds*1000),line.StartMilliseconds??0),(long)(CurrentTimeSeconds*1000));t.Commit();}),HasLine);
        S(CommandIds.TimingJumpStart,"Jump to line start",()=>{if(SelectedEvent?.StartMilliseconds is {} ms){StopPlayback();CurrentTimeSeconds=ms/1000d;}},HasLine);
        S(CommandIds.TimingJumpEnd,"Jump to line end",()=>{if(SelectedEvent?.EndMilliseconds is {} ms){StopPlayback();CurrentTimeSeconds=ms/1000d;}},HasLine);
        R(CommandIds.StylesManage,"Styles Manager…",async _=>{if(CommitDraft()&&Dialogs is not null&&ActiveEditor is {} editor){await Dialogs.ShowStylesAsync(editor);OnPropertyChanged(nameof(StyleNames));ReloadDraft();}});
        R(CommandIds.ScriptInfoEdit,"Script Info…",async _=>{if(CommitDraft()&&Dialogs is not null&&ActiveEditor is {} editor)await Dialogs.ShowScriptInfoAsync(editor);});
        R(CommandIds.EditFind,"Find / Replace…  Ctrl+F",async _=>{if(CommitDraft()&&Dialogs is not null)await Dialogs.ShowFindAsync(this);});
        S(CommandIds.AudioZoomIn,"Zoom audio in  +",()=>AudioWindowSeconds=Math.Max(0.02,AudioWindowSeconds/2));
        S(CommandIds.AudioZoomOut,"Zoom audio out  -",()=>AudioWindowSeconds=Math.Min(3600,AudioWindowSeconds*2));
        foreach(var name in VisualToolNames){var tool=name;S("video/tool/"+(tool=="Clip"?"clip":tool=="Position"?"position":tool.ToLowerInvariant()),tool+" tool",()=>SelectVisualTool(tool));}
        foreach(var name in VectorModes){var mode=name;S("video/vector/"+mode.ToLowerInvariant(),"Vector clip: "+mode,()=>{CancelGesture();VectorMode=mode;});}
        S("video/position/toggle-move","Toggle position / movement",ToggleVisualMove,HasLine);
        S("video/clip/invert","Invert selected clips",InvertVisualClip,HasLine);
        S(CommandIds.ViewThemeCycle,"Cycle theme",()=>{_settings=_settings with{Theme=_theme.Next()};_settingsStore.Save(_settings);});
        void H(string id,string key,KeyModifiers modifiers=KeyModifiers.None,HotkeyContext context=HotkeyContext.Always)=>Hotkeys.Add(new(id,context,new(key,modifiers)));
        H(CommandIds.SubtitleNew,"N",KeyModifiers.Control);H(CommandIds.SubtitleOpen,"O",KeyModifiers.Control);H(CommandIds.SubtitleSave,"S",KeyModifiers.Control);H(CommandIds.SubtitleSaveAs,"S",KeyModifiers.Control|KeyModifiers.Shift);H(CommandIds.SubtitleClose,"W",KeyModifiers.Control);
        H(CommandIds.EditUndo,"Z",KeyModifiers.Control);H(CommandIds.EditRedo,"Y",KeyModifiers.Control);H(CommandIds.EditRedo,"Z",KeyModifiers.Control|KeyModifiers.Shift);H(CommandIds.EditFind,"F",KeyModifiers.Control);
        H(CommandIds.FormatBold,"B",KeyModifiers.Control,HotkeyContext.SubtitleEdit);H(CommandIds.FormatItalic,"I",KeyModifiers.Control,HotkeyContext.SubtitleEdit);H(CommandIds.FormatUnderline,"U",KeyModifiers.Control,HotkeyContext.SubtitleEdit);
        H(CommandIds.EditCommit,"Enter",KeyModifiers.Control);H(CommandIds.EditCommitNext,"Enter",context:HotkeyContext.SubtitleEdit);H(CommandIds.EditCommitNext,"Enter",context:HotkeyContext.SubtitleGrid);
        H(CommandIds.EditCancel,"Escape");H(CommandIds.VideoStop,"Escape",context:HotkeyContext.Video);H(CommandIds.VideoStop,"Escape",context:HotkeyContext.Audio);
        foreach(var context in new[]{HotkeyContext.Video,HotkeyContext.Audio,HotkeyContext.SubtitleGrid}){H(CommandIds.VideoPlay,"Space",context:context);H(CommandIds.AudioPlaySelection,"R",context:context);}
        H(CommandIds.VideoFrameNext,"Right",context:HotkeyContext.Video);H(CommandIds.VideoFramePrevious,"Left",context:HotkeyContext.Video);
        H(CommandIds.GridLineNext,"Down",KeyModifiers.Alt);H(CommandIds.GridLinePrevious,"Up",KeyModifiers.Alt);H(CommandIds.GridDuplicate,"D",KeyModifiers.Control);H(CommandIds.GridSplit,"D",KeyModifiers.Control|KeyModifiers.Shift);
        H(CommandIds.GridInsertAfter,"Insert",context:HotkeyContext.SubtitleGrid);H(CommandIds.GridInsertBefore,"Insert",KeyModifiers.Control,HotkeyContext.SubtitleGrid);H(CommandIds.GridDelete,"Delete",context:HotkeyContext.SubtitleGrid);
        H(CommandIds.GridMoveUp,"Up",KeyModifiers.Alt|KeyModifiers.Shift);H(CommandIds.GridMoveDown,"Down",KeyModifiers.Alt|KeyModifiers.Shift);
        H(CommandIds.GridCopy,"C",KeyModifiers.Control,HotkeyContext.SubtitleGrid);H(CommandIds.GridCut,"X",KeyModifiers.Control,HotkeyContext.SubtitleGrid);H(CommandIds.GridPaste,"V",KeyModifiers.Control,HotkeyContext.SubtitleGrid);H(CommandIds.GridSelectAll,"A",KeyModifiers.Control,HotkeyContext.SubtitleGrid);
        H(CommandIds.TimingSetStart,"D3",KeyModifiers.Control);H(CommandIds.TimingSetEnd,"D4",KeyModifiers.Control);
        H(CommandIds.AudioZoomIn,"OemPlus",context:HotkeyContext.Audio);H(CommandIds.AudioZoomOut,"OemMinus",context:HotkeyContext.Audio);
    }
    private string? FindAssociatedMedia(string subtitlePath)
    {
        var directory=Path.GetDirectoryName(subtitlePath)??"";
        foreach(var key in new[]{"Video File","Audio File"})
        {
            var reference=_activeSubtitleDocument?.GetSectionValue("[Aegisub Project Garbage]",key).Trim().Trim('"');
            if(string.IsNullOrWhiteSpace(reference)||reference.StartsWith("?dummy:",StringComparison.OrdinalIgnoreCase)||reference=="?video")continue;
            if(reference.StartsWith("?script",StringComparison.OrdinalIgnoreCase))reference=reference[7..].TrimStart('/', '\\');
            try {var candidate=Path.GetFullPath(Path.IsPathRooted(reference)?reference:Path.Combine(directory,reference));if(File.Exists(candidate))return candidate;}catch(ArgumentException){}
        }
        var stem=Path.Combine(directory,Path.GetFileNameWithoutExtension(subtitlePath));
        return new[]{".mkv",".mp4",".webm",".m2ts",".ts",".avi",".mov"}.Select(ext=>stem+ext).FirstOrDefault(File.Exists);
    }
    private async Task StepFrameAsync(int delta)
    {
        StopPlayback(); var media=_media; if(media is null)return;
        CurrentTimeSeconds=await Task.Run(()=>media.AdjacentFrameTime(CurrentTimeSeconds,delta));
        await RefreshVideoFrameAsync(CurrentTimeSeconds);
    }
    public void SelectSearchResult(SubtitleSearchMatch match) { Select(match.Line); TextCursor=match.Index; }
}
