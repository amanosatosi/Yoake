using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Controls.Presenters;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yoake.Core.Commands;
using Yoake.Core.Hotkeys;
using Yoake.Core.Logging;
using Yoake.Core.Subtitles;
using Yoake.UI;
using Yoake.UI.Controls;
using Yoake.UI.ViewModels;
using KeyModifiers = Avalonia.Input.KeyModifiers;

namespace Yoake.App;

// Real input over the shipping overlay and native FFMS2/Mangetsu providers.
// Kept separate from startup and the detailed visual pixel/geometry verifier.
internal sealed class VideoUiAcceptanceVerification(IClassicDesktopStyleApplicationLifetime desktop,
    MainWindow window,MainWindowViewModel model,StartupOptions options)
{
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMilliseconds(100)};
    private readonly Pointer _pointer=new(927,PointerType.Mouse,true);
    private bool _ready,_ticking;private int _stage;private Task<bool>? _media;
    private AssEvent? _line;private string _source="",_committed="";private long _revision;
    private object? _samples,_frames,_keys;private Guid? _document;
    private string? _prospective;private AssPoint _a,_b;
    private VisualOverlayControl Overlay=>window.FindControl<VisualOverlayControl>("VisualOverlay")!;
    private Rect Video
    {
        get{var frame=model.VideoFrame!;var scale=Math.Min(Overlay.Bounds.Width/frame.PixelSize.Width,Overlay.Bounds.Height/frame.PixelSize.Height);
            var w=frame.PixelSize.Width*scale;var h=frame.PixelSize.Height*scale;return new((Overlay.Bounds.Width-w)/2,(Overlay.Bounds.Height-h)/2,w,h);}
    }
    public void Start(){window.LayoutUpdated+=(_,_)=>_ready=window.IsVisible&&window.Bounds.Width>0;_timer.Tick+=Tick;_timer.Start();}
    private async void Tick(object? sender,EventArgs args)
    {
        if(!_ready||_ticking)return;_ticking=true;
        try
        {
            switch(_stage++)
            {
                case 0:
                    Require(!model.Actions["video/tool/clip"].CanExecute(null),"Visual tool command must require loaded video.");
                    var path=Path.Combine(options.ProfileDirectory!,"video-tools.ass");
                    File.WriteAllText(path,"[Script Info]\nPlayResX: 384\nPlayResY: 288\n[Events]\nFormat: "+string.Join(',',AssDocument.EventFormat)+"\nDialogue: 0,0:00:00.30,0:00:01.40,Default,A,0001,0002,0003,fx,{\\future(x)\\distort(1,2,3,4,5,6,7,8)}日本語 é\n");
                    Require(model.OpenSubtitle(path),"Open lossless visual fixture.");
                    _media=model.OpenMediaAsync(Path.Combine(options.VerificationMedia!,"av.avi"));return;
                case 1:
                    if(_media?.IsCompleted!=true){_stage--;return;}Require(await _media,"Load native video provider.");await Invoke(CommandIds.ViewAudioVideo);
                    _line=model.SelectedEvent!;_document=model.ActiveDocumentId;_samples=model.WaveformSamples;_frames=model.FrameTimes;_keys=model.Keyframes;
                    _source=model.ActiveEditor!.Document.Serialize();
                    await Invoke("time/lead/in");model.CurrentTimeSeconds=.2;
                    Require(model.VisibleVisualLines() is [var pending]&&pending.RelativeTime==0&&pending.Duration==1200,
                        "Visual tools use the pending compositor interval at a frame outside stored timing.");
                    await Invoke("audio/timing/revert");model.CurrentTimeSeconds=.4;
                    model.Draft!.Text+=" retained draft";var draft=model.Draft;
                    foreach(var (key,tool) in new[]{(Key.A,"Crosshair"),(Key.S,"Position"),(Key.D,"RotateZ"),(Key.F,"RotateXY"),(Key.G,"Scale"),(Key.H,"Clip"),(Key.J,"VectorClip")})
                    {
                        Press(Overlay,key);Radio(tool);Require(ReferenceEquals(model.Draft,draft)&&model.ActiveEditor.Document.Serialize()==_source,"Switching by actual Video key must preserve draft/source.");
                    }
                    foreach(var mode in MainWindowViewModel.VectorModes)
                    {
                        Press(window.FindControl<ToggleButton>("Vector"+mode)!,Key.Enter);VectorRadio(mode);
                        Require(model.ActiveEditor.Document.Serialize()==_source,"Vector mode switching must not write subtitle source.");
                    }
                    await Invoke("settings/hotkeys/set",new HotkeyChange(HotkeyContext.Video,"video/vector/line",["F9"]));
                    Require(window.FindControl<MenuItem>("VideoVectorLineMenu")!.InputGesture?.Key==Key.F9,"Vector menu shows effective user shortcut.");
                    Press(Overlay,Key.F9);VectorRadio("Line");Press(Overlay,Key.H);Radio("Clip");
                    Require(!await model.Registry.InvokeAsync("video/tool/scale",new(Guid.NewGuid())),"Foreign document tool invocation must be rejected.");
                    using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();Require(!await model.Registry.InvokeAsync("video/tool/scale",new(_document),cancellationToken:cancelled.Token),"Canceled tool invocation must leave active mode intact.");}
                    Radio("Clip");await Invoke(CommandIds.EditCancel);_a=new(30,30);_b=new(160,120);return;
                case 2:
                    Require(Video.Width>100&&Video.Height>80,"Real clip input surface has usable geometry.");
                    Down(_a);Require(model.HasGesture&&AssVisualTags.Clip(_line!.Text) is {Rectangular:true},"Pointer down without a clip starts a prospective structured rectangle.");
                    Move(_b,true);Require(AssVisualTags.Clip(_line.Text)!.Points.SequenceEqual(new[]{_a,_b}),"Real pointer movement updates prospective rectangle.");
                    _revision=model.PreviewRevision;return;
                case 3:
                    if(model.DisplayedPreviewRevision<_revision){_stage--;return;}
                    Require(Overlay.RenderedTool=="Clip"&&Overlay.RenderedHandles==4,"Prospective rectangle and its corners must render before release.");
                    Up(_b);Require(!model.HasGesture,"Pointer release commits the clip transaction.");_committed=model.ActiveEditor!.Document.Serialize();
                    Press(Overlay,Key.Z,KeyModifiers.Control);Require(model.ActiveEditor.Document.Serialize()==_source&&!model.ActiveEditor.Undo.CanUndo,"One focused Ctrl-Z restores exact pre-clip source.");
                    Press(Overlay,Key.Z,KeyModifiers.Control|KeyModifiers.Shift);Require(model.ActiveEditor.Document.Serialize()==_committed,"Real redo restores the committed structured clip.");
                    Press(Overlay,Key.Z,KeyModifiers.Control);model.InverseClip=true;Down(_a);Move(_b,true);Up(_b);
                    Require(AssVisualTags.Clip(_line!.Text) is {Rectangular:true,Inverse:true},"New inverse clip creation works without an existing clip.");
                    Press(Overlay,Key.Z,KeyModifiers.Control);Down(_a);Move(_b,true);_pointer.Capture(null);
                    Require(!model.HasGesture&&model.ActiveEditor.Document.Serialize()==_source,"Capture loss restores absent clip and exact source.");
                    foreach(var (key,tool) in new[]{(Key.D,"RotateZ"),(Key.J,"VectorClip")})
                    {
                        Press(Overlay,Key.H);Down(_a);Move(_b,true);Press(Overlay,key);Radio(tool);
                        Require(!model.HasGesture&&model.ActiveEditor.Document.Serialize()==_source,"Actual Video tool key cancels the active pointer gesture.");
                    }
                    await Invoke("video/vector/line");VectorRadio("Line");Down(_a);Up(_a);_revision=model.PreviewRevision;return;
                case 4:
                    if(model.DisplayedPreviewRevision<_revision){_stage--;return;}
                    Move(_b,false);_revision=model.PreviewRevision;return;
                case 5:
                    // Rendering does not change source/revision: observe a UI turn after hover.
                    _prospective=Overlay.VectorPreviewDrawing;Require(_prospective is not null,"Line hover must show prospective geometry.");
                    var firstPath=AssVectorPath.Parse(AssVisualTags.Clip(_line!.Text)!.Drawing)!;firstPath.Append(_b,false);
                    Require(_prospective==firstPath.Serialize(1),"Line prospective contour must include the next point and closing topology.");
                    Down(_b);Up(_b);Require(AssVisualTags.Clip(_line.Text)!.Drawing==_prospective,"Line release matches prospective path.");
                    await Invoke("video/vector/bicubic");VectorRadio("Bicubic");Move(new(240,180),false);return;
                case 6:
                    _prospective=Overlay.VectorPreviewDrawing;Require(_prospective is not null&&Overlay.RenderedHandles>=5,"Bicubic hover shows curve endpoints and control geometry.");
                    var curvePath=AssVectorPath.Parse(AssVisualTags.Clip(_line!.Text)!.Drawing)!;curvePath.Append(new(240,180),true);
                    Require(_prospective==curvePath.Serialize(1),"Bicubic prospective curve uses exact structured control points.");
                    var closing=curvePath.Curves(true).Last();Require(closing.End==_a&&closing.Start==new AssPoint(240,180),"Closing segment joins prospective endpoint to contour start.");
                    Down(new(240,180));Up(new(240,180));Require(AssVisualTags.Clip(_line.Text)!.Drawing==_prospective,"Bicubic commit matches hover geometry.");
                    Press(Overlay,Key.Z,KeyModifiers.Control);Press(Overlay,Key.Z,KeyModifiers.Control);Press(Overlay,Key.Z,KeyModifiers.Control);
                    Require(model.ActiveEditor!.Document.Serialize()==_source&&!model.ActiveEditor.Undo.CanUndo,"Each vector gesture is one undo and preserves all unknown/Mangetsu/Unicode source.");
                    var text=window.FindControl<AssTextBox>("SubtitleText")!;var presenter=text.GetVisualDescendants().OfType<TextPresenter>().First();presenter.PreeditText="日本";
                    Press(text,Key.H);Press(text,Key.Enter);presenter.PreeditText=null;Radio("VectorClip");
                    Require(model.ActiveEditor.Document.Serialize()==_source,"IME/edit-box input cannot route Video tool commands or commit source.");
                    await Invoke(CommandIds.EditCancel);await Invoke("video/vector/smooth");
                    Down(_a);Move(_b,true);Require(model.HasGesture,"Document-switch cancellation starts with an actual captured vector drag.");
                    await Invoke(CommandIds.SubtitleNew);Require(!model.HasGesture&&_pointer.Captured is null,"Changing documents cancels the gesture and releases capture.");
                    Require(model.ActiveVisualTool=="Position"&&model.VectorMode=="Select"&&!model.Actions["video/tool/clip"].CanExecute(null),"A new document has independent standby tool/mode and no provider availability.");
                    await Invoke(CommandIds.WorkspaceActivateTab,_document);return;
                case 7:
                    Radio("VectorClip");VectorRadio("Smooth");Require(ReferenceEquals(_samples,model.WaveformSamples)&&ReferenceEquals(_frames,model.FrameTimes)&&ReferenceEquals(_keys,model.Keyframes),"Tool/tab operations retain document-owned providers and frame map.");
                    Require(model.ActiveEditor!.Document.Serialize()==_source&&!model.ActiveEditor.Undo.CanUndo&&StartupDiagnostics.BindingWarningCount==0&&StartupDiagnostics.FrameworkErrorCount==0,"No unintended source/undo or native binding errors.");
                    _timer.Stop();options.FinishVerification("PASS: separate real-window Video tools acceptance; actual Video A/S/D/F/G/H/J, all vector toolbar radio modes, synchronized menu/toolbars, effective remapped mode shortcut, no-video/foreign/canceled availability, retained draft/source, rectangular pointer-down/live-move/release from no clip, inverse, one-step keyboard undo/redo, capture-loss/tool-switch cancellation, line/bicubic prospective controls/curve/closing and exact committed topology, IME isolation, per-document mode retention and native provider/frame-map retention. Physical display/DPI responsiveness remains manual QA.\n");StartupDiagnostics.Complete();desktop.Shutdown(0);return;
            }
        }
        catch(Exception exception){_timer.Stop();Program.Fatal("Packaged Video tools acceptance at stage "+(_stage-1),exception);desktop.Shutdown(1);}
        finally{_ticking=false;}
    }
    private void Radio(string tool)
    {
        Require(model.ActiveVisualTool==tool,"Active semantic tool: "+tool);
        Require(MainWindowViewModel.VisualToolNames.Count(name=>model.Actions["video/tool/"+(name=="Position"?"position":name.ToLowerInvariant())].IsChecked)==1,"Exactly one semantic primary radio choice.");
        Require(window.FindControl<ToggleButton>("VisualTool"+tool)!.IsChecked==true&&window.FindControl<MenuItem>("VideoTool"+tool+"Menu")!.IsChecked,"Primary menu and toolbar reflect semantic tool.");
    }
    private void VectorRadio(string mode)
    {
        Radio("VectorClip");Require(model.VectorMode==mode&&MainWindowViewModel.VectorModes.Count(name=>model.Actions["video/vector/"+name.ToLowerInvariant()].IsChecked)==1,"Exactly one semantic vector radio choice.");
        Require(window.FindControl<ToggleButton>("Vector"+mode)!.IsChecked==true&&window.FindControl<MenuItem>("VideoVector"+mode+"Menu")!.IsChecked,"Vector menu and toolbar reflect semantic mode.");
    }
    private async Task Invoke(string id,object? parameter=null)=>Require(await model.Registry.InvokeAsync(id,new(model.ActiveDocumentId),parameter),"Video preparation: "+id);
    private static void Press(Control control,Key key,KeyModifiers modifiers=KeyModifiers.None)
    {(TopLevel.GetTopLevel(control) as Window)?.Activate();Require(control.Focus()&&control.IsKeyboardFocusWithin,"Actual Video input must own focus: "+control.Name);control.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Source=control,Key=key,KeyModifiers=modifiers});}
    private Point Root(AssPoint p)=>Overlay.TranslatePoint(new(Video.X+p.X/model.ScriptSize.Width*Video.Width,Video.Y+p.Y/model.ScriptSize.Height*Video.Height),window)!.Value;
    private void Down(AssPoint p)=>Overlay.RaiseEvent(new PointerPressedEventArgs(Overlay,_pointer,window,Root(p),0,new(RawInputModifiers.LeftMouseButton,PointerUpdateKind.LeftButtonPressed),KeyModifiers.None));
    private void Move(AssPoint p,bool down)=>Overlay.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent,Overlay,_pointer,window,Root(p),0,new(down?RawInputModifiers.LeftMouseButton:RawInputModifiers.None,PointerUpdateKind.Other),KeyModifiers.None));
    private void Up(AssPoint p)=>Overlay.RaiseEvent(new PointerReleasedEventArgs(Overlay,_pointer,window,Root(p),0,new(RawInputModifiers.None,PointerUpdateKind.LeftButtonReleased),KeyModifiers.None,MouseButton.Left));
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
