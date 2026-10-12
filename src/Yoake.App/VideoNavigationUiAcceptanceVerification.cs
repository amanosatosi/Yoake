using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yoake.Core.Commands;
using Yoake.Core.Hotkeys;
using Yoake.Core.Logging;
using Yoake.Core.Media;
using Yoake.Core.Subtitles;
using Yoake.UI;
using Yoake.UI.Controls;
using Yoake.UI.ViewModels;
using KeyModifiers = Avalonia.Input.KeyModifiers;

namespace Yoake.App;

internal sealed class VideoNavigationUiAcceptanceVerification(IClassicDesktopStyleApplicationLifetime desktop,
    MainWindow window,MainWindowViewModel model,StartupOptions options)
{
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMilliseconds(100)};
    private bool _ready,_ticking;private int _stage;private Task<bool>? _media;
    private AssEvent[] _rows=[];private string _source="";private int _frame;private Guid? _document;
    private object? _frames,_samples,_keys;private FrameTiming? _map;
    private Control Video=>window.FindControl<VisualOverlayControl>("VisualOverlay")!;
    private Control Grid=>window.FindControl<ListBox>("SubtitleRows")!;
    public void Start(){window.LayoutUpdated+=(_,_)=>_ready=window.IsVisible&&window.Bounds.Width>0;_timer.Tick+=Tick;_timer.Start();}
    private async void Tick(object? sender,EventArgs args)
    {
        if(!_ready||_ticking)return;_ticking=true;
        try
        {
            switch(_stage++)
            {
                case 0:
                    Require(!model.Actions["video/play"].CanExecute(null)&&!model.Actions["video/jump"].CanExecute(null),"Video commands require video.");
                    var path=Path.Combine(options.ProfileDirectory!,"video-navigation.ass");
                    File.WriteAllText(path,"[Script Info]\nPlayResX: 384\nPlayResY: 288\n[Events]\nFormat: "+string.Join(',',AssDocument.EventFormat)+"\nDialogue: 0,0:00:00.30,0:00:00.60,Default,A,0001,0002,0003,fx,{\\future(x)\\distort(1,2,3,4,5,6,7,8)}日本語\nComment: 0,0:00:00.70,0:00:00.90,Default,B,0,0,0,,မြန်မာ\nDialogue: 0,0:00:01.00,0:00:01.20,Default,C,0,0,0,,last\n");
                    Require(model.OpenSubtitle(path),"Open navigation fixture.");_media=model.OpenMediaAsync(Path.Combine(options.VerificationMedia!,"av.avi"));return;
                case 1:
                    if(_media?.IsCompleted!=true){_stage--;return;}Require(await _media,"Native video navigation media must load.");await Invoke(CommandIds.ViewAudioVideo);
                    _rows=model.Events.ToArray();_source=model.ActiveEditor!.Document.Serialize();_document=model.ActiveDocumentId;
                    _frames=model.FrameTimes;_samples=model.WaveformSamples;_keys=model.Keyframes;_map=new(model.FrameTimes,24);
                    // Use a known sorted external key list; command fallback remains the
                    // video's EOF, including a native provider with no later cut.
                    var keys=Path.Combine(options.ProfileDirectory!,"navigation-keys.txt");File.WriteAllText(keys,"# keyframe format v1\nfps 0\n0\n4\n8\n");await Invoke("keyframe/open",keys);
                    await Seek(4);Press(Video,Key.Right,KeyModifiers.Shift);Require(model.CurrentFrame==8,"Actual Video Shift-Right reaches the strictly next keyframe.");
                    Press(Video,Key.Right,KeyModifiers.Shift);Require(model.CurrentFrame==model.FrameTimes.Count-1,"Next keyframe after the final cut reaches EOF.");
                    Press(Video,Key.Left,KeyModifiers.Shift);Require(model.CurrentFrame==8,"Actual Video Shift-Left chooses the preceding cut.");
                    Press(Grid,Key.Left,KeyModifiers.Shift);Require(model.CurrentFrame==4,"Grid Shift-Left uses its exact shipped context binding.");
                    await Invoke("keyframe/close");_keys=model.Keyframes;await Seek(1);
                    Press(Video,Key.Right,KeyModifiers.Alt);Require(model.CurrentFrame==Math.Min(11,model.FrameTimes.Count-1),"Alt-Right jumps the exact default ten frames.");
                    await Invoke("video/preferences/fast-step",3);await Seek(4);Press(Video,Key.Left,KeyModifiers.Alt);Require(model.CurrentFrame==1,"Alt-Left uses configured frame step.");
                    await Seek(0);Press(Video,Key.Right,KeyModifiers.Control);Require(model.CurrentFrame==Start(0),"Ctrl-Right first reaches current subtitle START frame.");
                    Press(Video,Key.Right,KeyModifiers.Control);Require(model.CurrentFrame==End(0),"Next boundary uses END frame at the exact subtitle endpoint.");
                    Press(Video,Key.Right,KeyModifiers.Control);Require(ReferenceEquals(model.SelectedEvent,_rows[1])&&model.CurrentFrame==Start(1),"Next boundary changes to the next actual row including comments.");
                    Press(Video,Key.Left,KeyModifiers.Control);Require(ReferenceEquals(model.SelectedEvent,_rows[0])&&model.CurrentFrame==End(0),"Previous boundary changes to previous row END.");
                    Press(Video,Key.D1,KeyModifiers.Control);Require(model.CurrentFrame==Start(0),"Default Ctrl-1 seeks START frame.");
                    Press(Video,Key.D2,KeyModifiers.Control);Require(model.CurrentFrame==End(0),"Default Ctrl-2 seeks END frame rather than next frame at the endpoint.");
                    await Invoke("settings/hotkeys/set",new HotkeyChange(HotkeyContext.Default,"video/focus_seek",["F10"]));
                    Press(Grid,Key.F10);Require(window.FindControl<VideoFrameSlider>("VideoSeekBar")!.IsKeyboardFocusWithin,"Focus-seek enters the actual video slider.");
                    Press(window.FindControl<VideoFrameSlider>("VideoSeekBar")!,Key.F10);Require(Grid.IsKeyboardFocusWithin,"Focus-seek returns to the previously focused input.");
                    _frame=model.CurrentFrame;Press(Video,Key.G,KeyModifiers.Control);return;
                case 2:
                    var jump=window.OwnedWindows.OfType<VideoJumpWindow>().Single();var input=jump.FindControl<TextBox>("JumpFrame")!;
                    Require(input.IsKeyboardFocusWithin&&input.Text==_frame.ToString(),"Actual jump dialog initially focuses and selects current frame.");
                    input.Text="invalid";Press(jump.FindControl<Button>("JumpConfirm")!,Key.Enter);
                    Require(window.OwnedWindows.Contains(jump)&&model.CurrentFrame==_frame,"Invalid jump input cannot close the dialog or seek.");
                    input.Text="3";Require(jump.FindControl<TextBox>("JumpTime")!.Text==AssTime.Format((long)Math.Round(model.FrameTimes[3]*1000)),"Frame input updates VFR time display.");
                    var presenter=input.GetVisualDescendants().OfType<TextPresenter>().First();presenter.PreeditText="日本";Press(input,Key.Enter);presenter.PreeditText=null;
                    Require(window.OwnedWindows.Contains(jump),"IME Enter cannot accept the jump dialog.");
                    Press(jump.FindControl<Button>("JumpConfirm")!,Key.Enter);return;
                case 3:
                    Require(!window.OwnedWindows.OfType<VideoJumpWindow>().Any()&&model.CurrentFrame==3&&window.FindControl<VideoFrameSlider>("VideoSeekBar")!.IsKeyboardFocusWithin,"Accepted jump seeks exact frame and restores slider focus.");
                    Press(Video,Key.G,KeyModifiers.Control);return;
                case 4:
                    var cancel=window.OwnedWindows.OfType<VideoJumpWindow>().Single();cancel.FindControl<TextBox>("JumpTime")!.Text="0:00:00.50";
                    Require(cancel.FindControl<TextBox>("JumpFrame")!.Text==_map!.FrameAtMilliseconds(500).ToString(),"Time input updates exact VFR frame index.");
                    Press(cancel.FindControl<Button>("JumpCancel")!,Key.Enter);return;
                case 5:
                    Require(model.CurrentFrame==3&&!window.OwnedWindows.OfType<VideoJumpWindow>().Any(),"Canceled jump preserves current frame.");
                    // Start/stop happen in the same input turn, before an audio device
                    // opens. This proves routing/range without claiming audible output.
                    Press(Video,Key.Space);Require(model.IsPlaying&&model.Actions["video/play"].IsChecked&&window.FindControl<MenuItem>("VideoPlayMenu")!.IsChecked,"Real Video Space publishes shared play/menu state.");
                    Press(Video,Key.Escape);Require(!model.IsPlaying&&!model.Actions["video/play"].IsChecked,"Video Escape stops central AV playback.");
                    await Invoke("time/lead/out");var range=model.PrimaryAudioRange!.Value;
                    var expected=FrameNavigation.LineRange(_map!,model.FrameTimes,range.StartMilliseconds,range.EndMilliseconds,model.MediaDurationSeconds)!.Value;
                    Press(Grid,Key.Home);Require(model.IsPlaying&&Math.Abs(model.CurrentTimeSeconds-expected.Start)<.000001,"Real Grid Home play-line starts at the pending primary START frame.");
                    Press(Video,Key.Escape);Require(model.HasPendingAudioTiming&&model.ActiveEditor!.Document.Serialize()==_source,"Play-line and Stop preserve pending timing/source/undo.");
                    Require(!await model.Registry.InvokeAsync("video/jump",new(Guid.NewGuid()),new VideoJumpRequest(Frame:10)),"Foreign-document seek is rejected.");
                    using(var c=new CancellationTokenSource()){c.Cancel();Require(!await model.Registry.InvokeAsync("video/jump",new(_document),new VideoJumpRequest(Frame:10),c.Token),"Canceled jump is rejected before changing frame.");}
                    Require(ReferenceEquals(_frames,model.FrameTimes)&&ReferenceEquals(_samples,model.WaveformSamples)&&ReferenceEquals(_keys,model.Keyframes)&&!model.ActiveEditor.Undo.CanUndo,"Navigation retains actual providers/timeline/history.");
                    Require(StartupDiagnostics.BindingWarningCount==0&&StartupDiagnostics.FrameworkErrorCount==0,"Navigation must not introduce native binding/layout errors.");
                    _timer.Stop();options.FinishVerification("PASS: separate real-window Video navigation acceptance; actual Video/Grid keyframe routing and EOF fallback; configured frame-based Alt steps; exact START/END subtitle boundary traversal including comment rows; Ctrl-1/Ctrl-2; seek-focus return; Ctrl-G real frame/time dialog synchronization, invalid/cancel and IME Enter isolation; Video Space/Escape shared playback state; Grid Home pending-aware frame-bounded play-line; foreign/canceled requests; native providers/frame-map/source/undo retention. Audible output and hardware playback latency remain manual QA.\n");StartupDiagnostics.Complete();desktop.Shutdown(0);return;
            }
        }
        catch(Exception exception){_timer.Stop();Program.Fatal("Packaged Video navigation acceptance at stage "+(_stage-1),exception);desktop.Shutdown(1);}
        finally{_ticking=false;}
    }
    private int Start(int row)=>Math.Clamp(_map!.FrameAtMilliseconds(_rows[row].StartMilliseconds!.Value,FrameTimeKind.Start),0,model.FrameTimes.Count-1);
    private int End(int row)=>Math.Clamp(_map!.FrameAtMilliseconds(_rows[row].EndMilliseconds!.Value,FrameTimeKind.End),0,model.FrameTimes.Count-1);
    private async Task Seek(int frame)=>await Invoke("video/seek/frame",frame);
    private async Task Invoke(string id,object? parameter=null)=>Require(await model.Registry.InvokeAsync(id,new(model.ActiveDocumentId),parameter),"Video navigation preparation: "+id);
    private static void Press(Control control,Key key,KeyModifiers modifiers=KeyModifiers.None)
    {
        if(control is ListBox list)
        {
            var row=list.ContainerFromIndex(Math.Max(0,list.SelectedIndex)) as Control;
            Require(row is {Bounds.Height:>0},"A realized interactive subtitle row must exist before keyboard input.");control=row!;
        }
        (TopLevel.GetTopLevel(control) as Window)?.Activate();Require(control.Focus()&&control.IsKeyboardFocusWithin,"Actual navigation control must own focus: "+control.Name);control.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Source=control,Key=key,KeyModifiers=modifiers});}
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
