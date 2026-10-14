using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yoake.Core.Commands;
using Yoake.Core.Hotkeys;
using Yoake.Core.Logging;
using Yoake.Core.Settings;
using Yoake.Core.Subtitles;
using Yoake.UI;
using Yoake.UI.ViewModels;

namespace Yoake.App;

internal sealed class AssDrawUiAcceptanceVerification(IClassicDesktopStyleApplicationLifetime desktop,MainWindow window,MainWindowViewModel model,StartupOptions options)
{
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMilliseconds(100)};
    private bool _ready,_ticking,_dataCreated;private int _stage,_pid;private Window? _missing;private OptionsWindow? _configuration;
    private string _stub="",_custom="",_rootExe="",_dataExe="",_marker="",_source="",_history="",_plan="";private object? _draft,_selected,_waveform,_times,_keys,_karaoke;
    private string? _audio,_video;private int _frame;private SubtitleEditor? _editor;
    public void Start(){window.LayoutUpdated+=(sender,args)=>_ready=window.IsVisible&&window.Bounds.Width>0;_timer.Tick+=Tick;_timer.Start();}
    private async void Tick(object? sender,EventArgs args)
    {
        if(!_ready||_ticking)return;_ticking=true;options.MarkStartupVerified();
        try
        {
            switch(_stage++)
            {
                case 0:
                    Require(window.TryGetPlatformHandle() is not null,"ASSDraw integration requires a real native editor.");
                    _stub=Path.Combine(options.VerificationMedia!,"assdraw","ASSDraw3.exe");Require(File.Exists(_stub),"CI compiled the harmless external process fixture.");
                    var document=AssDocument.CreateEmpty();var fixture=new SubtitleEditor(document);var line=fixture.Insert(null,false,0);fixture.SetField(line,"Text","あい","Fixture");var file=Path.Combine(options.ProfileDirectory!,"assdraw.ass");File.WriteAllText(file,document.Serialize());Require(model.OpenSubtitle(file),"Open ASSDraw state fixture.");
                    Require(await model.OpenMediaAsync(Path.Combine(options.VerificationMedia!,"av.avi")),"Load independent native media.");await Invoke(CommandIds.ViewAudioVideo);await Invoke("audio/karaoke");await Invoke("time/lead/out");Require(model.KaraokePlan!.Changed,"Prepare actual uncommitted Toshiki timing.");model.Draft!.MarginV="unfinished";
                    _editor=model.ActiveEditor;_source=_editor!.Document.Serialize();_history=_editor.Undo.NextUndoName??"";_draft=model.Draft;_selected=model.SelectedEvent;_waveform=model.WaveformSamples;_times=model.FrameTimes;_keys=model.Keyframes;_audio=model.AudioSourcePath;_video=model.VideoSourcePath;_frame=model.CurrentFrame;_karaoke=model.KaraokePlan;_plan=model.KaraokePlan.PreviewText;
                    _rootExe=Path.Combine(AppContext.BaseDirectory,"ASSDraw3.exe");_dataExe=Path.Combine(AppContext.BaseDirectory,"data","ASSDraw3.exe");Require(!File.Exists(_rootExe)&&!File.Exists(_dataExe),"The test must never replace an existing bundled tool.");
                    window.FindControl<MenuItem>("ToolsMenu")!.IsSubMenuOpen=true;return;
                case 1:Press(window.FindControl<MenuItem>("AssDrawMenu")!,Key.Enter);return;
                case 2:
                    _missing=window.OwnedWindows.Single(w=>w.Name=="AssDrawMissingDialog");Require(_missing.FindControl<TextBlock>("AssDrawMissingMessage")!.Text=="ASSDraw3.exe doesn't exist.","Native missing dialog shows the exact required message.");Unchanged();Press(_missing.FindControl<Button>("AssDrawConfigure")!,Key.Enter);return;
                case 3:
                    _configuration=window.OwnedWindows.OfType<OptionsWindow>().Single();Require(_configuration.FindControl<TabControl>("OptionsTabs")!.SelectedIndex==2,"Configure opens the actual External tools options tab.");
                    var invalid=_configuration.FindControl<TextBox>("AssDrawExecutablePath")!;invalid.Text=Path.Combine(options.ProfileDirectory!,"missing.exe");Press(_configuration.FindControl<Button>("AssDrawPathApply")!,Key.Enter);return;
                case 4:
                    Require(model.AssDrawExecutable is null,"Invalid configuration cannot overwrite settings.");
                    _custom=Path.Combine(options.ProfileDirectory!,"日本語 tools with spaces","ASSDraw3.exe");Directory.CreateDirectory(Path.GetDirectoryName(_custom)!);File.Copy(_stub,_custom,false);
                    var path=_configuration!.FindControl<TextBox>("AssDrawExecutablePath")!;path.Text=_custom;path.Focus();var presenter=path.GetVisualDescendants().OfType<TextPresenter>().Single();presenter.PreeditText="にほん";Press(path,Key.Enter);Press(path,Key.Escape);presenter.PreeditText="";Require(_configuration.IsVisible&&model.AssDrawExecutable is null,"IME Enter/Escape neither configures nor closes Options.");
                    Press(_configuration.FindControl<Button>("AssDrawPathApply")!,Key.Enter);return;
                case 5:
                    if(model.AssDrawExecutable!=_custom){_stage--;return;}Require(new SettingsStore(Path.Combine(options.ProfileDirectory!,"settings.json")).Load().AssDrawExecutable==_custom,"NativeAOT settings persist the configured Unicode executable.");Unchanged();Press(_configuration!.FindControl<TextBox>("AssDrawExecutablePath")!,Key.Escape);return;
                case 6:
                    Require(!window.OwnedWindows.OfType<OptionsWindow>().Any(),"Actual Escape closes configuration.");_marker=Path.Combine(Path.GetDirectoryName(_custom)!,"assdraw-started.txt");window.FindControl<MenuItem>("ToolsMenu")!.IsSubMenuOpen=true;return;
                case 7:Press(window.FindControl<MenuItem>("AssDrawMenu")!,Key.Enter);return;
                case 8:
                    if(!Started()){_stage--;return;}Require(Alive(_pid)&&model.SubtitleStatus.StartsWith("Launched ASSDraw3",StringComparison.Ordinal),"Launcher returns while a real independent native process remains alive.");Unchanged();File.Delete(_marker);
                    await Invoke("settings/hotkeys/set",new HotkeyChange(HotkeyContext.Default,"tool/assdraw",["F8"]));Require(window.FindControl<MenuItem>("AssDrawMenu")!.InputGesture?.Key==Key.F8,"ASSDraw menu reflects the effective shortcut.");Press(window.FindControl<ListBox>("SubtitleRows")!,Key.F8);return;
                case 9:
                    if(!Started()){_stage--;return;}Require(Alive(_pid),"Real remapped keyboard launches another independent process.");Unchanged();return;
                case 10:
                    if(Alive(_pid)){_stage--;return;}File.Delete(_custom);File.Copy(_stub,_rootExe,false);_marker=Path.Combine(AppContext.BaseDirectory,"assdraw-started.txt");window.FindControl<MenuItem>("ToolsMenu")!.IsSubMenuOpen=true;return;
                case 11:Press(window.FindControl<MenuItem>("AssDrawMenu")!,Key.Enter);return;
                case 12:
                    if(!Started()){_stage--;return;}Require(model.SubtitleStatus.EndsWith(_rootExe,StringComparison.OrdinalIgnoreCase),"Removed configured executable falls back to the portable/installed application root.");Unchanged();return;
                case 13:
                    if(Alive(_pid)){_stage--;return;}File.Delete(_rootExe);File.Delete(_marker);_dataCreated=!Directory.Exists(Path.GetDirectoryName(_dataExe));Directory.CreateDirectory(Path.GetDirectoryName(_dataExe)!);File.Copy(_stub,_dataExe,false);_marker=Path.Combine(Path.GetDirectoryName(_dataExe)!,"assdraw-started.txt");Press(window.FindControl<ListBox>("SubtitleRows")!,Key.F8);return;
                case 14:
                    if(!Started()){_stage--;return;}Require(model.SubtitleStatus.EndsWith(_dataExe,StringComparison.OrdinalIgnoreCase),"Supported application data directory is a real secondary discovery path.");Unchanged();return;
                case 15:
                    if(Alive(_pid)){_stage--;return;}File.Delete(_dataExe);File.Delete(_marker);if(_dataCreated)Directory.Delete(Path.GetDirectoryName(_dataExe)!);
                    var broken=Path.Combine(options.ProfileDirectory!,"not an executable.exe");File.WriteAllText(broken,"No executable code in this failure fixture.");await Invoke("settings/assdraw/path",broken);window.FindControl<MenuItem>("ToolsMenu")!.IsSubMenuOpen=true;return;
                case 16:Press(window.FindControl<MenuItem>("AssDrawMenu")!,Key.Enter);return;
                case 17:
                    Require(model.SubtitleStatus.StartsWith("Could not launch ASSDraw3",StringComparison.Ordinal),"Real Windows process-start failure is reported without escaping ICommand.");Unchanged();
                    Require(StartupDiagnostics.BindingWarningCount==0&&StartupDiagnostics.FrameworkErrorCount==0,"ASSDraw integration introduces no binding/framework errors.");_timer.Stop();options.FinishVerification("PASS: actual native Tools ASSDraw3 menu and remapped grid F8; exact missing-message dialog and accessible External tools configuration; invalid path and IME guards; NativeAOT settings persistence; explicit Unicode/spaces executable launch returning while an independent native stub lives; repeat launch, process exit independent of editor, removed configured path, installed/portable root and data discovery, real Windows invalid-executable failure; exact source/invalid draft/selection/undo/native media/frame/timecodes/keyframes and uncommitted Toshiki plan retained. No ASSDraw source was downloaded or built.\n");StartupDiagnostics.Complete();desktop.Shutdown(0);return;
            }
        }
        catch(Exception error){_timer.Stop();Program.Fatal("Packaged ASSDraw integration at stage "+(_stage-1),error);desktop.Shutdown(1);}
        finally{_ticking=false;}
    }
    private bool Started()=>File.Exists(_marker)&&int.TryParse(File.ReadAllText(_marker),out _pid)&&_pid>0;
    private static bool Alive(int pid){try{using var process=Process.GetProcessById(pid);return !process.HasExited;}catch(ArgumentException){return false;}}
    private void Unchanged()=>Require(ReferenceEquals(_editor,model.ActiveEditor)&&_editor!.Document.Serialize()==_source&&(_editor.Undo.NextUndoName??"")==_history&&ReferenceEquals(_draft,model.Draft)&&model.Draft!.MarginV=="unfinished"&&ReferenceEquals(_selected,model.SelectedEvent)&&ReferenceEquals(_waveform,model.WaveformSamples)&&ReferenceEquals(_times,model.FrameTimes)&&ReferenceEquals(_keys,model.Keyframes)&&_audio==model.AudioSourcePath&&_video==model.VideoSourcePath&&_frame==model.CurrentFrame&&ReferenceEquals(_karaoke,model.KaraokePlan)&&_plan==model.KaraokePlan!.PreviewText,"External launcher preserves document, invalid draft, selection, undo, independent media/timeline and pending karaoke state.");
    private async Task Invoke(string id,object? parameter=null)=>Require(await model.Registry.InvokeAsync(id,new(model.ActiveDocumentId),parameter),"ASSDraw preparation failed: "+id);
    private static void Press(Control control,Key key)
    {if(control is ListBox list)control=list.ContainerFromIndex(Math.Max(0,list.SelectedIndex)) as Control??throw new InvalidOperationException("Realized subtitle row receives ASSDraw key.");(TopLevel.GetTopLevel(control) as Window)?.Activate();Require(control.Focus()&&control.IsKeyboardFocusWithin,"Real control owns focus: "+control.Name);control.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Source=control,Key=key});}
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
