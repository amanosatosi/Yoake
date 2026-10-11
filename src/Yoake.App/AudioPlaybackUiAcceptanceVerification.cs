using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Yoake.Core.Audio;
using Yoake.Core.Commands;
using Yoake.Core.Logging;
using Yoake.Core.Subtitles;
using Yoake.UI;
using Yoake.UI.Controls;
using Yoake.UI.ViewModels;

namespace Yoake.App;

// Real input and real media, without substituting a fake output device. Range
// submission and immediate stop are deterministic even on device-less CI hosts.
internal sealed class AudioPlaybackUiAcceptanceVerification(IClassicDesktopStyleApplicationLifetime desktop,
    MainWindow window,MainWindowViewModel model,StartupOptions options)
{
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMilliseconds(100)};
    private bool _ready,_ticking;private int _stage;private Task<bool>? _media;
    private AudioPlaybackPlan? _lastPlan;private SubtitleEditor? _editor;private EventEditDraft? _draft;
    private string _source="";private AssEvent[] _selection=[];private object? _samples,_times,_keys;
    private static readonly string[] Commands=["audio/play/current","audio/play/line","audio/play/selection","audio/play/selection/before","audio/play/selection/after","audio/play/selection/begin","audio/play/selection/end","audio/play/to_end","audio/play/cursor","audio/play/toggle","audio/stop"];
    public void Start()
    {
        model.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(MainWindowViewModel.ActiveAudioPlayback)&&model.ActiveAudioPlayback is {} plan)_lastPlan=plan;};
        window.StartupLayoutReady+=(_,_)=>_ready=true;_timer.Tick+=Tick;_timer.Start();
    }
    private async void Tick(object? sender,EventArgs args)
    {
        if(!_ready||_ticking)return;_ticking=true;
        try
        {
            switch(_stage++)
            {
                case 0:
                    Require(Commands.All(id=>!model.Actions[id].CanExecute(null)),"No-audio playback commands must be disabled.");
                    var script=Path.Combine(options.ProfileDirectory!,"audio-playback.ass");
                    File.WriteAllText(script,"[Script Info]\nScriptType: v4.00+\n[V4+ Styles]\nFormat: Name, Fontname, Fontsize\nStyle: Default,Arial,24\n[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\nDialogue: 0,0:00:00.30,0:00:01.40,Default,,0,0,0,,{\\future(x)\\distort(1,2,3,4,5,6,7,8)}日本語 é မြန်မာ 👩‍👩‍👧\nComment: 0,0:00:01.50,0:00:01.80,Default,,0,0,0,,Other selected row\n");
                    Require(model.OpenSubtitle(script),"Open audio playback fixture.");_media=model.OpenMediaAsync(Path.Combine(options.VerificationMedia!,"av.avi"));return;
                case 1:
                    if(_media?.IsCompleted!=true){_stage--;return;}Require(await _media,"Playback fixture must load.");await Invoke(CommandIds.ViewAudioVideo);await Invoke(CommandIds.GridSelectAll);
                    var timecodes=Path.Combine(options.ProfileDirectory!,"playback-vfr.txt");File.WriteAllText(timecodes,"# timecode format v2\n0\n400\n800\n1200\n1600\n2000\n2400\n2800\n3200\n3600\n");await Invoke("timecode/open",timecodes);
                    Require(model.MediaDurationSeconds>3.5,"Retimed video must be longer than the two-second audio fixture.");
                    _editor=model.ActiveEditor;_source=_editor!.Document.Serialize();_selection=model.SelectedEvents.ToArray();model.Draft!.Start="unfinished timing";_draft=model.Draft;
                    _samples=model.WaveformSamples;_times=model.FrameTimes;_keys=model.Keyframes;return;
                case 2:
                    Play(Key.Q,AudioPlaybackKind.Before,0,300);Play(Key.W,AudioPlaybackKind.After,1400,1900);
                    Play(Key.E,AudioPlaybackKind.Begin,300,800);Play(Key.D,AudioPlaybackKind.End,900,1400);
                    Play(Key.R,AudioPlaybackKind.Line,300,1400);Play(Key.S,AudioPlaybackKind.Selection,300,1400,true);
                    Play(Key.Space,AudioPlaybackKind.Selection,300,1400);Play(Key.T,AudioPlaybackKind.ToEnd,300,2000);
                    _lastPlan=null;Press(Audio,Key.B);Require(_lastPlan?.Kind==AudioPlaybackKind.Selection&&model.IsPlaying,"Audio B must start selection playback.");
                    Require(model.Registry.GetRequired("audio/play/toggle").IsChecked(new(model.ActiveDocumentId)),"Toggle must be checked while playing.");
                    Press(Audio,Key.B);Require(!model.IsPlaying&&!model.Actions["audio/stop"].CanExecute(null),"Audio B must stop playback and update command state.");Preserved();
                    window.FindControl<MenuItem>("AudioMenu")!.IsSubMenuOpen=true;return;
                case 3:window.FindControl<MenuItem>("AudioPlaybackMenu")!.IsSubMenuOpen=true;return;
                case 4:
                    _lastPlan=null;Press(window.FindControl<MenuItem>("AudioPlayCurrentMenu")!,Key.Enter);
                    Require(_lastPlan?.Kind==AudioPlaybackKind.Current&&model.IsPlaying,"Native playback submenu Enter must submit the fixed range.");
                    var captured=model.ActiveAudioPlayback!;model.Draft!.End="0:00:01.70";
                    Require(model.ActiveAudioPlayback==captured,"Captured selection playback must ignore endpoint edits.");Press(Audio,Key.H);model.Draft.End="0:00:01.40";Preserved();return;
                case 5:
                    CheckPlayingTimingDrag();
                    _lastPlan=null;Press(window.FindControl<AssTextBox>("SubtitleText")!,Key.Q);
                    Require(_lastPlan is null&&!model.IsPlaying,"Ordinary editor Q must not invoke the Audio preview command.");
                    Press(window.FindControl<VisualOverlayControl>("VisualOverlay")!,Key.D);
                    Require(model.ActiveVisualTool=="RotateZ"&&_lastPlan is null,"Video D must select rotate Z rather than play the Audio end preview.");
                    using(var canceled=new CancellationTokenSource())
                    {canceled.Cancel();Require(!await model.Registry.InvokeAsync("audio/play/selection",new(model.ActiveDocumentId),cancellationToken:canceled.Token),"Canceled playback invocation must not submit output.");}
                    Require(!await model.Registry.InvokeAsync("audio/play/selection",new(Guid.NewGuid())),"Foreign document playback must be rejected.");Preserved();
                    _media=model.OpenMediaAsync(Path.Combine(options.VerificationMedia!,"video.avi"));return;
                case 6:
                    if(_media?.IsCompleted!=true){_stage--;return;}Require(await _media,"Video-only fixture must load.");
                    Require(Commands.All(id=>!model.Actions[id].CanExecute(null))&&!model.IsPlaying,"Video-only media must not enable audio previews.");
                    Require(StartupDiagnostics.BindingWarningCount==0&&StartupDiagnostics.FrameworkErrorCount==0,"Playback routing must have no binding/framework errors.");
                    _timer.Stop();options.FinishVerification("PASS: separate real-window audio playback routing; real focused Q/W/E/D/R/S/Space/T/B/H; real native Playback submenu Enter; exact bounded sample ranges independent of longer VFR video; live selection end versus captured endpoint; stop/toggle enable/check state; canceled/foreign invocation; text typing and Video D isolation; no-audio/video-only state; retained draft/source/selection/undo/provider metadata. Audible output/device latency remains manual hardware QA.\n");StartupDiagnostics.Complete();desktop.Shutdown(0);return;
            }
        }
        catch(Exception exception){_timer.Stop();model.StopPlayback();Program.Fatal("Packaged audio playback acceptance at stage "+(_stage-1),exception);desktop.Shutdown(1);}
        finally{_ticking=false;}
    }
    private Control Audio=>window.FindControl<AudioWaveformControl>("AudioDisplay")!;
    private void CheckPlayingTimingDrag()
    {
        model.Draft!.Start="0:00:00.30";model.AudioWindowSeconds=2;var audio=(AudioWaveformControl)Audio;
        Require(audio.Bounds.Width>100&&audio.Bounds.Height>40,"The real waveform must have usable pointer geometry.");
        Press(audio,Key.S);Require(model.IsPlaying,"Timing drag starts with active selection playback.");
        var point=audio.TranslatePoint(new Point((1.4-audio.ViewportStart)/audio.VisibleSeconds*audio.Bounds.Width,audio.Bounds.Height/2),window)!.Value;
        var pointer=new Pointer(816,PointerType.Mouse,true);var moved=point+new Vector(.2/audio.VisibleSeconds*audio.Bounds.Width,0);
        audio.RaiseEvent(new PointerPressedEventArgs(audio,pointer,window,point,0,new(RawInputModifiers.LeftMouseButton,PointerUpdateKind.LeftButtonPressed),KeyModifiers.None));
        Require(model.HasGesture&&model.IsPlaying,"Actual marker press must begin a transaction without stopping selection playback.");
        audio.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent,audio,pointer,window,moved,0,new(RawInputModifiers.LeftMouseButton,PointerUpdateKind.Other),KeyModifiers.None));
        Require(model.PrimaryAudioRange!.Value.EndMilliseconds==1600&&model.ActiveAudioPlayback!.EndFrame==76800,"Real marker movement must update the displayed and following playback endpoint.");
        pointer.Capture(null);
        Require(!model.HasGesture&&model.IsPlaying&&model.PrimaryAudioRange!.Value.EndMilliseconds==1400&&model.ActiveAudioPlayback!.EndFrame==67200&&_editor!.Document.Serialize()==_source&&!_editor.Undo.CanUndo,"Capture loss must roll back timing and playback endpoint without stopping the activity or adding undo history.");
        Press(audio,Key.H);_draft=model.Draft;_draft!.Start="unfinished timing";Preserved();
    }
    private void Play(Key key,AudioPlaybackKind kind,long start,long end,bool follow=false)
    {
        _lastPlan=null;Press(Audio,key);
        Require(_lastPlan is {} plan&&plan.Kind==kind&&plan.StartFrame==start*48&&plan.EndFrame==end*48&&model.IsPlaying,"Real Audio "+key+" must submit the expected sample interval.");
        Require(model.Actions["audio/stop"].CanExecute(null),"Stop must be enabled during audio playback.");
        if(follow){model.Draft!.End="0:00:01.50";Require(model.ActiveAudioPlayback!.EndFrame==72000,"Selection playback must follow a valid end edit without committing the invalid start draft.");}
        Press(Audio,Key.H);if(follow)model.Draft!.End="0:00:01.40";
        Require(!model.IsPlaying&&model.ActiveAudioPlayback is null&&!model.Actions["audio/stop"].CanExecute(null),"Real Audio H must stop and clear the playback interval.");Preserved();
    }
    private void Preserved()=>Require(ReferenceEquals(_editor,model.ActiveEditor)&&ReferenceEquals(_draft,model.Draft)&&model.Draft!.Start=="unfinished timing"&&_editor!.Document.Serialize()==_source&&!_editor.Undo.CanUndo&&model.SelectedEvents.SequenceEqual(_selection)&&ReferenceEquals(_samples,model.WaveformSamples)&&ReferenceEquals(_times,model.FrameTimes)&&ReferenceEquals(_keys,model.Keyframes),"Audio previews must preserve pending draft, source, undo, selection and native media metadata.");
    private async Task Invoke(string id,object? parameter=null)=>Require(await model.Registry.InvokeAsync(id,new(model.ActiveDocumentId),parameter),"Playback preparation failed: "+id);
    private static void Press(Control control,Key key)
    {(TopLevel.GetTopLevel(control) as Window)?.Activate();Require(control.Focus()&&control.IsKeyboardFocusWithin,"Actual playback/menu control must own focus.");control.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Source=control,Key=key});}
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
