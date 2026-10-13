using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yoake.Core.Commands;
using Yoake.Core.Logging;
using Yoake.Core.Media;
using Yoake.Core.Subtitles;
using Yoake.UI;
using Yoake.UI.Controls;
using Yoake.UI.ViewModels;

namespace Yoake.App;

// Separate packaged native process. CI kills it only after production autosave;
// another independently launched process restores the raw draft and slot plan.
internal sealed class KaraokeUiAcceptanceVerification(IClassicDesktopStyleApplicationLifetime desktop, MainWindow window, MainWindowViewModel model, StartupOptions options)
{
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMilliseconds(120)};
    private bool _ready,_ticking,_tagMenuOpened;private int _stage;private Task<bool>? _media;
    private SubtitleEditor? _editor;private string _source="",_committed="",_plan="";private Guid _document;
    private object? _waveform,_times,_keyframes;private byte[]? _pixels;private Pointer? _pointer;
    private string Script=>Path.Combine(options.ProfileDirectory!,"karaoke.ass");
    private AudioWaveformControl Audio=>window.FindControl<AudioWaveformControl>("AudioDisplay")!;
    public void Start(){window.LayoutUpdated+=(_,_)=>_ready=window.IsVisible&&window.Bounds.Width>0;_timer.Tick+=Tick;_timer.Start();}
    private async void Tick(object? sender,EventArgs args)
    {
        if(!_ready||_ticking)return;_ticking=true;
        try
        {
            if(options.KaraokeRecovery){await Recover();return;}
            switch(_stage++)
            {
                case 0:
                    Require(!model.Actions["audio/karaoke"].CanExecute(null),"Karaoke is disabled without audio.");
                    File.WriteAllText(Script,"[Script Info]\nScriptType: v4.00+\nPlayResX: 320\nPlayResY: 180\nTitle: Toshiki 日本語\n[V4+ Styles]\nFormat: Name,Fontname,Fontsize,PrimaryColour,SecondaryColour,Alignment,MarginL,MarginR,MarginV\nStyle: Default,Arial,24,&H00FFFFFF,&H000000FF,5,0,0,0\n[Events]\nFormat: "+string.Join(',',AssDocument.EventFormat)+"\nDialogue: 0,0:00:00.00,0:00:02.00,Default,,0,0,0,,{\\future(x)}さまよう\nDialogue: 0,0:00:02.00,0:00:03.00,Default,,0,0,0,,<病|やまい>\nComment: 0,0:00:00.00,0:00:03.00,Default,,0,0,0,template,{\\distort(1,2,3,4,5,6,7,8)}literal\\|\\pipes\n[X-Opaque]\nPreserve: value\n");
                    Require(model.OpenSubtitle(Script),"Open native Japanese timing fixture.");_media=model.OpenMediaAsync(Path.Combine(options.VerificationMedia!,"av.avi"));return;
                case 1:
                    if(_media?.IsCompleted!=true){_stage--;return;}Require(await _media,"Native audio/video provider loaded.");await Invoke(CommandIds.ViewAudioVideo);
                    model.AudioWindowSeconds=2.5;_editor=model.ActiveEditor;_document=model.ActiveDocumentId!.Value;_source=_editor!.Document.Serialize();_waveform=model.WaveformSamples;_times=model.FrameTimes;_keyframes=model.Keyframes;
                    window.FindControl<MenuItem>("AudioMenu")!.IsSubMenuOpen=true;return;
                case 2:
                    Press(window.FindControl<MenuItem>("KaraokeMenu")!,Key.Enter);
                    Require(model.IsKaraokeMode&&model.Actions["audio/karaoke"].IsChecked&&model.KaraokeSlots.Length==3,"Actual menu activates checked Toshiki and song-sane slots.");return;
                case 3:
                    var slots=window.FindControl<KaraokeSlotsControl>("KaraokeSlots")!;
                    Require(slots.IsEffectivelyVisible&&slots.Bounds.Height>=40&&Audio.Bounds.Height>40,"Karaoke and waveform are usable above the text editor.");
                    slots.GetVisualDescendants().OfType<ComboBox>().Single().SetCurrentValue(SelectingItemsControl.SelectedItemProperty,"kf");
                    var cuts=slots.Cuts;var width=new FormattedText("さま",System.Globalization.CultureInfo.CurrentCulture,FlowDirection.LeftToRight,Typeface.Default,12,Brushes.White).Width;
                    var first=new FormattedText("よ",System.Globalization.CultureInfo.CurrentCulture,FlowDirection.LeftToRight,Typeface.Default,12,Brushes.White).Width;
                    Click(cuts,new(4+24+6+width+first,10));
                    Require(model.KaraokePlan!.Count==4&&model.KaraokePlan.Slots[3].Text=="う","Real grapheme splitter separates the held vowel without inventing timing.");
                    Require(_editor!.Document.Serialize()==_source&&!_editor.Undo.CanUndo,"Slot and tag changes are uncommitted.");return;
                case 4:
                    Drag(.4,.5,true);Require(model.KaraokePlan!.Boundaries.SequenceEqual(new long[]{500}),"Pointer places and drags an explicit boundary.");
                    Drag(.5,.65,false);Require(model.KaraokePlan.Boundaries.SequenceEqual(new long[]{500}),"Capture loss restores the pending timing checkpoint.");
                    Drag(.5,.7,false,true);Require(!model.HasGesture&&model.KaraokePlan.Boundaries[0]==500,"Actual Escape cancels and releases pointer capture.");
                    Drag(1.2,1.2,true);Require(model.KaraokePlan.Boundaries.SequenceEqual(new long[]{500,1200}),"Next pending slot receives only an explicit boundary.");return;
                case 5:
                    var cursor=model.KaraokePlan!.Current;Click(Audio,new((.2-Audio.ViewportStart)/Audio.VisibleSeconds*Audio.Bounds.Width,Audio.Bounds.Height/2));
                    Require(model.ActiveAudioPlayback is {StartSeconds:0,EndSeconds:.5}&&model.KaraokePlan.Current==cursor,"Timed click auditions the existing central range without moving the slot cursor.");model.Actions["audio/stop"].Execute(null);
                    Press(Audio,Key.Z);model.Actions["audio/stop"].Execute(null);Require(model.KaraokePlan.Current==1,"Existing Audio Z selects the previous assigned group.");
                    Press(Audio,Key.Add);Require(model.KaraokePlan.Boundaries[1]==1210,"Existing keypad Plus adjusts this group's explicit end by 10ms.");
                    Press(Audio,Key.Subtract);Require(model.KaraokePlan.Boundaries[1]==1200,"Existing keypad Minus preserves total timing.");return;
                case 6:
                    await Invoke("video/jump",new VideoJumpRequest(Time:"0:00:00.75"));
                    _pixels=(await model.ExtractCurrentVideoFrameAsync(FrameImageKind.Subtitles)).Image.Pixels;Require(_pixels.Where((_,i)=>i%4==3).Any(a=>a>0),"Mangetsu produces actual live karaoke coverage.");
                    Drag(.5,.85,true);var changed=(await model.ExtractCurrentVideoFrameAsync(FrameImageKind.Subtitles)).Image.Pixels;
                    Require(!changed.SequenceEqual(_pixels),"Changing the pending boundary changes actual Mangetsu pixels at the same mapped frame.");return;
                case 7:
                    var text=window.FindControl<AssTextBox>("SubtitleText")!;Require(text.Focus(),"Real text editor focuses.");var presenter=text.GetVisualDescendants().OfType<TextPresenter>().First();presenter.PreeditText="日本";
                    Press(text,Key.Enter);Require(presenter.PreeditText=="日本"&&_editor!.Document.Serialize()==_source,"IME Enter never commits karaoke or source.");presenter.PreeditText=null;
                    model.Draft!.MarginL="incomplete";Require(!model.Actions["karaoke/commit"].CanExecute(null),"Invalid raw draft disables commit without losing timing.");model.Draft.MarginL="0";model.Draft.Actor="歌手";
                    window.FindControl<MenuItem>("AudioMenu")!.IsSubMenuOpen=true;return;
                case 8:
                    Require(window.FindControl<MenuItem>("KaraokeMenu")!.IsChecked,"Real native menu check reflects mode.");window.FindControl<MenuItem>("AudioMenu")!.IsSubMenuOpen=false;
                    Press(Audio,Key.Enter);Require(model.SelectedEvent==model.Events[1]&&model.Events[0].Actor=="歌手"&&model.Events[0].Text.Contains("\\kf85"),"Audio Enter commits the whole pending draft and timing, then advances.");
                    _committed=_editor!.Document.Serialize();Press(window.FindControl<ListBox>("SubtitleRows")!,Key.Z,KeyModifiers.Control);
                    Require(_editor.Document.Serialize()==_source&&!_editor.Undo.CanUndo,"One real Undo restores exact ASS source and metadata.");
                    Press(window.FindControl<ListBox>("SubtitleRows")!,Key.Y,KeyModifiers.Control);Require(_editor.Document.Serialize()==_committed,"Real Redo restores karaoke source.");return;
                case 9:
                    model.SelectedEvent=model.Events[1];Require(model.KaraokePlan!.Text=="やまい"&&model.KaraokePlan.Count==3,"Renderer furigana exposes reading slots.");
                    if(!_tagMenuOpened)
                    {
                        Drag(2.3,2.3,true);RightClick(2.3);Require(model.KaraokePlan.Current==0,"Ctrl+right on a boundary selects its left timing group, matching the source.");
                        _tagMenuOpened=true;_stage--;return;
                    }
                    var menu=Audio.ContextMenu!;Require(menu.IsOpen,"Ctrl+right opens the real per-group tag menu.");
                    Press(menu.Items.OfType<MenuItem>().Single(m=>m.Header as string=="\\ko"),Key.Enter);
                    Require(model.KaraokePlan.Slots[0].Tag=="ko"&&model.KaraokePlan.Slots[1].Tag=="k","Real context-menu tag choice changes only the selected group.");
                    var bar=window.FindControl<KaraokeSlotsControl>("KaraokeSlots")!;Press(bar.GetVisualDescendants().OfType<Button>().Single(b=>b.Name=="KaraokeRest"),Key.Enter);
                    Require(model.KaraokePlan.Count==4&&model.KaraokePlan.Slots.Any(s=>s.Text.Length==0),"Real Rest button inserts a timing slot without corrupting ruby.");
                    Press(bar.GetVisualDescendants().OfType<Button>().Single(b=>b.Name=="KaraokeCancel"),Key.Enter);Require(model.KaraokePlan.Count==3&&model.KaraokePlan.Boundaries.Count==0,"Actual Cancel restores slot planning without modifying source.");
                    await Invoke("audio/view/spectrum");return;
                case 10:
                    if(!Audio.SpectrumTileReady){_stage--;return;}Drag(2.3,2.3,true);Require(model.KaraokePlan!.Boundaries[0]==2300,"Real spectrogram uses the same timing input.");
                    _plan=model.KaraokePlan.PreviewText;
                    Require(await model.Registry.InvokeAsync(CommandIds.SubtitleNew,new()),"Open another actual document tab.");Require(!model.IsKaraokeMode&&model.KaraokeSlots.Length==0,"New tab has independent karaoke state.");
                    await Invoke(CommandIds.WorkspaceActivateTab,_document);Require(model.KaraokePlan!.PreviewText==_plan&&model.IsKaraokeMode,"Returning to the tab retains pending explicit slots and provider.");
                    Require(ReferenceEquals(_editor,model.ActiveEditor)&&ReferenceEquals(_waveform,model.WaveformSamples)&&ReferenceEquals(_times,model.FrameTimes)&&ReferenceEquals(_keyframes,model.Keyframes),"Timing retains editor, native media and external timeline state.");return;
                case 11:
                    await Invoke("audio/close");Require(!model.HasAudio&&model.VideoSourcePath is not null&&model.VideoFrame is not null&&!model.ShowKaraoke&&!model.Actions["karaoke/commit"].CanExecute(null)&&model.KaraokePlan!.PreviewText==_plan,"Audio Close hides/disables input and retains video and pending plans.");
                    Require(model.Actions["karaoke/cancel"].CanExecute(null)&&model.Actions["audio/karaoke"].CanExecute(null),"Cancellation and mode exit remain accessible without reopening audio.");
                    _media=model.OpenAudioAsync(Path.Combine(options.VerificationMedia!,"av.avi"));return;
                case 12:
                    if(_media?.IsCompleted!=true){_stage--;return;}Require(await _media,"Audio reopens independently.");Require(model.ShowKaraoke&&model.KaraokePlan!.PreviewText==_plan,"Reopening audio resumes the same timing plan.");
                    _editor!.SetField(model.SelectedEvent!,"Name","newer external metadata","External edit");
                    Require(!model.Actions["karaoke/commit"].CanExecute(null)&&model.KaraokePlan!.PreviewText==_plan,"Newer metadata fences the whole commit while retaining unique timing.");
                    _editor.Undo.Undo();Require(model.Actions["karaoke/commit"].CanExecute(null),"Restoring the captured source resolves the timing conflict.");
                    model.Draft!.MarginV="unfinished";await Invoke("settings/autosave",new AutosaveOptions(true,1,null));return;
                case 13:
                    var entries=await model.ListRecoveryAsync();var entry=entries.FirstOrDefault(e=>e.Metadata?.OriginalPath==Script&&e.Metadata.Karaoke?.Any(p=>p.Row==1&&p.Boundaries.SequenceEqual(new long[]{2300}))==true&&e.Metadata.Draft?.Values.GetValueOrDefault("MarginV")=="unfinished");
                    if(entry is null){_stage--;return;}
                    Require(File.ReadAllText(Script)==_source,"Original file was not overwritten by uncommitted karaoke or autosave.");
                    _timer.Stop();options.FinishVerification("READY: real Toshiki native menu, checked/enabled controls, source-derived kana/ruby slots, real text splitter/rest/cancel, waveform/spectrum click/drag/capture loss/Escape, keyboard adjustment/commit/undo/redo, live Mangetsu pixels, audition through the central clock, IME/invalid draft isolation, document tabs and Audio Close/reopen; production autosave holds explicit pending boundaries plus raw invalid draft.\n");return;
            }
        }
        catch(Exception error){_timer.Stop();Program.Fatal("Packaged Toshiki acceptance stage "+(_stage-1),error);desktop.Shutdown(1);}
        finally{_ticking=false;}
    }
    private async Task Recover()
    {
        switch(_stage++)
        {
            case 0:await Invoke("settings/autosave",new AutosaveOptions(false,60,null));window.FindControl<MenuItem>("FileMenu")!.IsSubMenuOpen=true;return;
            case 1:Press(window.FindControl<MenuItem>("RecoveryMenu")!,Key.Enter);return;
            case 2:
                var recovery=window.OwnedWindows.OfType<RecoveryWindow>().SingleOrDefault();if(recovery is null){_stage--;return;}
                var versions=recovery.FindControl<ListBox>("RecoveryVersions")!;var rows=versions.Items.OfType<RecoveryEntry>().ToArray();
                var selected=rows.FirstOrDefault(e=>e.Metadata?.Karaoke?.Any(k=>k.Row==1&&k.Boundaries.SequenceEqual(new long[]{2300}))==true);
                if(selected is null){var groups=recovery.FindControl<ListBox>("RecoveryFiles")!;groups.SelectedIndex=0;_stage--;return;}
                versions.SelectedItem=selected;Press(recovery.FindControl<Button>("RecoveryOpen")!,Key.Enter);return;
            case 3:
                if(model.KaraokePlan is null){_stage--;return;}
                Require(model.ActiveSubtitlePath is null&&model.RecoveryProvenance==Script&&model.IsKaraokeMode&&!model.ShowKaraoke,"Restart recovery creates an unsaved owned tab without native providers.");
                Require(model.Draft!.MarginV=="unfinished"&&model.KaraokePlan.Text=="やまい"&&model.KaraokePlan.Boundaries.SequenceEqual(new long[]{2300}),"Raw invalid draft and exact uncommitted reading timing survive forced termination.");
                Require(model.ActiveEditor!.Document.Serialize().Contains("[X-Opaque]")&&model.Events[2].Text.Contains("\\distort(1,2,3,4,5,6,7,8)"),"Unknown source and Mangetsu template remain lossless.");
                Require(File.ReadAllText(Script).Contains("さまよう")&&!File.ReadAllText(Script).Contains("\\kf85"),"Original subtitle is untouched across both processes.");
                Require(StartupDiagnostics.BindingWarningCount==0&&StartupDiagnostics.FrameworkErrorCount==0,"No native binding/framework failures.");
                _timer.Stop();options.FinishVerification("PASS: independent native restart recovery of document-owned Toshiki slot plan, explicit boundaries/tag state and invalid raw draft through actual File recovery menu and native lists/buttons; original file, unknown metadata and Mangetsu template untouched; NativeAOT source-generated metadata.\n");StartupDiagnostics.Complete();desktop.Shutdown(0);return;
        }
    }
    private async Task Invoke(string id,object? value=null)=>Require(await model.Registry.InvokeAsync(id,new(model.ActiveDocumentId),value),"Native preparation: "+id);
    private void Drag(double from,double to,bool release,bool escape=false)
    {
        var control=Audio;var local=new Point((from-control.ViewportStart)/control.VisibleSeconds*control.Bounds.Width,control.Bounds.Height/2);var point=control.TranslatePoint(local,window)!.Value;var end=point+new Vector((to-from)/control.VisibleSeconds*control.Bounds.Width,0);
        _pointer=new Pointer(937,PointerType.Mouse,true);control.RaiseEvent(new PointerPressedEventArgs(control,_pointer,window,point,0,new(RawInputModifiers.LeftMouseButton,PointerUpdateKind.LeftButtonPressed),KeyModifiers.None));Require(model.HasGesture,"Real audio pointer acquires a karaoke checkpoint.");
        control.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent,control,_pointer,window,end,0,new(RawInputModifiers.LeftMouseButton,PointerUpdateKind.Other),KeyModifiers.None));
        if(escape)Press(control,Key.Escape);
        else if(release)control.RaiseEvent(new PointerReleasedEventArgs(control,_pointer,window,end,0,new(RawInputModifiers.None,PointerUpdateKind.LeftButtonReleased),KeyModifiers.None,MouseButton.Left));
        else _pointer.Capture(null);
        Require(!model.HasGesture&&_pointer.Captured is null,"Released/lost/canceled pointer cannot leave a timing drag stuck.");
    }
    private void Click(Control control,Point local)
    {
        var point=control.TranslatePoint(local,window)!.Value;var pointer=new Pointer(938,PointerType.Mouse,true);
        control.RaiseEvent(new PointerPressedEventArgs(control,pointer,window,point,0,new(RawInputModifiers.LeftMouseButton,PointerUpdateKind.LeftButtonPressed),KeyModifiers.None));
        control.RaiseEvent(new PointerReleasedEventArgs(control,pointer,window,point,0,new(RawInputModifiers.None,PointerUpdateKind.LeftButtonReleased),KeyModifiers.None,MouseButton.Left));
    }
    private void RightClick(double seconds)
    {
        var point=Audio.TranslatePoint(new Point((seconds-Audio.ViewportStart)/Audio.VisibleSeconds*Audio.Bounds.Width,Audio.Bounds.Height/2),window)!.Value;
        var pointer=new Pointer(939,PointerType.Mouse,true);
        Audio.RaiseEvent(new PointerPressedEventArgs(Audio,pointer,window,point,0,new(RawInputModifiers.RightMouseButton|RawInputModifiers.Control,PointerUpdateKind.RightButtonPressed),KeyModifiers.Control));
        Audio.RaiseEvent(new PointerReleasedEventArgs(Audio,pointer,window,point,0,new(RawInputModifiers.Control,PointerUpdateKind.RightButtonReleased),KeyModifiers.Control,MouseButton.Right));
    }
    private static void Press(Control control,Key key,KeyModifiers modifiers=KeyModifiers.None)
    {
        if(control is ListBox list)control=(Control?)list.ContainerFromIndex(Math.Max(0,list.SelectedIndex))??throw new InvalidOperationException("Realized subtitle row is required.");
        (TopLevel.GetTopLevel(control) as Window)?.Activate();Require(control.Focus()&&control.IsKeyboardFocusWithin,"Real native control must own keyboard focus: "+control.Name);
        control.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Source=control,Key=key,KeyModifiers=modifiers});
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
