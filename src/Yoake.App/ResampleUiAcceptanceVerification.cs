using Avalonia;
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
using Yoake.UI.ViewModels;

namespace Yoake.App;

internal sealed class ResampleUiAcceptanceVerification(IClassicDesktopStyleApplicationLifetime desktop,MainWindow window,MainWindowViewModel model,StartupOptions options)
{
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMilliseconds(100)};
    private bool _ready,_ticking;private int _stage;private Task<bool>? _opening;private ResampleWindow? _dialog;
    private SubtitleEditor? _editor;private AssEvent? _line;private Guid? _document;private string _source="",_path="",_audio="";
    private object? _waveform,_times,_keys;private VideoFrameCapture? _before;private int _frame;
    private Yoake.Core.Audio.AudioClipInterval? _pending;
    private T In<T>(string name)where T:Control=>_dialog!.FindControl<T>(name)??throw new InvalidOperationException("Missing resampling control: "+name);
    public void Start(){window.LayoutUpdated+=(_,_)=>_ready=window.IsVisible&&window.Bounds.Width>0;_timer.Tick+=Tick;_timer.Start();}
    private async void Tick(object? sender,EventArgs args)
    {
        if(!_ready||_ticking)return;_ticking=true;options.MarkStartupVerified();
        try
        {
            switch(_stage++)
            {
                case 0:
                    Require(window.TryGetPlatformHandle() is not null,"Resampling requires the real native application.");
                    _path=Path.Combine(options.ProfileDirectory!,"resample.ass");
                    var doc=AssDocument.CreateEmpty();var edit=new SubtitleEditor(doc);edit.SetScriptInfo(new Dictionary<string,string>{{"PlayResX","640"},{"PlayResY","480"},{"LayoutResX","640"},{"LayoutResY","480"},{"YCbCr Matrix","TV.709"}});
                    var line=edit.Insert(null,false,200);edit.SetTiming(line,200,900);edit.SetField(line,"Text",@"{\an7\pos(120,120)\distort(0,0,1,0,1,1,0,1)\future(2,3)}Resample 日本語","Fixture");
                    File.WriteAllText(_path,doc.Serialize()+"[Fonts]\nfontname: fixture.ttf\n!!!!\n[X-Opaque]\nkeep: exactly\n");Require(model.OpenSubtitle(_path),"Open lossless resample fixture.");
                    _editor=model.ActiveEditor;_line=model.SelectedEvent;_document=model.ActiveDocumentId;_source=_editor!.Document.Serialize();_opening=model.OpenMediaAsync(Path.Combine(options.VerificationMedia!,"av.avi"));return;
                case 1:
                    if(!_opening!.IsCompleted){_stage--;return;}Require(_opening.Result,"Native AV source is available.");
                    await Invoke("video/open/dummy",new DummyVideoOptions{Width=320,Height=240,FrameCount=100,FramesPerSecond=24});await Invoke("video/jump",new VideoJumpRequest(Frame:8));
                    _audio=model.AudioSourcePath!;_waveform=model.WaveformSamples;_times=model.FrameTimes;_keys=model.Keyframes;_frame=model.CurrentFrame;
                    model.Draft!.Text+="\\Nvalid uncommitted draft";_before=await model.ExtractCurrentVideoFrameAsync(FrameImageKind.Subtitles);
                    Require(Coverage(_before.Image.Pixels)>0,"Real Mangetsu preview has subtitle coverage before resampling.");
                    window.FindControl<MenuItem>("ToolsMenu")!.IsSubMenuOpen=true;return;
                case 2:
                    var menu=window.FindControl<MenuItem>("ResampleMenu")!;Require(menu.IsEnabled,"Resample is user-accessible through Tools.");Press(menu,Key.Enter);return;
                case 3:
                    _dialog=window.OwnedWindows.OfType<ResampleWindow>().Single();Require(_dialog.TryGetPlatformHandle() is not null,"Real resampling dialog exists.");
                    Require(In<TextBox>("ResampleSourceX").Text=="640"&&In<TextBox>("ResampleTargetX").Text=="320","Source defaults to script and target defaults to actual video.");
                    foreach(var name in new[]{"ResampleTargetX","ResampleAspect","ResamplePreview","ResampleApply","ResampleCancel"})Within(In<Control>(name),_dialog);
                    var input=In<TextBox>("ResampleTargetX");input.Focus();var presenter=input.GetVisualDescendants().OfType<TextPresenter>().Single();presenter.PreeditText="にほん";Press(input,Key.Enter);Press(input,Key.Escape);presenter.PreeditText="";return;
                case 4:
                    Require(_dialog!.IsVisible&&model.ResamplePreview is null,"IME Enter/Escape do not preview, apply or close.");
                    In<TextBox>("ResampleTargetX").Text="0";Press(In<Button>("ResamplePreview"),Key.Enter);return;
                case 5:
                    Require(model.ResamplePreview is null&&In<TextBlock>("ResampleSummary").Text!.Contains("Dimensions"),"Invalid dimensions produce actionable feedback; displayed: "+In<TextBlock>("ResampleSummary").Text);Unchanged();
                    In<TextBox>("ResampleTargetX").Text="1280";In<TextBox>("ResampleTargetY").Text="960";model.Draft!.Start="unfinished";Press(In<Button>("ResamplePreview"),Key.Enter);return;
                case 6:
                    Require(model.ResamplePreview is null&&model.Draft!.Start=="unfinished","Invalid raw timing is retained and cannot enter ASS serialization.");Unchanged();
                    model.Draft!.Start=_line!.Start;await Invoke("time/lead/out");_pending=model.PrimaryAudioRange;Require(model.HasPendingAudioTiming,"Native audio timing remains a pending transaction before resampling.");
                    _before=await model.ExtractCurrentVideoFrameAsync(FrameImageKind.Subtitles);Press(In<Button>("ResamplePreview"),Key.Enter);return;
                case 7:
                    if(model.HasEditOperation){_stage--;return;}
                    Require(model.ResamplePreview is {ChangedEvents:1,ChangedStyles:1}&&In<ListBox>("ResampleWarnings").ItemCount>=1,"Real preview lists edits and preserved extension warnings.");Unchanged();
                    Press(In<Button>("ResampleApply"),Key.Enter);return;
                case 8:
                    Require(!window.OwnedWindows.OfType<ResampleWindow>().Any(),"Reviewed Apply closes the actual dialog.");
                    Require(_editor!.Document.GetScriptInfo("PlayResX")=="1280"&&_line!.Text.Contains("\\pos(240,240)")&&_line.Text.Contains("\\distort(0,0,1,0,1,1,0,1)")&&_line.Text.Contains("valid uncommitted draft"),"Real resampling scales script geometry, retains normalized pins and commits valid draft.");
                    Require(_editor.Document.Serialize().EndsWith("[Fonts]\nfontname: fixture.ttf\n!!!!\n[X-Opaque]\nkeep: exactly\n")&&_editor.Document.GetScriptInfo("YCbCr Matrix")=="TV.709","Opaque metadata, encoded attachment and matrix remain exact.");
                    var after=await model.ExtractCurrentVideoFrameAsync(FrameImageKind.Subtitles);var prior=Bounds(_before!.Image.Pixels,_before.Image.Width);var current=Bounds(after.Image.Pixels,after.Image.Width);
                    Require(after.Image.Width==320&&after.Image.Height==240&&Coverage(after.Image.Pixels)>0&&Math.Abs(prior.Width-current.Width)<=3&&Math.Abs(prior.Height-current.Height)<=3&&Math.Abs(prior.X-current.X)<=3&&Math.Abs(prior.Y-current.Y)<=3,"Actual Mangetsu rendering retains position and size at source video resolution after proportional resampling.");
                    MediaUnchanged();window.FindControl<MenuItem>("EditMenu")!.IsSubMenuOpen=true;return;
                case 9:
                    Press(window.FindControl<MenuItem>("UndoMenu")!,Key.Enter);return;
                case 10:
                    Unchanged();Require(model.Draft!.Text==_line!.Text,"Single actual Undo restores the committed original and editor draft.");window.FindControl<MenuItem>("EditMenu")!.IsSubMenuOpen=true;return;
                case 11:
                    Press(window.FindControl<MenuItem>("RedoMenu")!,Key.Enter);return;
                case 12:
                    Require(_editor!.Document.GetScriptInfo("PlayResX")=="1280","Actual Redo restores the whole resample transaction.");MediaUnchanged();
                    await Invoke("settings/hotkeys/set",new HotkeyChange(HotkeyContext.Default,"tool/resampleres",["F9"]));Require(window.FindControl<MenuItem>("ResampleMenu")!.InputGesture?.Key==Key.F9,"Effective menu shortcut reflects user override.");
                    Press(window.FindControl<ListBox>("SubtitleRows")!,Key.F9);return;
                case 13:
                    _dialog=window.OwnedWindows.OfType<ResampleWindow>().Single();Require(!In<ComboBox>("ResampleAspect").IsEnabled,"Matching aspect ratios disable the source's irrelevant aspect controls.");
                    In<TextBox>("ResampleTargetY").Text="720";In<ComboBox>("ResampleAspect").SelectedItem=ResampleAspect.Manual;
                    Require(In<TextBox>("ResampleLeft").IsEnabled&&!In<TextBox>("ResampleRight").IsEnabled,"Manual symmetric margins provide meaningful input state.");In<TextBox>("ResampleLeft").Text="20";Require(In<TextBox>("ResampleRight").Text=="20","Symmetric input updates its paired margin.");
                    Press(In<Button>("ResampleCancel"),Key.Enter);return;
                case 14:
                    Require(!window.OwnedWindows.OfType<ResampleWindow>().Any()&&_editor!.Document.GetScriptInfo("PlayResX")=="1280","Real Cancel does not apply pending dimensions.");
                    Press(window.FindControl<ListBox>("SubtitleRows")!,Key.F9);return;
                case 15:
                    _dialog=window.OwnedWindows.OfType<ResampleWindow>().Single();await Invoke(CommandIds.SubtitleNew);return;
                case 16:
                    Require(!window.OwnedWindows.OfType<ResampleWindow>().Any()&&model.ActiveDocumentId!=_document,"Switching documents closes and releases the owning dialog.");
                    Require(!await model.Registry.InvokeAsync("resample/apply",new(_document)),"Foreign document cannot apply an old plan.");await Invoke(CommandIds.WorkspaceActivateTab,_document);MediaUnchanged();
                    Require(StartupDiagnostics.BindingWarningCount==0&&StartupDiagnostics.FrameworkErrorCount==0,"Resampling must not cause binding or framework errors.");
                    _timer.Stop();options.FinishVerification("PASS: real native Tools menu and remapped F9, compact resampling dialog, script/video defaults, IME Enter/Escape isolation, validation without draft loss, immutable background preview and warning list, actual reviewed Apply/Cancel, symmetric margins, one real menu Undo/Redo, normalized Mangetsu distortion and rendered glyph geometry, native audio and dummy video provider/frame/timeline retention, attachment/opaque metadata/matrix preservation, document-tab isolation.\n");StartupDiagnostics.Complete();desktop.Shutdown(0);return;
            }
        }
        catch(Exception exception){_timer.Stop();Program.Fatal("Packaged resample acceptance at stage "+(_stage-1),exception);desktop.Shutdown(1);}
        finally{_ticking=false;}
    }
    private void Unchanged()=>Require(_editor!.Document.Serialize()==_source&&ReferenceEquals(_line,model.SelectedEvent),"Preview/cancel/Undo retains original source and active row.");
    private void MediaUnchanged()=>Require(ReferenceEquals(_editor,model.ActiveEditor)&&model.AudioSourcePath==_audio&&ReferenceEquals(_waveform,model.WaveformSamples)&&ReferenceEquals(_times,model.FrameTimes)&&ReferenceEquals(_keys,model.Keyframes)&&model.CurrentFrame==_frame&&model.HasPendingAudioTiming&&model.PrimaryAudioRange==_pending,"Resampling must preserve document and independent media/timeline and pending timing state.");
    private static int Coverage(byte[] pixels)=>pixels.Where((_,i)=>i%4==3).Count(a=>a>0);
    private static Rect Bounds(byte[] pixels,int width)
    {var points=Enumerable.Range(0,pixels.Length/4).Where(i=>pixels[i*4+3]>0).ToArray();return points.Length==0?default:new(points.Min(i=>i%width),points.Min(i=>i/width),points.Max(i=>i%width)-points.Min(i=>i%width)+1,points.Max(i=>i/width)-points.Min(i=>i/width)+1);}
    private async Task Invoke(string id,object? parameter=null)=>Require(await model.Registry.InvokeAsync(id,new(model.ActiveDocumentId),parameter),"Resampling preparation failed: "+id);
    private static void Press(Control control,Key key)
    {
        if(control is ListBox list)control=list.ContainerFromIndex(Math.Max(0,list.SelectedIndex)) as Control??throw new InvalidOperationException("A realized subtitle row receives the hotkey.");
        (TopLevel.GetTopLevel(control) as Window)?.Activate();Require(control.Focus()&&control.IsKeyboardFocusWithin,"Real controls must own keyboard focus: "+control.Name);control.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Source=control,Key=key});
    }
    private static void Within(Control control,Window owner)
    {var p=control.TranslatePoint(default,owner);Require(p is {} point&&control.Bounds.Width>0&&control.Bounds.Height>0&&point.X>=-1&&point.Y>=-1&&point.X+control.Bounds.Width<=owner.ClientSize.Width+1&&point.Y+control.Bounds.Height<=owner.ClientSize.Height+1,"Resampling control must fit its native dialog: "+control.Name);}
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
