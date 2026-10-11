using Avalonia;
using Yoake.Core.Logging;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yoake.Core.Commands;
using Yoake.Core.Media;
using Yoake.Core.Settings;
using Yoake.Core.Subtitles;
using Yoake.UI;
using Yoake.UI.ViewModels;

namespace Yoake.App;

// Subsystem acceptance stays separate from startup and contextual-hotkey probes.
internal sealed class TimingUiAcceptanceVerification(IClassicDesktopStyleApplicationLifetime desktop,
    MainWindow window,MainWindowViewModel model,StartupOptions options)
{
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMilliseconds(100)};
    private bool _ready,_ticking,_openTimingKeysPending;private int _stage=-3;private ShiftTimesWindow? _shift;
    private SubtitleEditor? _editor;private AssEvent[] _rows=[];private string _source="";private Task<bool>? _media;
    private FrameTiming? _timing;private IReadOnlyList<double>? _providerTimes;private int _frame;
    private IReadOnlyList<int>? _providerKeys;private string _keyPath="",_savedKeys="";
    private ListBox Grid=>window.FindControl<ListBox>("SubtitleRows")!;
    private T InShift<T>(string name)where T:Control=>_shift!.FindControl<T>(name)??throw new InvalidOperationException("Missing Shift Times control: "+name);
    public void Start(){window.LayoutUpdated+=(_,_)=>_ready=window.IsVisible&&window.Bounds.Width>0;_timer.Tick+=Tick;_timer.Start();}
    private async void Tick(object? sender,EventArgs args)
    {
        if(!_ready||_ticking)return;_ticking=true;options.MarkStartupVerified();
        try
        {
            if(_openTimingKeysPending)
            {
                // Opening a new document replaces the grid ItemsSource. Wait
                // for its actual layout/container before sending real keys.
                if(Grid.ContainerFromIndex(Math.Max(0,Grid.SelectedIndex)) is not Control {Bounds.Height:>0})return;
                _openTimingKeysPending=false;Press(Grid,Key.I,KeyModifiers.Control);return;
            }
            switch(_stage++)
            {
                case -3:
                    _keyPath=Path.Combine(options.ProfileDirectory!,"before-video.key.txt");
                    File.WriteAllText(_keyPath,"# keyframe format v1\nfps 0\n0\n7\n");
                    await Invoke("keyframe/open",_keyPath);await Invoke(CommandIds.ViewVideo);return;
                case -2:
                    Require(model.Keyframes.SequenceEqual(new[]{0,7})&&model.FrameTimes.Count==0&&model.Actions["keyframe/save"].CanExecute(null),"External keyframe metadata must load and remain exportable before video.");
                    Require(window.FindControl<Yoake.UI.Controls.VideoFrameSlider>("VideoSeekBar")!.RenderedKeyframeMarks==0,"Empty video ruler must not draw external cuts without a frame map.");
                    await Invoke("keyframe/close");await Invoke(CommandIds.ViewDefault);return;
                case -1:
                    Require(model.ExternalKeyframePath is null&&model.Keyframes.Count==0&&!model.ActiveEditor!.Undo.CanUndo,"Closing metadata without video must clear it without subtitle edits.");return;
                case 0:
                    Require(window.TryGetPlatformHandle() is not null,"Timing acceptance requires the real native window.");
                    var restored=model.ShiftHistory.Count>0;
                    if(restored)Require(model.ShiftPreferences is{ByFrames:true,Frames:1,Scope:SubtitleTimingScope.Selected},"Shift preferences and history must restore on the second process launch.");
                    var path=Path.Combine(options.ProfileDirectory!,"timing.ass");
                    File.WriteAllText(path,"[Events]\nFormat: "+string.Join(',',AssDocument.EventFormat)+"\n"+
                        "Dialogue: 0,0:00:01.00,0:00:02.00,Default,,0,0,0,,{\\future(opaque)\\distort(0,0,1,1)}日本語 é\n"+
                        "Dialogue: 0,0:00:03.00,0:00:04.00,Default,,0,0,0,,မြန်မာ 👩‍👩‍👧‍👦\n");
                    Require(model.OpenSubtitle(path),"Open timing fixture.");_editor=model.ActiveEditor;_rows=model.Events.ToArray();_source=_editor!.Document.Serialize();
                    model.SetSelectedEvents(_rows);Require(!model.Actions["time/snap/start_video"].CanExecute(null),"Video-only snaps must be disabled without video.");
                    _openTimingKeysPending=true;return;
                case 1:
                    _shift=window.OwnedWindows.OfType<ShiftTimesWindow>().Single();
                    Require(_shift.IsVisible&&_shift.Bounds.Width>=640&&InShift<TextBox>("ShiftTime").IsKeyboardFocusWithin,"Ctrl-I must open a usable, focused actual shift dialog.");
                    Require(!InShift<CheckBox>("ShiftByFrames").IsEnabled,"Frame shift choice must be disabled before loading video.");
                    InShift<TextBox>("ShiftTime").Text="0:00:00.10";InShift<CheckBox>("ShiftByFrames").IsChecked=false;InShift<CheckBox>("ShiftBackward").IsChecked=false;
                    InShift<ComboBox>("ShiftScope").SelectedItem=SubtitleTimingScope.Selected;InShift<ComboBox>("ShiftFields").SelectedItem=SubtitleTimingFields.Both;
                    foreach(var name in new[]{"ShiftTime","ShiftFrames","ShiftScope","ShiftFields","ShiftHistory","ShiftApply","ShiftCancel"})Within(InShift<Control>(name),_shift!);
                    var input=InShift<TextBox>("ShiftTime");input.GetVisualDescendants().OfType<TextPresenter>().First().PreeditText="日本";
                    Press(input,Key.Enter);Require(window.OwnedWindows.Contains(_shift)&&_editor!.Document.Serialize()==_source,"IME Enter must not apply a shift or close the dialog.");
                    input.GetVisualDescendants().OfType<TextPresenter>().First().PreeditText=null;
                    Click("ShiftApply");return;
                case 2:
                    if(model.HasEditOperation){_stage--;return;}
                    Require(!window.OwnedWindows.OfType<ShiftTimesWindow>().Any()&&_rows[0].StartMilliseconds==1100&&_rows[1].StartMilliseconds==3100,"The real Apply button must shift every selected line and close after success.");
                    Require(model.SelectedEvents.Count==2&&ReferenceEquals(_editor,model.ActiveEditor),"Shift must preserve editor and multi-selection.");Press(Grid,Key.Z,KeyModifiers.Control);return;
                case 3:
                    Require(_editor!.Document.Serialize()==_source&&!_editor.Undo.CanUndo,"One Ctrl-Z must restore all timing and exact Unicode/Mangetsu source.");Press(Grid,Key.I,KeyModifiers.Control);return;
                case 4:
                    _shift=window.OwnedWindows.OfType<ShiftTimesWindow>().Single();Require(InShift<ListBox>("ShiftHistory").Items.Count>0,"Shift history must be usable in the real dialog.");
                    InShift<TextBox>("ShiftTime").Text="0:00:10.00";InShift<ComboBox>("ShiftFields").SelectedItem=SubtitleTimingFields.Start;Click("ShiftApply");return;
                case 5:
                    if(model.HasEditOperation){_stage--;return;}
                    Require(_shift!.IsVisible&&_editor!.Document.Serialize()==_source&&!_editor.Undo.CanUndo&&InShift<TextBlock>("ShiftStatus").Text!.Contains("end before its start",StringComparison.Ordinal),"Invalid shifts must remain editable, report validation, and mutate no history.");
                    Press(InShift<TextBox>("ShiftTime"),Key.Escape);return;
                case 6:
                    Require(!window.OwnedWindows.OfType<ShiftTimesWindow>().Any(),"Native Escape must cancel the modal shift workflow.");
                    _media=model.OpenMediaAsync(Path.Combine(options.VerificationMedia!,"av.avi"));return;
                case 7:
                    if(!_media!.IsCompleted){_stage--;return;}Require(_media.Result&&model.CanShiftByFrames,"Real native video must supply the frame map.");
                    if(model.VideoAutoSeek)await Invoke("video/autoseek");await Invoke(CommandIds.ViewAudioVideo);
                    await Invoke(CommandIds.VideoFrameNext);_frame=model.CurrentFrame;_providerTimes=model.FrameTimes;_timing=new(model.FrameTimes,25);
                    model.SelectedEvent=_rows[0];model.SetSelectedEvents(_rows);Press(Grid,Key.D3,KeyModifiers.Control);return;
                case 8:
                    if(model.HasEditOperation){_stage--;return;}
                    Require(_rows.All(row=>row.StartMilliseconds==_timing!.SubtitleBoundary(_frame,FrameTimeKind.Start)),"Real Ctrl-3 must snap all selected starts through the VFR START boundary.");
                    Require(model.CurrentFrame==_frame&&ReferenceEquals(_providerTimes,model.FrameTimes),"Timing commands cannot recreate the provider or seek.");Press(Grid,Key.Z,KeyModifiers.Control);return;
                case 9:
                    Require(_editor!.Document.Serialize()==_source,"Undo VFR snap exactly.");Press(Grid,Key.D4,KeyModifiers.Control);return;
                case 10:
                    if(model.HasEditOperation){_stage--;return;}
                    Require(_rows.All(row=>row.EndMilliseconds==_timing!.SubtitleBoundary(_frame,FrameTimeKind.End)),"Real Ctrl-4 must use VFR END boundaries and extend starts safely.");
                    Press(Grid,Key.Z,KeyModifiers.Control);return;
                case 11:
                    Require(_editor!.Document.Serialize()==_source,"Undo end snap exactly.");Press(Grid,Key.I,KeyModifiers.Control);return;
                case 12:
                    _shift=window.OwnedWindows.OfType<ShiftTimesWindow>().Single();Require(InShift<CheckBox>("ShiftByFrames").IsEnabled,"Loaded video must expose frame shifting.");
                    InShift<CheckBox>("ShiftByFrames").IsChecked=true;InShift<TextBox>("ShiftFrames").Text="1";InShift<ComboBox>("ShiftFields").SelectedItem=SubtitleTimingFields.Both;Click("ShiftApply");return;
                case 13:
                    if(model.HasEditOperation){_stage--;return;}
                    Require(_rows[0].StartMilliseconds!=1000&&model.ShiftPreferences is{ByFrames:true,Frames:1},"Frame shift must use the actual dialog choice and persist it.");
                    Require(ReferenceEquals(_providerTimes,model.FrameTimes)&&model.CurrentFrame==_frame,"Shifting frames must retain loaded provider state and playback position.");Press(Grid,Key.Z,KeyModifiers.Control);return;
                case 14:
                    Require(_editor!.Document.Serialize()==_source&&!_editor.Undo.CanUndo,"Frame shift must undo in one transaction.");
                    Require(model.Keyframes.Count>0,"Native timing fixture must expose scene keyframes.");Press(Grid,Key.D5,KeyModifiers.Control);return;
                case 15:
                    if(model.HasEditOperation){_stage--;return;}
                    var previous=model.Keyframes.Where(key=>key<=_frame).DefaultIfEmpty(0).Last();var next=model.Keyframes.FirstOrDefault(key=>key>_frame,_timing!.Count);
                    Require(_rows.All(row=>row.StartMilliseconds==_timing!.SubtitleBoundary(previous,FrameTimeKind.Start)&&row.EndMilliseconds==_timing.SubtitleBoundary(next-1,FrameTimeKind.End)),"Real Ctrl-5 must use the scene containing the current frame.");
                    Press(Grid,Key.Z,KeyModifiers.Control);return;
                case 16:
                    Require(_editor!.Document.Serialize()==_source,"Scene snap must undo exactly.");Press(Grid,Key.D6,KeyModifiers.Control);return;
                case 17:
                    if(model.HasEditOperation){_stage--;return;}
                    var target=_timing!.SubtitleBoundary(_frame,FrameTimeKind.Start);
                    Require(_rows[0].StartMilliseconds==target&&_rows[0].EndMilliseconds==target+1000&&_rows[1].StartMilliseconds==target+2000&&_rows[1].EndMilliseconds==target+3000,"Real Ctrl-6 must align the active start and shift the complete selection by the same amount.");
                    Require(model.SelectedEvents.Count==2&&model.CurrentFrame==_frame&&ReferenceEquals(_providerTimes,model.FrameTimes),"Scene/shift commands must retain multi-selection, frame and provider state.");
                    Press(Grid,Key.Z,KeyModifiers.Control);return;
                case 18:
                    Require(_editor!.Document.Serialize()==_source&&!_editor.Undo.CanUndo,"All timing commands must undo exact source in one operation.");
                    Require(StartupDiagnostics.BindingWarningCount==0&&StartupDiagnostics.FrameworkErrorCount==0,"Timing workflow must have no binding/framework errors.");
                    _providerKeys=model.Keyframes;_keyPath=Path.Combine(options.ProfileDirectory!,"日本語.key.txt");_savedKeys=Path.Combine(options.ProfileDirectory!,"export.key.txt");
                    Require(model.FrameTimes.Count>2,"Native fixture must contain enough frames to verify external cuts.");
                    File.WriteAllText(_keyPath,$"# WWXD log file, using qpfile format\n0 I -1\n2 I -1\n{model.FrameTimes.Count+8} I -1\n");
                    window.FindControl<MenuItem>("VideoMenu")!.IsSubMenuOpen=true;return;
                case 19:
                    window.FindControl<MenuItem>("KeyframesMenu")!.IsSubMenuOpen=true;return;
                case 20:
                    var open=window.FindControl<MenuItem>("OpenKeyframesMenu")!;open.CommandParameter=_keyPath;Press(open,Key.Enter);return;
                case 21:
                    if(model.HasKeyframeOperation){_stage--;return;}
                    Require(model.Keyframes.SequenceEqual(new[]{0,2})&&model.ExternalKeyframePath==_keyPath,"Real menu Enter must load WWXD cuts, filtering out-of-video indices only for navigation.");
                    Require(ReferenceEquals(_providerTimes,model.FrameTimes)&&model.CurrentFrame==_frame&&_editor!.Document.Serialize()==_source&&!_editor.Undo.CanUndo,"External cuts cannot reload media, seek or mutate document/history.");
                    await Invoke("settings/hotkeys/set",new HotkeyChange(Yoake.Core.Hotkeys.HotkeyContext.Default,"keyframe/close",["F12"]));
                    window.FindControl<MenuItem>("VideoMenu")!.IsSubMenuOpen=true;return;
                case 22:
                    window.FindControl<MenuItem>("KeyframesMenu")!.IsSubMenuOpen=true;return;
                case 23:
                    var save=window.FindControl<MenuItem>("SaveKeyframesMenu")!;save.CommandParameter=_savedKeys;Press(save,Key.Enter);return;
                case 24:
                    if(model.HasKeyframeOperation){_stage--;return;}
                    Require(KeyframeFile.Load(_savedKeys).SequenceEqual(new[]{0,2,model.FrameTimes.Count+8}),"Real Save menu must export the complete external metadata in interoperable Aegisub format.");
                    Require(model.RecentKeyframeItems.Any(item=>item.Path==_savedKeys),"Successful export must update the semantic recent-keyframe menu.");
                    Require(window.FindControl<MenuItem>("CloseKeyframesMenu")!.InputGesture?.Key==Key.F12,"The keyframe menu must reflect a live user binding.");Press(Grid,Key.F12);return;
                case 25:
                    Require(ReferenceEquals(_providerKeys,model.Keyframes)&&model.ExternalKeyframePath is null&&!model.Actions["keyframe/close"].CanExecute(null),"Real remapped close key must restore the same provider keyframe metadata without recreation.");
                    Require(ReferenceEquals(_providerTimes,model.FrameTimes)&&model.CurrentFrame==_frame&&ReferenceEquals(_editor,model.ActiveEditor)&&_editor!.Document.Serialize()==_source&&!_editor.Undo.CanUndo,"Keyframe load/export/close must preserve the active editor, exact source, undo, provider and frame.");
                    File.WriteAllText(_keyPath,$"# keyframe format v1\nfps 0\n{model.FrameTimes.Count+8}\n");
                    await Invoke("keyframe/open",_keyPath);return;
                case 26:
                    Require(model.Keyframes.Count==0&&model.Actions["keyframe/save"].CanExecute(null),"A full external list containing only out-of-video cuts must still be exportable.");
                    await Invoke("keyframe/save",_savedKeys);
                    Require(KeyframeFile.Load(_savedKeys).SequenceEqual(new[]{model.FrameTimes.Count+8}),"Export must retain out-of-video metadata even when no cuts are navigable.");Press(Grid,Key.F12);return;
                case 27:
                    Require(ReferenceEquals(_providerKeys,model.Keyframes)&&ReferenceEquals(_providerTimes,model.FrameTimes)&&model.CurrentFrame==_frame,"Closing an entirely out-of-video list must restore provider metadata and retain its frame map/position.");
                    await Invoke("settings/hotkeys/reset-command",new HotkeyCommandReset(Yoake.Core.Hotkeys.HotkeyContext.Default,"keyframe/close"));
                    Require(StartupDiagnostics.BindingWarningCount==0&&StartupDiagnostics.FrameworkErrorCount==0,"Keyframe menu workflow must have no binding/framework errors.");
                    _timer.Stop();options.FinishVerification("PASS: separate real-window timing acceptance; Ctrl-I; actual time/frame Shift Times controls and reusable persisted history; IME Enter and Escape; non-destructive validation; real Ctrl-3/Ctrl-4 multi-selection VFR snaps, Ctrl-5 scene boundaries and Ctrl-6 selection shift; exact transactional undo and unknown/Mangetsu/Unicode source; provider/frame retention; two-process settings restoration; actual keyframe menu keyboard loading/export, WWXD and Aegisub formats, live remapped close/provider fallback, semantic recents, pre-video metadata/empty-ruler behavior, export of entirely out-of-video lists, source/history/provider/frame retention.\n");
                    StartupDiagnostics.Complete();desktop.Shutdown(0);return;
            }
        }
        catch(Exception exception){_timer.Stop();Program.Fatal("Packaged timing acceptance at stage "+(_stage-1),exception);desktop.Shutdown(1);}
        finally{_ticking=false;}
    }
    private async Task Invoke(string id,object? parameter=null)=>Require(await model.Registry.InvokeAsync(id,new(model.ActiveDocumentId),parameter),"Timing preparation command failed: "+id);
    private void Click(string name)=>Press(InShift<Button>(name),Key.Enter);
    private static void Press(Control control,Key key,KeyModifiers modifiers=KeyModifiers.None)
    {
        if(control is ListBox list)control=list.ContainerFromIndex(Math.Max(0,list.SelectedIndex)) as Control??throw new InvalidOperationException("A realized grid row must receive timing keys.");
        (TopLevel.GetTopLevel(control) as Window)?.Activate();Require(control.Focus()&&control.IsKeyboardFocusWithin,"The shipped control must own keyboard focus.");
        control.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Source=control,Key=key,KeyModifiers=modifiers});
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private static void Within(Control control,Window owner)
    {
        var origin=control.TranslatePoint(new Avalonia.Point(0,0),owner);
        Require(origin is {} point&&control.Bounds.Width>0&&control.Bounds.Height>0&&point.X>=-1&&point.Y>=-1&&point.X+control.Bounds.Width<=owner.ClientSize.Width+1&&point.Y+control.Bounds.Height<=owner.ClientSize.Height+1,
            "Timing control must remain usable inside the dialog at the CI display size: "+control.Name);
    }
}
