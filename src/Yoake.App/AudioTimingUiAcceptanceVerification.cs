using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yoake.Core.Audio;
using Yoake.Core.Commands;
using Yoake.Core.Logging;
using Yoake.Core.Subtitles;
using Yoake.UI;
using Yoake.UI.Controls;
using Yoake.UI.ViewModels;

namespace Yoake.App;

// Separate from startup and playback acceptance: real focused timing keys,
// native menus and waveform pointer/capture paths with the existing providers.
internal sealed class AudioTimingUiAcceptanceVerification(IClassicDesktopStyleApplicationLifetime desktop,
    MainWindow window,MainWindowViewModel model,StartupOptions options)
{
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMilliseconds(100)};
    private bool _ready,_ticking;private int _stage;private Task<bool>? _media;
    private SubtitleEditor? _editor;private AssEvent[] _rows=[];private string _source="";
    private object? _samples,_times,_keys;
    public void Start(){window.LayoutUpdated+=(_,_)=>_ready=window.IsVisible&&window.Bounds.Width>0;_timer.Tick+=Tick;_timer.Start();}
    private async void Tick(object? sender,EventArgs args)
    {
        if(!_ready||_ticking)return;_ticking=true;
        try
        {
            switch(_stage++)
            {
                case 0:
                    var path=Path.Combine(options.ProfileDirectory!,"audio-timing.ass");
                    File.WriteAllText(path,"[Script Info]\nScriptType: v4.00+\n[V4+ Styles]\nFormat: Name, Fontname, Fontsize\nStyle: Default,Arial,24\n[Events]\nFormat: "+string.Join(',',AssDocument.EventFormat)+"\nDialogue: 0,0:00:00.30,0:00:01.40,Default,A,0001,0002,0003,fx,{\\future(x)\\distort(1,2,3,4,5,6,7,8)}日本語 é\nComment: 0,0:00:01.50,0:00:01.80,Default,B,0,0,0,,မြန်မာ\n");
                    Require(model.OpenSubtitle(path),"Open timing fixture.");_media=model.OpenMediaAsync(Path.Combine(options.VerificationMedia!,"av.avi"));return;
                case 1:
                    if(_media?.IsCompleted!=true){_stage--;return;}Require(await _media,"Real timing media must load.");await Invoke(CommandIds.ViewAudioVideo);
                    _editor=model.ActiveEditor;_rows=model.Events.ToArray();_source=_editor!.Document.Serialize();_samples=model.WaveformSamples;_times=model.FrameTimes;_keys=model.Keyframes;
                    Require(!model.AudioAutoCommit&&model.AudioAutoNext,"Exact audio timing preference defaults.");
                    Press(Audio,Key.C);Require(model.PrimaryAudioRange==new AudioClipInterval(200,1400)&&_rows[0].StartMilliseconds==300&&!_editor.Undo.CanUndo,"Real Audio C stages lead-in without editing source.");
                    Press(Audio,Key.V);Require(model.PrimaryAudioRange==new AudioClipInterval(200,1750),"Real Audio V stages lead-out.");
                    Press(Audio,Key.X);Require(ReferenceEquals(model.SelectedEvent,_rows[1])&&!_editor.Undo.CanUndo,"Real Audio X navigates without committing.");
                    Press(Audio,Key.Z);Require(model.PrimaryAudioRange==new AudioClipInterval(200,1750)&&_editor.Document.Serialize()==_source,"Real Audio Z returns to retained markers.");
                    window.FindControl<MenuItem>("AudioMenu")!.IsSubMenuOpen=true;return;
                case 2:window.FindControl<MenuItem>("AudioTimingMenu")!.IsSubMenuOpen=true;return;
                case 3:
                    Require(!window.FindControl<MenuItem>("AudioAutoCommitMenu")!.IsChecked&&window.FindControl<MenuItem>("AudioAutoNextMenu")!.IsChecked,"Actual menu option checks reflect the registry.");
                    Press(window.FindControl<MenuItem>("AudioCommitStayMenu")!,Key.Enter);
                    Require(ReferenceEquals(model.SelectedEvent,_rows[0])&&_rows[0].StartMilliseconds==200&&_rows[0].EndMilliseconds==1750&&!model.HasPendingAudioTiming,"Native menu Enter commits timing and stays.");
                    return;
                case 4:
                    Press(Grid,Key.Z,KeyModifiers.Control);Require(_editor!.Document.Serialize()==_source&&!_editor.Undo.CanUndo,"One real Ctrl-Z restores exact timing/source.");
                    model.AudioWindowSeconds=2;return;
                case 5:
                    Drag(false);Require(model.PrimaryAudioRange==new AudioClipInterval(300,1400)&&_editor!.Document.Serialize()==_source,"Capture loss restores prior markers/source.");
                    Drag(true);Require(model.PrimaryAudioRange==new AudioClipInterval(300,1600)&&_rows[0].EndMilliseconds==1400&&!_editor!.Undo.CanUndo,"Released drag remains pending with auto-commit off.");
                    Press(Audio,Key.Enter);Require(_rows[0].EndMilliseconds==1600&&ReferenceEquals(model.SelectedEvent,_rows[1]),"Real Audio Enter commits and follows auto-next.");
                    Press(Audio,Key.G,KeyModifiers.Shift);Require(model.Events.Count==3&&model.PrimaryAudioRange==new AudioClipInterval(1800,4800),"Real Shift-G commits and stages default next at the previous end.");
                    Require(model.SelectedEvent!.StartMilliseconds==1800&&model.SelectedEvent.EndMilliseconds==4800,"Last-line successor has stored default timing.");
                    window.FindControl<MenuItem>("AudioMenu")!.IsSubMenuOpen=true;return;
                case 6:window.FindControl<MenuItem>("AudioTimingMenu")!.IsSubMenuOpen=true;return;
                case 7:
                    Press(window.FindControl<MenuItem>("AudioAutoCommitMenu")!,Key.Enter);Require(model.AudioAutoCommit,"Native auto-commit menu toggles the persisted semantic option.");
                    window.FindControl<MenuItem>("AudioMenu")!.IsSubMenuOpen=true;return;
                case 8:window.FindControl<MenuItem>("AudioTimingMenu")!.IsSubMenuOpen=true;return;
                case 9:
                    Require(window.FindControl<MenuItem>("AudioAutoCommitMenu")!.IsChecked,"Native menu checks refresh after changing the option.");
                    Press(window.FindControl<MenuItem>("AudioAutoNextMenu")!,Key.Enter);Require(!model.AudioAutoNext,"Native auto-next menu toggles independently.");
                    return;
                case 10:
                    model.SelectedEvent=_rows[0];Press(Audio,Key.V);Require(_rows[0].EndMilliseconds==1950&&ReferenceEquals(model.SelectedEvent,_rows[0]),"Auto-commit applies timing without auto-next.");
                    Press(Audio,Key.G);Require(ReferenceEquals(model.SelectedEvent,_rows[0]),"Real G honors disabled auto-next.");
                    var text=window.FindControl<AssTextBox>("SubtitleText")!;var before=_editor!.Document.Serialize();var active=model.SelectedEvent;
                    Require(text.Focus()&&text.IsKeyboardFocusWithin,"The edit box must own focus before IME composition starts.");
                    var preedit=text.GetVisualDescendants().OfType<TextPresenter>().First();preedit.PreeditText="日本";
                    Press(text,Key.Enter);Require(preedit.PreeditText=="日本"&&ReferenceEquals(active,model.SelectedEvent)&&_editor.Document.Serialize()==before,"IME Enter in the edit box must preserve composition and must not leak timing commit/navigation.");
                    preedit.PreeditText=null;
                    var count=model.Events.Count;Press(text,Key.C);Require(model.Events.Count==count&&_editor.Document.Serialize()==before,"Text C must not invoke Audio lead-in.");
                    Require(ReferenceEquals(_samples,model.WaveformSamples)&&ReferenceEquals(_times,model.FrameTimes)&&ReferenceEquals(_keys,model.Keyframes)&&ReferenceEquals(_editor,model.ActiveEditor),"Timing must retain the providers, frame map and editor.");
                    var settings=new Yoake.Core.Settings.SettingsStore(Path.Combine(options.ProfileDirectory!,"settings.json")).Load();Require(settings.AudioAutoCommit==true&&settings.AudioAutoNext==false,"Options must persist through the real settings store.");
                    _=model.Registry.InvokeAsync("audio/timing/options",new(model.ActiveDocumentId));return;
                case 11:
                    var preferences=window.OwnedWindows.OfType<AudioTimingOptionsWindow>().Single();Require(preferences.IsVisible&&preferences.Bounds.Width>=360,"Timing preferences must open as a usable native dialog.");
                    Require(preferences.FindControl<NumericUpDown>("DefaultDuration")!.Value==3000m&&preferences.FindControl<NumericUpDown>("LeadIn")!.Value==100m&&preferences.FindControl<NumericUpDown>("LeadOut")!.Value==350m,"The actual preferences controls must display audited defaults.");
                    preferences.FindControl<NumericUpDown>("DefaultDuration")!.SetCurrentValue(NumericUpDown.ValueProperty,4000m);
                    preferences.FindControl<NumericUpDown>("LeadIn")!.SetCurrentValue(NumericUpDown.ValueProperty,120m);
                    preferences.FindControl<NumericUpDown>("LeadOut")!.SetCurrentValue(NumericUpDown.ValueProperty,400m);
                    Press(preferences.FindControl<Button>("CloseOptions")!,Key.Enter);return;
                case 12:
                    Require(!window.OwnedWindows.OfType<AudioTimingOptionsWindow>().Any(),"Real preferences Close button must dismiss the dialog.");
                    var saved=new Yoake.Core.Settings.SettingsStore(Path.Combine(options.ProfileDirectory!,"settings.json")).Load();Require(saved.AudioDefaultDuration==4000&&saved.AudioLeadIn==120&&saved.AudioLeadOut==400,"Actual numeric controls must route through the shared preferences command and persist immediately.");
                    Require(Audio.Bounds.Width>100&&Audio.Bounds.Height>40&&StartupDiagnostics.BindingWarningCount==0&&StartupDiagnostics.FrameworkErrorCount==0,"Ordinary CI-size layout/input must remain valid.");
                    _timer.Stop();options.FinishVerification("PASS: separate audio timing acceptance; real Audio C/V/X/Z/Enter/G/Shift-G; native commit/auto-option menu Enter and checked state; waveform drag/release/capture-loss; one-operation Ctrl-Z; transient versus stored timing; default successor; auto-commit/auto-next precedence; IME/text isolation; retained document/media/timeline; real preferences dialog/numeric-control routing and persisted settings.\n");StartupDiagnostics.Complete();desktop.Shutdown(0);return;
            }
        }
        catch(Exception exception){_timer.Stop();Program.Fatal("Packaged audio timing acceptance at stage "+(_stage-1),exception);desktop.Shutdown(1);}
        finally{_ticking=false;}
    }
    private AudioWaveformControl Audio=>window.FindControl<AudioWaveformControl>("AudioDisplay")!;
    private Control Grid=>window.FindControl<ListBox>("SubtitleRows")!;
    private void Drag(bool release)
    {
        var audio=Audio;Require(audio.Bounds.Width>100&&audio.Bounds.Height>40,"Waveform has usable geometry.");
        var end=model.PrimaryAudioRange!.Value.EndMilliseconds/1000d;
        var point=audio.TranslatePoint(new Point((end-audio.ViewportStart)/audio.VisibleSeconds*audio.Bounds.Width,audio.Bounds.Height/2),window)!.Value;
        var pointer=new Pointer(917,PointerType.Mouse,true);var moved=point+new Vector(.2/audio.VisibleSeconds*audio.Bounds.Width,0);
        audio.RaiseEvent(new PointerPressedEventArgs(audio,pointer,window,point,0,new(RawInputModifiers.LeftMouseButton,PointerUpdateKind.LeftButtonPressed),KeyModifiers.None));Require(model.HasGesture,"Actual waveform marker begins pending timing.");
        audio.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent,audio,pointer,window,moved,0,new(RawInputModifiers.LeftMouseButton,PointerUpdateKind.Other),KeyModifiers.None));
        if(release)audio.RaiseEvent(new PointerReleasedEventArgs(audio,pointer,window,moved,0,new(RawInputModifiers.None,PointerUpdateKind.LeftButtonReleased),KeyModifiers.None,MouseButton.Left));
        else pointer.Capture(null);
        Require(!model.HasGesture,"Release/capture loss ends the actual gesture.");
    }
    private async Task Invoke(string id)=>Require(await model.Registry.InvokeAsync(id,new(model.ActiveDocumentId)),"Timing preparation: "+id);
    private static void Press(Control control,Key key,KeyModifiers modifiers=KeyModifiers.None)
    {
        if(control is ListBox list)
        {
            var row=list.ContainerFromIndex(Math.Max(0,list.SelectedIndex)) as Control;
            Require(row is {Bounds.Height:>0},"A realized interactive subtitle row must exist before keyboard input.");control=row!;
        }
        (TopLevel.GetTopLevel(control) as Window)?.Activate();Require(control.Focus()&&control.IsKeyboardFocusWithin,$"The actual timing control must own focus: {control.Name ?? control.GetType().Name}; enabled={control.IsEffectivelyEnabled}, visible={control.IsEffectivelyVisible}, attached={TopLevel.GetTopLevel(control) is not null}.");control.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Source=control,Key=key,KeyModifiers=modifiers});}
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
