using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yoake.Core.Commands;
using Yoake.Core.Hotkeys;
using Yoake.Core.Logging;
using Yoake.Core.Settings;
using Yoake.Core.Subtitles;
using Yoake.UI;
using Yoake.UI.Controls;
using Yoake.UI.ViewModels;
using KeyModifiers = Avalonia.Input.KeyModifiers;

namespace Yoake.App;

// A separate packaged launch exercises actual routed key events and the
// shipping settings controls. It supplements every existing startup probe.
internal sealed class HotkeyUiAcceptanceVerification(IClassicDesktopStyleApplicationLifetime desktop,
    MainWindow window,MainWindowViewModel model,StartupOptions options)
{
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMilliseconds(100)};
    private bool _opened,_loaded;private int _layouts,_stage;
    private OptionsWindow? _options;private LogWindow? _log;private Task<bool>? _media;
    private Guid? _document;private AssEvent? _line;private SubtitleEditor? _editor;
    private int _frame;private string _savedPath="";
    private const string Source=@"{\future(opaque)\distort(0,0,1,1)}日本語 မြန်မာ é 👩‍👩‍👧‍👦";
    private AssTextBox Text=>window.FindControl<AssTextBox>("SubtitleText")!;
    private ListBox Grid=>window.FindControl<ListBox>("SubtitleRows")!;
    private T InOptions<T>(string name) where T:Control=>_options!.FindControl<T>(name)??throw new InvalidOperationException("Missing settings control: "+name);

    public void Start()
    {
        window.Opened+=(_,_)=>_opened=true;window.Loaded+=(_,_)=>_loaded=true;window.LayoutUpdated+=(_,_)=>_layouts++;
        _timer.Tick+=Tick;_timer.Start();
    }
    private void Tick(object? sender,EventArgs args)
    {
        if(!_opened||!_loaded||_layouts==0)return;
        options.MarkStartupVerified();
        try
        {
            switch(_stage++)
            {
                case 0:
                    Require(window.IsVisible&&window.TryGetPlatformHandle() is not null,"Acceptance must use a real native window.");
                    var restored=model.HotkeyProfile.Overrides.Any(item=>item.Context==HotkeyContext.Default&&item.CommandId==CommandIds.SubtitleNew);
                    Require(model.Actions[CommandIds.SubtitleNew].Shortcut==(restored?"Ctrl-Alt-K":"Ctrl-N"),"Profile must restore the effective shortcut on a subsequent launch.");
                    Invoke(CommandIds.GridInsertAfter);Invoke(CommandIds.GridInsertAfter);
                    model.SelectedEvent=model.Events[0];model.Draft!.Text=Source;Invoke(CommandIds.EditCommit);
                    _document=model.ActiveDocumentId;_line=model.SelectedEvent;_editor=model.ActiveEditor;
                    _savedPath=Path.Combine(options.ProfileDirectory!,"keyboard-fixture.ass");Require(model.SaveActiveSubtitle(_savedPath),"Prepare a saved Unicode fixture.");
                    Invoke(CommandIds.ViewAudioVideo);return;
                case 1:
                    Press(Grid,Key.O,KeyModifiers.Alt);return;
                case 2:
                    _options=window.OwnedWindows.OfType<OptionsWindow>().Single();
                    Require(_options.IsVisible&&_options.ClientSize.Width>=650&&_options.ClientSize.Height>=460,"Keyboard settings must have usable real-window geometry.");
                    Require(InOptions<ComboBox>("HotkeyContext").Items.Count==Enum.GetValues<HotkeyContext>().Length,"All reference and Yoake contexts must be selectable.");
                    Choose(CommandIds.SubtitleNew);Capture(Key.K,KeyModifiers.Control|KeyModifiers.Alt);Click("ChangeHotkey");return;
                case 3:
                    Require(model.Actions[CommandIds.SubtitleNew].Shortcut=="Ctrl-Alt-K","Change must replace the command binding immediately.");
                    Capture(Key.F10);Click("AddHotkey");return;
                case 4:
                    Require(model.HotkeyProfile.EffectiveGestures(CommandIds.SubtitleNew,HotkeyContext.Default).SequenceEqual(new[]{"Ctrl-Alt-K","F10"}),"Settings must retain multiple bindings.");
                    Choose(CommandIds.SubtitleOpen);Capture(Key.K,KeyModifiers.Control|KeyModifiers.Alt);Click("ChangeHotkey");return;
                case 5:
                    Require(InOptions<TextBlock>("HotkeyStatus").Text!.Contains("already bound",StringComparison.OrdinalIgnoreCase),"A conflicting binding must identify its owner.");
                    Require(model.Actions[CommandIds.SubtitleOpen].Shortcut=="Ctrl-O"&&model.Actions[CommandIds.SubtitleNew].Shortcut=="Ctrl-Alt-K","Conflict must leave both commands unchanged.");
                    Choose(CommandIds.SubtitleNew);InOptions<ListBox>("EffectiveHotkeys").SelectedItem="F10";Click("RemoveHotkey");return;
                case 6:
                    Require(model.HotkeyProfile.EffectiveGestures(CommandIds.SubtitleNew,HotkeyContext.Default).SequenceEqual(new[]{"Ctrl-Alt-K"}),"Remove must affect only the selected binding.");
                    Click("ClearHotkeys");Require(model.Actions[CommandIds.SubtitleNew].InputGesture is null,"An explicitly cleared command must have no displayed shortcut.");
                    Click("ResetCommandHotkeys");Require(model.Actions[CommandIds.SubtitleNew].Shortcut=="Ctrl-N","Reset command must restore defaults.");
                    Capture(Key.K,KeyModifiers.Control|KeyModifiers.Alt);Click("ChangeHotkey");Click("ResetContextHotkeys");
                    Require(model.Actions[CommandIds.SubtitleNew].Shortcut=="Ctrl-N","Reset context must restore its defaults.");
                    Capture(Key.K,KeyModifiers.Control|KeyModifiers.Alt);Click("ChangeHotkey");Click("ResetAllHotkeys");
                    Require(model.HotkeyProfile.Overrides.Length==0,"Reset all must remove all overrides.");
                    Capture(Key.K,KeyModifiers.Control|KeyModifiers.Alt);Click("ChangeHotkey");
                    // Route the same physical Right key to a real different action
                    // in Audio, using the shipping customization workflow.
                    InOptions<ComboBox>("HotkeyContext").SelectedItem=HotkeyContext.Audio;
                    Choose("time/next");Click("ClearHotkeys");Choose(CommandIds.GridLineNext);Capture(Key.Right);Click("AddHotkey");return;
                case 7:
                    Require(model.Hotkeys.Resolve(new("Right"),[HotkeyContext.Audio])?.CommandId==CommandIds.GridLineNext,"Audio customization must remain context-local.");
                    Require(ReferenceEquals(_editor,model.ActiveEditor)&&ReferenceEquals(_line,model.SelectedEvent)&&model.SelectedEvent!.Text==Source,"Settings must preserve the active editor and source.");
                    var menu=window.FindControl<MenuItem>("NewMenu")!;
                    Require(menu.InputGesture?.Key==Key.K&&menu.InputGesture.KeyModifiers==(KeyModifiers.Control|KeyModifiers.Alt),"Shipping menu must display the currently effective shortcut.");
                    var settings=new SettingsStore(Path.Combine(options.ProfileDirectory!,"settings.json")).Load();
                    Require(new HotkeyProfile(AegisubHotkeyDefaults.ForYoake(),settings.HotkeyOverrides).EffectiveGestures(CommandIds.SubtitleNew,HotkeyContext.Default).SequenceEqual(new[]{"Ctrl-Alt-K"}),"Actual settings file must contain the customization.");
                    _options!.Close();return;
                case 8:
                    var tabs=model.Tabs.Count;Press(Grid,Key.N,KeyModifiers.Control);Require(model.Tabs.Count==tabs,"Removed Ctrl-N must not run its old action.");
                    Press(Grid,Key.K,KeyModifiers.Control|KeyModifiers.Alt);Require(model.Tabs.Count==tabs+1,"The remapped key must create a document through the shipping router.");
                    Invoke(CommandIds.WorkspaceActivateTab,_document);return;
                case 9:
                    Text.Focus();Text.CaretIndex=Text.Text!.Length;var count=model.Events.Count;
                    var selected=model.SelectedEvent;var before=model.SelectedEvent!.Text;
                    Press(Text,Key.A);Require(model.ActiveVisualTool=="Position"&&model.Events.Count==count,"Typing must not select a video tool or mutate lines.");
                    Press(Text,Key.Delete,KeyModifiers.Control);Require(model.Events.Count==count&&ReferenceEquals(selected,model.SelectedEvent),"Native word deletion must not delete subtitle rows.");
                    var presenter=Text.GetVisualDescendants().OfType<TextPresenter>().Single();presenter.PreeditText="仮";
                    var draft=Text.Text;Press(Text,Key.Enter);Require(ReferenceEquals(selected,model.SelectedEvent)&&selected!.Text==before&&Text.Text==draft&&presenter.PreeditText=="仮","IME Enter must not commit or navigate.");
                    presenter.PreeditText=null;Text.SetCurrentValue(TextBox.TextProperty,Source+" edited");
                    Press(Text,Key.Enter,KeyModifiers.Control);Require(ReferenceEquals(_line,model.SelectedEvent)&&_line!.Text==Source+" edited","Edit-box Ctrl-Enter must commit and stay.");
                    Press(Text,Key.Enter);Require(ReferenceEquals(model.Events[1],model.SelectedEvent),"Edit-box Enter must commit and advance.");
                    Press(Grid,Key.F2);Require(File.ReadAllText(_savedPath).Contains(Source+" edited",StringComparison.Ordinal),"F2 must use the real Save action and preserve exact unknown/Unicode source.");
                    _media=model.OpenMediaAsync(Path.Combine(options.VerificationMedia!,"av.avi"));return;
                case 10:
                    if(!_media!.IsCompleted){_stage--;return;}
                    Require(_media.Result&&model.FrameTimes.Count>2&&model.VideoFrame is not null&&model.WaveformSamples is not null,"Keyboard media acceptance needs loaded native providers.");
                    _frame=model.CurrentFrame;Press(Grid,Key.Right);return;
                case 11:
                    Require(model.CurrentFrame==_frame+1,"Grid Right must advance exactly one media frame.");
                    _frame=model.CurrentFrame;Press(window.FindControl<VisualOverlayControl>("VisualOverlay")!,Key.Right);return;
                case 12:
                    Require(model.CurrentFrame==_frame+1,"Video Right must use the same central frame timeline.");
                    Press(window.FindControl<VisualOverlayControl>("VisualOverlay")!,Key.D);Require(model.ActiveVisualTool=="RotateZ","Video D must select Rotate Z.");
                    var original=model.ActiveEditor!.Document.Serialize();_frame=model.CurrentFrame;model.SelectedEvent=model.Events[0];
                    Press(window.FindControl<AudioWaveformControl>("AudioDisplay")!,Key.Right);
                    Require(ReferenceEquals(model.SelectedEvent,model.Events[1])&&model.CurrentFrame==_frame&&model.ActiveEditor.Document.Serialize()==original,"Audio Right must obey its remapped local action without leaking Video Right or mutating the document.");
                    Press(window.FindControl<VisualOverlayControl>("VisualOverlay")!,Key.S);Require(model.ActiveVisualTool=="Position","Video S must select the drag tool without starting Audio playback.");
                    var waveform=model.WaveformSamples;var times=model.FrameTimes;
                    Invoke(CommandIds.ViewDefault);Invoke(CommandIds.ViewAudioVideo);
                    Require(ReferenceEquals(waveform,model.WaveformSamples)&&ReferenceEquals(times,model.FrameTimes)&&model.CurrentFrame==_frame,"View changes must retain the providers and keyboard playback position.");
                    Press(Grid,Key.O,KeyModifiers.Alt);return;
                case 13:
                    _options=window.OwnedWindows.OfType<OptionsWindow>().Single();InOptions<ComboBox>("HotkeyContext").SelectedItem=HotkeyContext.Audio;
                    Click("ResetContextHotkeys");InOptions<ComboBox>("HotkeyContext").SelectedItem=HotkeyContext.Default;
                    Choose(CommandIds.AppToggleToolbar);Capture(Key.F12);Click("AddHotkey");_options.Close();return;
                case 14:
                    Require(model.Hotkeys.Resolve(new("Right"),[HotkeyContext.Audio])?.CommandId=="time/next","Audio reset must restore its exact default action.");
                    Press(Grid,Key.F12);return;
                case 15:
                    Require(!model.ToolbarVisible&&!window.FindControl<Grid>("MainToolbar")!.IsVisible&&!window.FindControl<MenuItem>("ToolbarMenu")!.IsChecked,"Toolbar hotkey must hide the actual region and clear its checked menu state.");
                    Require(window.FindControl<ListBox>("SubtitleRows")!.Bounds.Height>=140&&ReferenceEquals(_editor,model.ActiveEditor),"Hiding the toolbar must retain editor identity and useful grid geometry.");
                    Press(Grid,Key.F12);return;
                case 16:
                    Require(model.ToolbarVisible&&window.FindControl<Grid>("MainToolbar")!.Bounds.Height>=24&&window.FindControl<MenuItem>("ToolbarMenu")!.IsChecked,"Toolbar hotkey must restore usable geometry and checked state.");
                    _=model.Registry.InvokeAsync(CommandIds.AppLog,new(model.ActiveDocumentId));return;
                case 17:
                    _log=window.OwnedWindows.OfType<LogWindow>().Single();
                    if(_log.FindControl<TextBox>("ApplicationLog")?.Text is not {Length:>0}){_stage--;return;}
                    Require(_log.IsVisible&&_log.FindControl<TextBox>("ApplicationLog")!.Text!.Contains("[INFO]",StringComparison.Ordinal),"Log action must show actual application records in the shipping window.");
                    _log.Close();return;
                case 18:
                    Require(StartupDiagnostics.BindingWarningCount==0&&StartupDiagnostics.FrameworkErrorCount==0,"Hotkey/settings workflow must produce no binding warnings or Avalonia errors.");
                    _timer.Stop();options.FinishVerification("PASS: separate real-window keyboard acceptance; exact contexts; live capture/change/add/remove/clear/reset; conflict ownership and atomic rejection; effective menu shortcuts; persisted profile restored across launches; removed/new gestures through shipping router; F2 saves unknown tagged Unicode; native word deletion/typing and IME Enter isolation; edit-box commit/stay/next; Grid/Video/Audio focus precedence and real native media frame navigation; provider retention through view changes; real toolbar visibility/geometry/checked state and actual log viewer.\n");
                    StartupDiagnostics.Complete();desktop.Shutdown(0);return;
            }
        }
        catch(Exception exception){_timer.Stop();Program.Fatal("Packaged contextual keyboard acceptance",exception);desktop.Shutdown(1);}
    }
    private void Choose(string command)
    {
        InOptions<TextBox>("HotkeyFilter").Text=command;
        var list=InOptions<ListBox>("HotkeyCommands");list.SelectedItem=list.Items.Cast<object>().Single(item=>item.ToString()!.EndsWith(" · "+command,StringComparison.Ordinal)||item.ToString()!.EndsWith(" · "+command+"  (backend not available)",StringComparison.Ordinal));
    }
    private void Capture(Key key,KeyModifiers modifiers=KeyModifiers.None)
    {
        Press(InOptions<TextBox>("HotkeyCapture"),key,modifiers);
        Require(!string.IsNullOrEmpty(InOptions<TextBox>("HotkeyGesture").Text),"The focused capture field must receive a real key gesture.");
    }
    private void Click(string name)=>InOptions<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Press(Control control,Key key,KeyModifiers modifiers=KeyModifiers.None)
    {
        if(control is ListBox list)
        {
            var row=list.ContainerFromIndex(Math.Max(0,list.SelectedIndex)) as Control;
            Require(row is {Bounds.Height:>0},"A realized grid row must exist before keyboard input.");control=row!;
        }
        (TopLevel.GetTopLevel(control) as Window)?.Activate();
        Require(control.Focus()&&control.IsKeyboardFocusWithin,"Shipping control must own focus before keyboard input: "+control.Name);
        control.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Source=control,Key=key,KeyModifiers=modifiers});
    }
    private void Invoke(string command,object? parameter=null)
    {
        var result=model.Registry.InvokeAsync(command,new(model.ActiveDocumentId),parameter);
        Require(result.IsCompletedSuccessfully&&result.Result,"Acceptance preparation command failed: "+command);
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
