using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using Yoake.Core.Commands;
using Yoake.Core.Hotkeys;
using Yoake.Core.Media;
using Yoake.Core.Subtitles;
using Yoake.UI;
using Yoake.UI.ViewModels;

namespace Yoake.App;

internal sealed class TimecodeUiAcceptanceVerification(IClassicDesktopStyleApplicationLifetime desktop,
    MainWindow window,MainWindowViewModel model,StartupOptions options)
{
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMilliseconds(100)};
    private bool _ready,_ticking;private int _stage;private Task<bool>? _media;
    private SubtitleEditor? _editor;private AssEvent[] _selection=[];private EventEditDraft? _draft;
    private string _source="",_path="",_save="",_start="";private double _position;
    private IReadOnlyList<double>? _providerTimes;private IReadOnlyList<int>? _keys;private object? _waveform;
    private ListBox Grid=>window.FindControl<ListBox>("SubtitleRows")!;
    public void Start(){window.LayoutUpdated+=(_,_)=>_ready=window.IsVisible&&window.Bounds.Width>0;_timer.Tick+=Tick;_timer.Start();}
    private async void Tick(object? sender,EventArgs args)
    {
        if(!_ready||_ticking)return;_ticking=true;options.MarkStartupVerified();
        try
        {
            switch(_stage++)
            {
                case 0:
                    Require(window.TryGetPlatformHandle() is not null&&model.ExternalTimecodePath is null&&!model.CanShiftByFrames,"Restored recents must not automatically open timecodes or media.");
                    if(model.RecentTimecodeItems.Count>0)Require(model.RecentTimecodeItems.Count==2&&window.FindControl<MenuItem>("RecentTimecodesMenu")!.IsEnabled,"Independent timecode recents must restore in the next process.");
                    _path=Path.Combine(options.ProfileDirectory!,"日本語.tc.txt");_save=Path.Combine(options.ProfileDirectory!,"export.tc.txt");File.WriteAllText(_path,"Assume 25\n0,1,50\n");
                    var subtitle=Path.Combine(options.ProfileDirectory!,"timecodes.ass");
                    File.WriteAllText(subtitle,"[Events]\nFormat: "+string.Join(',',AssDocument.EventFormat)+"\n"+
                        "Dialogue: 0,0:00:01.00,0:00:02.00,Default,,0,0,0,,{\\future(x)\\distort(0,0,1,1)}日本語 é\n"+
                        "Dialogue: 0,0:00:03.00,0:00:04.00,Default,,0,0,0,,မြန်မာ 👩‍👩‍👧‍👦\n");
                    Require(model.OpenSubtitle(subtitle),"Open lossless timecode fixture.");_editor=model.ActiveEditor;_source=_editor!.Document.Serialize();_selection=model.Events.ToArray();model.SetSelectedEvents(_selection);
                    await Invoke("timecode/open",_path);return;
                case 1:
                    Require(model.CanShiftByFrames&&model.FrameTimes.Count==0&&model.Actions["timecode/save"].CanExecute(null)&&!model.Actions["time/snap/start_video"].CanExecute(null),"External maps enable frame shifting without exposing phantom video frames.");
                    _media=model.OpenMediaAsync(Path.Combine(options.VerificationMedia!,"av.avi"));return;
                case 2:
                    if(!_media!.IsCompleted){_stage--;return;}
                    Require(_media.Result&&model.FrameTimes.Count==10&&model.FrameTimes[2]==.04,"Opening video must apply already-loaded timecodes to the existing central frame map.");
                    await Invoke("timecode/close");return;
                case 3:
                    _providerTimes=model.FrameTimes;_keys=model.Keyframes;_waveform=model.WaveformSamples;model.CurrentTimeSeconds=.7;_position=model.CurrentTimeSeconds;
                    _draft=model.Draft;_start=_draft!.Start;_draft.Start="unfinished timing";
                    window.FindControl<MenuItem>("VideoMenu")!.IsSubMenuOpen=true;return;
                case 4:window.FindControl<MenuItem>("TimecodesMenu")!.IsSubMenuOpen=true;return;
                case 5:
                    var open=window.FindControl<MenuItem>("OpenTimecodesMenu")!;open.CommandParameter=_path;Press(open,Key.Enter);return;
                case 6:
                    if(model.HasTimecodeOperation){_stage--;return;}
                    Require(model.ExternalTimecodePath==_path&&model.FrameTimes[3]==.08,"Real menu Enter must import the exact V1 extrapolation behavior.");Preserved();
                    await Invoke("settings/hotkeys/set",new HotkeyChange(HotkeyContext.Default,"timecode/close",["F11"]));
                    Require(window.FindControl<MenuItem>("CloseTimecodesMenu")!.InputGesture?.Key==Key.F11,"Menus must reflect live timecode remapping.");
                    window.FindControl<MenuItem>("VideoMenu")!.IsSubMenuOpen=true;return;
                case 7:window.FindControl<MenuItem>("TimecodesMenu")!.IsSubMenuOpen=true;return;
                case 8:
                    var save=window.FindControl<MenuItem>("SaveTimecodesMenu")!;save.CommandParameter=_save;Press(save,Key.Enter);return;
                case 9:
                    if(model.HasTimecodeOperation){_stage--;return;}
                    Require(TimecodeFile.Load(_save).Milliseconds.SequenceEqual(new long[]{0,20,40,80,120,160,200,240,280,320}),"Native Save menu must extend short V1 files through the full video and export interoperable V2 timestamps.");
                    Preserved();Press(Grid,Key.F11);return;
                case 10:
                    Require(model.ExternalTimecodePath is null&&ReferenceEquals(_providerTimes,model.FrameTimes)&&!model.Actions["timecode/close"].CanExecute(null),"Real remapped close must restore the same provider timestamps.");Preserved();
                    window.FindControl<MenuItem>("VideoMenu")!.IsSubMenuOpen=true;return;
                case 11:window.FindControl<MenuItem>("TimecodesMenu")!.IsSubMenuOpen=true;return;
                case 12:window.FindControl<MenuItem>("RecentTimecodesMenu")!.IsSubMenuOpen=true;return;
                case 13:
                    var recent=window.FindControl<MenuItem>("RecentTimecodesMenu")!.Items.OfType<MenuItem>().First();
                    Require(recent.Bounds.Width>0&&recent.Bounds.Width<540&&recent.Command is Yoake.UI.Commands.RegistryCommand,"Timecode recents must be bounded native menu items invoking semantic commands.");Press(recent,Key.Enter);return;
                case 14:
                    if(model.HasTimecodeOperation){_stage--;return;}
                    Require(model.ExternalTimecodePath==_save&&model.FrameTimes[8]==.28,"Real recent menu Enter must reopen its fixed file.");Preserved();Press(Grid,Key.Left);return;
                case 15:
                    Require(model.CurrentTimeSeconds==.28&&model.CurrentFrame==8,"Real Grid Left must navigate through external timestamps rather than the provider's original PTS.");_position=model.CurrentTimeSeconds;Preserved();
                    _draft!.Start=_start;Press(Grid,Key.D3,KeyModifiers.Control);return;
                case 16:
                    if(model.HasEditOperation){_stage--;return;}
                    var timing=TimecodeFile.Load(_save).Timing;
                    Require(_selection.All(row=>row.StartMilliseconds==timing.SubtitleBoundary(8,FrameTimeKind.Start)),"Real Ctrl-3 must use the same external map for all selected rows.");Press(Grid,Key.Z,KeyModifiers.Control);return;
                case 17:
                    Require(_editor!.Document.Serialize()==_source&&!_editor.Undo.CanUndo,"External-map snapping must undo as one exact source operation.");
                    var bad=Path.Combine(options.ProfileDirectory!,"bad.tc.txt");File.WriteAllText(bad,"# timecode format v2\n0\nNaN");
                    Require(!await model.Registry.InvokeAsync("timecode/open",new(model.ActiveDocumentId),bad)&&model.ExternalTimecodePath==_save&&model.FrameTimes[8]==.28,"Invalid timecodes must retain the active map, draft, source and undo.");
                    Press(Grid,Key.F11);return;
                case 18:
                    Require(ReferenceEquals(_providerTimes,model.FrameTimes)&&ReferenceEquals(_keys,model.Keyframes)&&ReferenceEquals(_waveform,model.WaveformSamples)&&model.CurrentTimeSeconds==_position,"Closing timecodes must preserve media/provider state and playback seconds.");
                    File.WriteAllText(_path,"# timecode format v2\n"+string.Join('\n',Enumerable.Range(0,13).Select(index=>index*70)));await Invoke("timecode/open",_path);await Invoke("timecode/save",_save);
                    Require(model.FrameTimes.Count==10&&TimecodeFile.Load(_save).Milliseconds.Count==13,"Navigation must stay inside native frame count while export preserves longer external files.");Press(Grid,Key.F11);return;
                case 19:
                    Require(ReferenceEquals(_providerTimes,model.FrameTimes)&&_editor!.Document.Serialize()==_source&&!_editor.Undo.CanUndo&&model.SelectedEvents.SequenceEqual(_selection),"All metadata workflows must retain the document, multi-selection and undo.");
                    await Invoke("settings/hotkeys/reset-command",new HotkeyCommandReset(HotkeyContext.Default,"timecode/close"));
                    Require(StartupDiagnostics.BindingWarningCount==0&&StartupDiagnostics.FrameworkErrorCount==0,"Timecode workflow must have no binding/framework errors.");
                    _timer.Stop();options.FinishVerification("PASS: separate real-window timecode acceptance; pre-video imports; already-loaded metadata applied on media open; actual native menu Enter import/export and bounded recent Enter; live remapped F11 close/provider fallback; actual Grid Left and Ctrl-3 external-map navigation/snapping and exact undo; V1 Assume extrapolation, extended V2 export and longer-than-video retention; lossless Unicode/unknown/Mangetsu source, invalid draft, selection, provider/audio and playback-seconds retention; non-destructive invalid import; independent two-process MRU persistence without automatic media reopening.\n");
                    StartupDiagnostics.Complete();desktop.Shutdown(0);return;
            }
        }
        catch(Exception exception){_timer.Stop();Program.Fatal("Packaged timecode acceptance at stage "+(_stage-1),exception);desktop.Shutdown(1);}
        finally{_ticking=false;}
    }
    private void Preserved()=>Require(ReferenceEquals(_editor,model.ActiveEditor)&&ReferenceEquals(_draft,model.Draft)&&model.Draft!.Start=="unfinished timing"&&model.SelectedEvents.SequenceEqual(_selection)&&model.CurrentTimeSeconds==_position&&ReferenceEquals(_keys,model.Keyframes)&&ReferenceEquals(_waveform,model.WaveformSamples)&&_editor!.Document.Serialize()==_source&&!_editor.Undo.CanUndo,"Timecode metadata must not recreate providers, reset selection/draft, seek or mutate source/history.");
    private async Task Invoke(string id,object? parameter=null)=>Require(await model.Registry.InvokeAsync(id,new(model.ActiveDocumentId),parameter),"Timecode preparation command failed: "+id);
    private static void Press(Control control,Key key,KeyModifiers modifiers=KeyModifiers.None)
    {
        if(control is ListBox list)control=list.ContainerFromIndex(Math.Max(0,list.SelectedIndex)) as Control??throw new InvalidOperationException("A realized grid row must receive timecode keys.");
        (TopLevel.GetTopLevel(control) as Window)?.Activate();Require(control.Focus()&&control.IsKeyboardFocusWithin,"The shipped control must own keyboard focus.");
        control.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Source=control,Key=key,KeyModifiers=modifiers});
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
