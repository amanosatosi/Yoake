using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using Yoake.Core.Audio;
using Yoake.Core.Commands;
using Yoake.Core.Logging;
using Yoake.Core.Subtitles;
using Yoake.UI;
using Yoake.UI.Controls;
using Yoake.UI.ViewModels;

namespace Yoake.App;

internal sealed class AudioDisplayUiAcceptanceVerification(IClassicDesktopStyleApplicationLifetime desktop,
    MainWindow window,MainWindowViewModel model,StartupOptions options)
{
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMilliseconds(100)};
    private bool _ready,_ticking;private int _stage;private Task<bool>? _media;
    private SubtitleEditor? _editor;private string _source="";private object? _samples,_times,_keys;private double _position,_right;
    private string Marker=>Path.Combine(options.ProfileDirectory!,"audio-display-verified.txt");
    private AudioWaveformControl Audio=>window.FindControl<AudioWaveformControl>("AudioDisplay")!;
    public void Start(){window.LayoutUpdated+=(_,_)=>_ready=window.IsVisible&&window.Bounds.Width>0;_timer.Tick+=Tick;_timer.Start();}
    private async void Tick(object? sender,EventArgs args)
    {
        if(!_ready||_ticking)return;_ticking=true;options.MarkStartupVerified();
        try
        {
            switch(_stage++)
            {
                case 0:
                    Require(window.TryGetPlatformHandle() is not null&&model.VideoFrame is null&&model.WaveformSamples is null,"Audio acceptance needs a real window; presentation settings must not reopen media.");
                    if(File.Exists(Marker))Require(model.IsAudioSpectrum&&!model.AudioAutoScroll&&!model.AudioVolumeLinked&&window.FindControl<ComboBox>("AudioMode")!.SelectedIndex==1,"Audio spectrum/autoscroll/link settings and selector must restore in a new process.");
                    var path=Path.Combine(options.ProfileDirectory!,"audio-display.ass");
                    File.WriteAllText(path,"[Events]\nFormat: "+string.Join(',',AssDocument.EventFormat)+"\n"+
                        "Dialogue: 0,0:00:00.30,0:00:01.00,Default,,0,0,0,,{\\future(x)\\distort(0,0,1,1)}日本語 é\n"+
                        "Dialogue: 0,0:00:01.20,0:00:01.40,Default,,0,0,0,,မြန်မာ 👩‍👩‍👧‍👦\n");
                    Require(model.OpenSubtitle(path),"Open audio presentation fixture.");_editor=model.ActiveEditor;_source=_editor!.Document.Serialize();
                    if(model.VideoAutoSeek)await Invoke("video/autoseek");if(!model.AudioAutoScroll)await Invoke("audio/opt/autoscroll");
                    await Invoke("audio/view/waveform");
                    Require(model.IsDefaultView && !model.Actions[CommandIds.ViewAudioVideo].CanExecute(null),
                        "Empty media must not enable Audio + Video.");_media=model.OpenMediaAsync(Path.Combine(options.VerificationMedia!,"av.avi"));return;
                case 1:
                    if(!_media!.IsCompleted){_stage--;return;}
                    Require(_media.Result&&Audio.Bounds.Width>128&&model.WaveformSamples is not null&&model.FrameTimes.Count>0,"Loaded audio/video and a usable viewport are required.");
                    Require(model.IsAudioVideoView && model.Actions[CommandIds.ViewAudioVideo].CanExecute(null),
                        "Loaded audio/video must select its combined view.");
                    _samples=model.WaveformSamples;_times=model.FrameTimes;_keys=model.Keyframes;_position=model.CurrentTimeSeconds;
                    model.AudioWindowSeconds=.5;Audio.ViewportStart=.4;
                    _right=AudioViewportMath.ScrollPixels(.4,.5,Audio.Bounds.Width,128,model.MediaDurationSeconds);Press(Audio,Key.F);return;
                case 2:
                    Near(_right,Audio.ViewportStart,"Real Audio F must scroll exactly 128 display pixels at current zoom.");Preserved();Press(Audio,Key.A);return;
                case 3:
                    Near(.4,Audio.ViewportStart,"Real Audio A must scroll back through the same command resolver.");Preserved();window.FindControl<MenuItem>("AudioMenu")!.IsSubMenuOpen=true;return;
                case 4:Press(window.FindControl<MenuItem>("AudioSpectrumMenu")!,Key.Enter);return;
                case 5:
                    if(!Audio.SpectrumTileReady){_stage--;return;}
                    Require(Audio.Spectrogram&&model.IsAudioSpectrum&&window.FindControl<ComboBox>("AudioMode")!.SelectedIndex==1&&window.FindControl<MenuItem>("AudioSpectrumMenu")!.IsChecked&&!window.FindControl<MenuItem>("AudioWaveformMenu")!.IsChecked,"Native spectrum menu must generate a real tile and synchronize radio state and the existing selector.");
                    Near(.4,Audio.ViewportStart,"Changing display type must retain viewport.");Preserved();window.FindControl<MenuItem>("AudioMenu")!.IsSubMenuOpen=true;return;
                case 6:Press(window.FindControl<MenuItem>("AudioWaveformMenu")!,Key.Enter);return;
                case 7:
                    Require(!Audio.Spectrogram&&window.FindControl<ComboBox>("AudioMode")!.SelectedIndex==0&&window.FindControl<MenuItem>("AudioWaveformMenu")!.IsChecked&&!window.FindControl<MenuItem>("AudioSpectrumMenu")!.IsChecked,"Native waveform menu must restore the shared display and exclusive radio state.");
                    Preserved();await Invoke("grid/line/next");return;
                case 8:
                    Near(1.05,Audio.ViewportStart,"Autoscroll must center a newly selected fitting range using release visibility semantics.");Preserved();window.FindControl<MenuItem>("AudioMenu")!.IsSubMenuOpen=true;return;
                case 9:Press(window.FindControl<MenuItem>("AudioAutoScrollMenu")!,Key.Enter);return;
                case 10:
                    Require(!model.AudioAutoScroll&&!window.FindControl<MenuItem>("AudioAutoScrollMenu")!.IsChecked,"Actual native autoscroll menu must update the persisted checked command.");await Invoke("grid/line/previous");return;
                case 11:
                    Near(1.05,Audio.ViewportStart,"Disabling autoscroll must retain viewport during line navigation.");Preserved();await Invoke("audio/go_to/start");
                    Near(.05,Audio.ViewportStart,"Go-to-start must center the active line start.");await Invoke("audio/go_to/end");Near(.75,Audio.ViewportStart,"Go-to-end must center the active line end.");
                    await Invoke("audio/go_to");Near(.525,Audio.ViewportStart,"Go-to-range must bring the long range's visible end to its inner edge.");Preserved();
                    await Invoke(CommandIds.ViewVideo);return;
                case 12:
                    var viewport=model.AudioViewport;await Invoke(CommandIds.ViewAudioVideo);Require(model.AudioViewport==viewport,"Hiding/showing audio must retain its viewport.");Preserved();
                    window.FindControl<ComboBox>("AudioMode")!.SelectedIndex=1;Require(model.IsAudioSpectrum&&model.Registry.GetRequired("audio/view/spectrum").IsChecked(new()),"The existing selector must invoke the same semantic spectrum command.");
                    window.FindControl<MenuItem>("AudioMenu")!.IsSubMenuOpen=true;return;
                case 13:Press(window.FindControl<MenuItem>("AudioLinkMenu")!,Key.Enter);return;
                case 14:
                    Require(model.AudioVolumeLinked&&!window.FindControl<Slider>("AudioVolume")!.IsEnabled&&window.FindControl<MenuItem>("AudioLinkMenu")!.IsChecked,"Native link command must update the existing volume controls and checked state.");Preserved();
                    window.FindControl<MenuItem>("AudioMenu")!.IsSubMenuOpen=true;return;
                case 15:Press(window.FindControl<MenuItem>("AudioLinkMenu")!,Key.Enter);return;
                case 16:
                    Require(!model.AudioVolumeLinked&&window.FindControl<Slider>("AudioVolume")!.IsEnabled,"Unlinking must restore independent volume control.");Preserved();
                    Require(StartupDiagnostics.BindingWarningCount==0&&StartupDiagnostics.FrameworkErrorCount==0,"Audio presentation workflow must have no binding/framework errors.");
                    File.WriteAllText(Marker,"verified");_timer.Stop();options.FinishVerification("PASS: separate real-window audio display acceptance; actual Audio A/F contextual keys at current viewport zoom; native spectrum/waveform radio MenuItem Enter and real spectrum tile; existing selector/shared command synchronization; native autoscroll/link menu Enter and live slider state; release range/endpoint scrolling and disabled autoscroll during navigation; provider, waveform, frame map, central playback seconds, exact Unicode/unknown/Mangetsu source and undo retention; mode hiding/showing viewport retention; independent two-process display/options persistence without media reopening.\n");StartupDiagnostics.Complete();desktop.Shutdown(0);return;
            }
        }
        catch(Exception exception){_timer.Stop();Program.Fatal("Packaged audio display acceptance at stage "+(_stage-1),exception);desktop.Shutdown(1);}
        finally{_ticking=false;}
    }
    private void Preserved()=>Require(ReferenceEquals(_editor,model.ActiveEditor)&&_editor!.Document.Serialize()==_source&&!_editor.Undo.CanUndo&&ReferenceEquals(_samples,model.WaveformSamples)&&ReferenceEquals(_times,model.FrameTimes)&&ReferenceEquals(_keys,model.Keyframes)&&model.CurrentTimeSeconds==_position,"Audio presentation must retain the document/history, providers and central playback position.");
    private async Task Invoke(string id,object? parameter=null)=>Require(await model.Registry.InvokeAsync(id,new(model.ActiveDocumentId),parameter),"Audio preparation command failed: "+id);
    private static void Near(double expected,double actual,string message)=>Require(Math.Abs(expected-actual)<.000001,message+$" Expected {expected}, actual {actual}.");
    private static void Press(Control control,Key key)
    {
        (TopLevel.GetTopLevel(control) as Window)?.Activate();Require(control.Focus()&&control.IsKeyboardFocusWithin,"The shipped audio/menu control must own keyboard focus.");
        control.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Source=control,Key=key});
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
