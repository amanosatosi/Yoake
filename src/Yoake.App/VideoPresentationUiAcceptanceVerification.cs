using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Media.Imaging;
using System.Runtime.InteropServices;
using Yoake.Core.Commands;
using Yoake.Core.Hotkeys;
using Yoake.Core.Logging;
using Yoake.Core.Media;
using Yoake.Core.Subtitles;
using Yoake.UI;
using Yoake.UI.Controls;
using Yoake.UI.ViewModels;

namespace Yoake.App;

// Real native hosts and shipping input controls; separate from startup acceptance.
internal sealed class VideoPresentationUiAcceptanceVerification(IClassicDesktopStyleApplicationLifetime desktop,
    MainWindow window, MainWindowViewModel model, StartupOptions options)
{
    private readonly DispatcherTimer _timer = new() { Interval=TimeSpan.FromMilliseconds(120) };
    private readonly Pointer _pointer = new(948,PointerType.Mouse,true);
    private bool _ticking;private int _stage;private Task<bool>? _opening;
    private string _source="",_undo="",_audio="";private object? _draft,_selection,_samples;private Yoake.Core.Audio.AudioClipInterval? _pending;
    private Guid? _document;private VideoViewState? _state;private IReadOnlyList<double>? _map;private byte[]? _raw;
    private IReadOnlyList<int>? _keys;
    private Window? _detached;private int _playFrame;private VideoPresentation _before;
    private byte[]? _beforePanPixels;
    private VisualOverlayControl Overlay=>window.FindControl<VisualOverlayControl>("VisualOverlay")!;
    private VideoPresentationControl Image=>window.FindControl<VideoPresentationControl>("VideoImage")!;
    public void Start(){_timer.Tick+=Tick;_timer.Start();}
    private async void Tick(object? sender,EventArgs args)
    {
        if(_ticking||!window.IsVisible||window.Bounds.Width<=0)return;_ticking=true;
        try
        {
            switch(_stage++)
            {
                case 0:
                    foreach(var id in Seven)Require(!model.Actions[id].CanExecute(null),"No-video availability: "+id);
                    var path=Path.Combine(options.ProfileDirectory!,"presentation.ass");File.WriteAllText(path,Fixture());Require(model.OpenSubtitle(path),"Presentation fixture opens.");
                    _audio=Path.Combine(options.VerificationMedia!,"av.avi");_opening=model.OpenMediaAsync(_audio);return;
                case 1:
                    if(_opening?.IsCompleted!=true){_stage--;return;}Require(await _opening,"Load independent native Audio/Video.");
                    await Invoke(CommandIds.ViewAudioVideo);await Invoke("video/open/dummy",new DummyVideoOptions{Width=320,Height=180,FrameCount=600,FramesPerSecond=25,Checkerboard=true});
                    var keyPath=Path.Combine(options.ProfileDirectory!,"pan-keys.txt");File.WriteAllText(keyPath,"# keyframe format v1\nfps 0\n0\n5\n175\n");await Invoke("keyframe/open",keyPath);_keys=model.Keyframes;
                    await Invoke("time/lead/out");model.Draft!.Layer="unfinished";
                    _document=model.ActiveDocumentId;_source=model.ActiveEditor!.Document.Serialize();_undo=model.ActiveEditor.Undo.NextUndoName??"";_draft=model.Draft;_selection=model.SelectedEvent;_pending=model.PrimaryAudioRange;_samples=model.WaveformSamples;
                    _map=model.FrameTimes;_state=model.VideoView;_raw=(await model.ExtractCurrentVideoFrameAsync(FrameImageKind.Raw)).Image.Pixels;
                    OpenZoom();return;
                case 2:Press(window.FindControl<MenuItem>("VideoZoom50Menu")!,Key.Enter);return;
                case 3:Zoom(.5,"50");OpenZoom();Press(window.FindControl<MenuItem>("VideoZoom100Menu")!,Key.Enter);return;
                case 4:Zoom(1,"100");OpenZoom();Press(window.FindControl<MenuItem>("VideoZoom200Menu")!,Key.Enter);return;
                case 5:
                    Zoom(2,"200");OpenZoom();Press(window.FindControl<MenuItem>("VideoZoomInMenu")!,Key.Enter);return;
                case 6:
                    Require(model.VideoWindowZoom==2.125,"Menu Zoom In adds exactly 12.5 percentage points.");OpenZoom();Press(window.FindControl<MenuItem>("VideoZoomOutMenu")!,Key.Enter);return;
                case 7:
                    Require(model.VideoWindowZoom==2,"Menu Zoom Out subtracts exactly 12.5 points.");
                    Require(model.IsCrosshairTool,"New documents start with the coordinate tool.");
                    var menu=Overlay.ContextMenu!.Items.OfType<MenuItem>().ToArray();
                    foreach(var id in new[]{"video/frame/save","video/frame/copy","video/frame/save/raw","video/frame/copy/raw","video/frame/save/subs","video/frame/copy/subs","video/copy_coordinates","video/reset_pan","video/detach"})
                        Require(menu.Any(item=>ReferenceEquals(item.Command,model.Actions[id])),"Video context menu exposes "+id);
                    Wheel(new(120,90),Avalonia.Input.KeyModifiers.None);Require(model.VideoWindowZoom==2.125&&model.VideoView!.ContentZoom==1,"Wheel resizes the video window, leaving content zoom alone.");
                    Wheel(new(120,90),Avalonia.Input.KeyModifiers.None,-1);Require(model.VideoWindowZoom==2,"Wheel down restores the window scale.");
                    Wheel(new(120,90),Avalonia.Input.KeyModifiers.Shift);Require(Math.Abs(model.VideoView!.PanY-5*window.RenderScaling)<.01,"Shift-wheel retains Yoake's existing pan gesture.");
                    await Invoke("video/reset_pan");
                    await Invoke("video/presentation/zoom","12.5%");Wheel(new(120,90),Avalonia.Input.KeyModifiers.None,-1);
                    Require(model.VideoWindowZoom==.125,"Window zoom remains at its minimum without an error.");await Invoke("video/presentation/zoom","200%");
                    Press(Overlay,Key.Add,Avalonia.Input.KeyModifiers.Control);Require(model.VideoWindowZoom==2.125,"Default Ctrl keypad Plus routes zoom.");
                    await Invoke("settings/hotkeys/set",new HotkeyChange(HotkeyContext.Video,"video/zoom/out",["F9"]));Press(Overlay,Key.F9);Require(model.VideoWindowZoom==2,"Live Video hotkey override routes zoom.");
                    _before=Area();_beforePanPixels=RenderImage();Pan(new(130,100),new(161,117));Require(!Overlay.IsPanning&&!model.HasGesture&&_pointer.Captured is null,"Middle release ends capture without subtitle editing.");
                    Require(Math.Abs(Area().X-_before.X-31)<.01&&Math.Abs(Area().Y-_before.Y-17)<.01,"Real middle drag translates image and overlay together.");Unchanged();
                    Require(!RenderImage().SequenceEqual(_beforePanPixels),"Actual clipped image pixels move during pan, not just stored coordinates.");
                    var target=new AssPoint(140.125,90.5);var mapped=Area().FromScript(target,320,180);Move(new(mapped.X,mapped.Y),RawInputModifiers.None);await Invoke("video/copy_coordinates");Require(await window.ReadClipboardAsync()=="140.125,90.5","Copy Coordinates uses zoom/pan transform.");
                    var anchor=Area().ToScript(140,100,320,180);Wheel(new(140,100),Avalonia.Input.KeyModifiers.Control);
                    var anchored=Area().FromScript(anchor,320,180);
                    Require(model.VideoView!.ContentZoom==1.125&&model.VideoWindowZoom==2&&Math.Abs(anchored.X-140)<.01&&Math.Abs(anchored.Y-100)<.01,"Ctrl-wheel magnifies around the pointer without changing window zoom.");
                    window.FindControl<MenuItem>("VideoMenu")!.IsSubMenuOpen=true;Press(window.FindControl<MenuItem>("VideoResetPanMenu")!,Key.Enter);return;
                case 8:
                    Require(model.VideoWindowZoom==2&&!model.VideoView!.ContentActive&&window.FindControl<MenuItem>("VideoZoom200Menu")!.IsChecked,"Reset Pan clears content zoom and pan, retains window zoom/radio.");Unchanged();
                    Down(new(120,90),true);Move(new(151,102),RawInputModifiers.MiddleMouseButton);Press(Overlay,Key.Escape);Require(!Overlay.IsPanning&&_pointer.Captured is null&&!model.VideoView.ContentActive,"Escape cancels pan and restores its checkpoint.");
                    Down(new(120,90),true);Move(new(145,102),RawInputModifiers.MiddleMouseButton);_pointer.Capture(null);Require(!Overlay.IsPanning&&!model.VideoView.ContentActive,"Capture loss cancels pan.");
                    Pan(new(130,100),new(144,109));await Invoke("video/aspect/wide");Require(model.VideoWindowZoom==2&&model.VideoView.PanX!=0,"Aspect keeps window zoom and pan.");
                    window.FindControl<MenuItem>("VideoMenu")!.IsSubMenuOpen=true;Press(window.FindControl<MenuItem>("VideoDetachMenu")!,Key.Enter);return;
                case 9:
                    _detached=window.DetachedVideoWindow;Require(_detached is {IsVisible:true,IsLoaded:true,CanResize:true}&&_detached.TryGetPlatformHandle() is {Handle: not 0},"Detach creates a real native resizable window.");
                    Require(ReferenceEquals(TopLevel.GetTopLevel(Overlay),_detached)&&!window.FindControl<Grid>("VisualColumn")!.IsVisible&&window.FindControl<MenuItem>("VideoDetachMenu")!.IsChecked,"Existing controls and tools moved, main host hidden, toggle checked.");
                    foreach(var id in ZoomIds)Require(!model.Actions[id].CanExecute(null),"Aegisub attached-only zoom command: "+id);
                    Require(ReferenceEquals(_map,model.FrameTimes)&&ReferenceEquals(_keys,model.Keyframes)&&ReferenceEquals(_state,model.VideoView),"Detach preserves external keys, map and presentation owner.");Unchanged();
                    Pan(new(100,100),new(120,111));Require(model.VideoView!.PanX!=0&&!model.HasGesture,"Detached pointer input pans same source.");
                    model.Draft!.Layer="0";await Invoke("video/tool/clip");
                    var a=Area().FromScript(new(130,70),320,180);var b=Area().FromScript(new(155,95),320,180);
                    Down(new(a.X,a.Y),false);Move(new(b.X,b.Y),RawInputModifiers.LeftMouseButton);Up(new(b.X,b.Y),false);
                    Require(AssVisualTags.Clip(model.SelectedEvent!.Text) is {Rectangular:true} clip&&Math.Abs(clip.Points[0].X-130)<.01&&Math.Abs(clip.Points[0].Y-70)<.01,"Detached visual clip edits exact script coordinates at non-default zoom/pan.");
                    Press(Overlay,Key.Z,Avalonia.Input.KeyModifiers.Control);Require(model.ActiveEditor!.Document.Serialize()==_source,"Visual gesture has one complete undo.");
                    _draft=model.Draft;model.Draft!.Layer="unfinished";_undo=model.ActiveEditor.Undo.NextUndoName??"";
                    var zoomInput=window.FindControl<VideoZoomControl>("VideoZoom")!.GetVisualDescendants().OfType<TextBox>().Single(input=>input.Name=="VideoZoomValue");zoomInput.Text="125%";
                    var presenter=zoomInput.GetVisualDescendants().OfType<TextPresenter>().First();presenter.PreeditText="日本";Press(zoomInput,Key.Enter);Require(presenter.PreeditText=="日本","Zoom input protects IME Enter.");presenter.PreeditText=null;Press(zoomInput,Key.Enter);return;
                case 10:
                    Require(model.VideoView!.WindowZoom==1.25,"Detached editable zoom invokes semantic action.");
                    _before=Area();Wheel(new(120,100),Avalonia.Input.KeyModifiers.Control);_before=Area();var px=model.VideoView.PanX;var py=model.VideoView.PanY;
                    _detached!.Width+=100;_detached.Height+=80;return;
                case 11:
                    Require(Math.Abs(Area().Height-_before.Height)<1,"Detached resize with active pan/content zoom preserves content height.");
                    await Invoke("video/jump",new VideoJumpRequest(Frame:175));_playFrame=model.CurrentFrame;Press(Overlay,Key.Space);Require(model.IsPlaying,"Detached Video uses existing central playback after retained Audio EOF.");return;
                case 12:
                    if(model.CurrentFrame<=_playFrame){_stage--;return;}
                    Require(model.VideoFrame is not null&&model.IsPlaying&&ReferenceEquals(Overlay.Model,model)&&ReferenceEquals(_map,model.FrameTimes),"Detached current frame and subtitle preview advance on the shared timeline.");
                    await Invoke("video/detach");Require(model.IsPlaying&&window.DetachedVideoWindow is null,"Reattach preserves active central playback.");
                    await Invoke("video/detach");_detached=window.DetachedVideoWindow;Require(model.IsPlaying&&_detached is not null,"Detach preserves the same active playback clock.");
                    Press(Overlay,Key.Escape);Require(!model.IsPlaying,"Detached Video Escape stops playback.");
                    _detached!.Close();return;
                case 13:
                    Require(window.DetachedVideoWindow is null&&!model.IsVideoDetached&&ReferenceEquals(TopLevel.GetTopLevel(Overlay),window)&&ReferenceEquals(_map,model.FrameTimes),"Native detached close reattaches without closing provider.");
                    Require(_detached is {IsVisible:false}&&_detached.TryGetPlatformHandle() is null&&!window.OwnedWindows.Contains(_detached),"Closed host releases native window and ownership without retaining controls.");Unchanged();
                    await Invoke("video/detach");model.Draft!.Layer="0";await Invoke(CommandIds.SubtitleNew);await Invoke("video/open/dummy",new DummyVideoOptions{Width=160,Height=90,FrameCount=30});return;
                case 14:
                    Require(window.DetachedVideoWindow is null&&!model.IsVideoDetached&&model.VideoView!.PanX==0,"New tab has independent embedded presentation.");
                    Require(model.IsVideoView && !model.HasAudio && !model.Actions[CommandIds.ViewAudio].CanExecute(null),
                        "Dummy-video document automatically selects video-only view.");
                    await Invoke(CommandIds.WorkspaceActivateTab,_document);return;
                case 15:
                    if(model.VideoFrame is null){_stage--;return;}
                    Require(window.DetachedVideoWindow is not null&&model.IsVideoDetached&&ReferenceEquals(_state,model.VideoView)&&ReferenceEquals(_map,model.FrameTimes),"Returning to tab restores detached host and exact owner.");
                    Require(model.IsAudioVideoView && model.HasAudio &&
                        model.Actions[CommandIds.ViewAudioVideo].CanExecute(null),
                        "Returning to a document with both providers automatically restores the valid combined view.");
                    await Invoke(CommandIds.ViewAudio);Require(window.DetachedVideoWindow.IsVisible,"Audio view does not dispose detached Video.");
                    await Invoke("video/focus_seek");Require(model.IsAudioView&&window.FindControl<VideoFrameSlider>("VideoSeekBar")!.IsKeyboardFocusWithin,"Detached seek focus does not replace the main Audio view.");await Invoke(CommandIds.ViewAudioVideo);
                    Pan(new(100,80),new(111,85));Down(new(100,80),true);Move(new(120,90),RawInputModifiers.MiddleMouseButton);
                    Require(Overlay.IsPanning&&_pointer.Captured==Overlay,"Close begins with real active middle capture.");
                    await Invoke("video/close");Require(window.DetachedVideoWindow is null&&model.HasAudio&&model.AudioSourcePath==_audio,"Video Close releases its host, keeps Audio and detached preference.");
                    Require(!Overlay.IsPanning&&_pointer.Captured is null&&!model.VideoView!.ContentActive,"Provider Close cancels captured pan without restoring the old source's offset.");
                    await Invoke("recent/video",model.RecentVideoItems.First(p=>p.Path.Contains(":320:180:",StringComparison.Ordinal)).Path);return;
                case 16:
                    Require(window.DetachedVideoWindow is not null&&model.IsVideoDetached&&model.HasAudio,"Recent Video reopens detached presentation and keeps Audio.");
                    var tc=Path.Combine(options.ProfileDirectory!,"pan-vfr.txt");File.WriteAllText(tc,"# timecode format v2\n0\n40\n110\n");await Invoke("timecode/open",tc);
                    Require(!await model.Registry.InvokeAsync("video/zoom/100",new(model.ActiveDocumentId)),"Attached-only preset rejects detached invocation.");return;
                case 17:
                    await Invoke("video/detach");await Invoke("video/zoom/100");await Invoke("video/detach");var retained=model.FrameTimes;
                    Require(model.Keyframes.SequenceEqual(new[]{0,5,175})&&model.ExternalTimecodePath is not null,"External keys/timecodes survive tab and Close/Recent hosting.");
                    await Invoke("audio/close");Require(!model.HasAudio&&window.DetachedVideoWindow is not null&&ReferenceEquals(retained,model.FrameTimes),"Audio Close preserves detached Video and external timing.");
                    Require((await model.ExtractCurrentVideoFrameAsync(FrameImageKind.Raw)).Image.Pixels.SequenceEqual(_raw!),"Zoom, pan, host and aspect stay out of source exports.");
                    Require(StartupDiagnostics.BindingWarningCount==0&&StartupDiagnostics.FrameworkErrorCount==0,"No bindings or native window errors.");
                    _timer.Stop();options.FinishVerification("PASS: separate packaged Video presentation; real zoom preset/in/out menu and default/remapped keyboard routing, physical source/DPI mapping and radio/availability, middle capture/release/Escape/loss with no source edit, anchored content zoom/reset, script clipboard and visual clipping/one-step undo at zoom/pan; real native detach, same controls/providers/central playback, editable zoom IME, resize, native close/reattach, tab ownership, view modes, Video Close/Recent/dummy/external timecodes and Audio Close retention; source-resolution export unchanged.\n");StartupDiagnostics.Complete();desktop.Shutdown(0);return;
            }
        }
        catch(Exception exception){_timer.Stop();Program.Fatal("Packaged Video presentation stage "+(_stage-1),exception);desktop.Shutdown(1);}
        finally{_ticking=false;}
    }
    private static readonly string[] ZoomIds=["video/zoom/50","video/zoom/100","video/zoom/200","video/zoom/in","video/zoom/out"];
    private static readonly string[] Seven=[..ZoomIds,"video/reset_pan","video/detach"];
    private VideoPresentation Area(){var r=Image.VideoArea;return new(r.X,r.Y,r.Width,r.Height);}
    private byte[] RenderImage()
    {
        using var bitmap=new RenderTargetBitmap(new PixelSize(Math.Max(1,(int)Image.Bounds.Width),Math.Max(1,(int)Image.Bounds.Height)),new Vector(96,96));bitmap.Render(Image);
        var pixels=new byte[bitmap.PixelSize.Width*bitmap.PixelSize.Height*4];var pinned=GCHandle.Alloc(pixels,GCHandleType.Pinned);
        try{bitmap.CopyPixels(new PixelRect(bitmap.PixelSize),pinned.AddrOfPinnedObject(),pixels.Length,bitmap.PixelSize.Width*4);}finally{pinned.Free();}return pixels;
    }
    private void Zoom(double scale,string suffix)
    {
        Require(model.VideoWindowZoom==scale&&window.FindControl<MenuItem>("VideoZoom"+suffix+"Menu")!.IsChecked,"Zoom radio: "+suffix);
        var host=window.FindControl<Border>("VideoCanvasHost")!;
        var expectedHeight=180*scale/window.RenderScaling;
        var row=window.FindControl<Grid>("VideoRegion")!.RowDefinitions[0].ActualHeight;
        Require(Math.Abs(host.Bounds.Height-Math.Min(expectedHeight,row))<1,
            "Window zoom must resize the video *canvas*, not just the image inside the old cell.");
        Require(Math.Abs(Image.VideoArea.Height-expectedHeight)<.01,"Zoom is source height in physical pixels.");
        Require(Math.Abs(Area().Height-expectedHeight)<.01,"Image and visual tool use the same source zoom geometry.");
        Require(Math.Abs(Image.Bounds.Height-Overlay.Bounds.Height)<.01&&Math.Abs(Image.Bounds.Width-Overlay.Bounds.Width)<.01,
            "Image and visual tool have the same actual viewport bounds.");
    }
    private void OpenZoom(){window.FindControl<MenuItem>("VideoMenu")!.IsSubMenuOpen=true;window.FindControl<MenuItem>("VideoZoomMenu")!.IsSubMenuOpen=true;}
    private void Unchanged()=>Require(model.ActiveEditor!.Document.Serialize()==_source&&(model.ActiveEditor.Undo.NextUndoName??"")==_undo&&ReferenceEquals(model.Draft,_draft)&&ReferenceEquals(model.SelectedEvent,_selection)&&model.PrimaryAudioRange==_pending&&ReferenceEquals(model.WaveformSamples,_samples)&&model.AudioSourcePath==_audio,"Presentation preserves ASS, draft, selection, pending timing, undo and independent Audio.");
    private async Task Invoke(string id,object? p=null)=>Require(await model.Registry.InvokeAsync(id,new(model.ActiveDocumentId),p),"Presentation command: "+id);
    private static void Press(Control c,Key key,Avalonia.Input.KeyModifiers modifiers=Avalonia.Input.KeyModifiers.None)
    {(TopLevel.GetTopLevel(c) as Window)?.Activate();Require(c.Focus(),"Input focus: "+c.Name);c.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Source=c,Key=key,KeyModifiers=modifiers});}
    private TopLevel Host=>TopLevel.GetTopLevel(Overlay)!;
    private Point Root(Point p)=>Overlay.TranslatePoint(p,Host)!.Value;
    private void Down(Point p,bool middle){Overlay.Focus();Overlay.RaiseEvent(new PointerPressedEventArgs(Overlay,_pointer,Host,Root(p),0,new(middle?RawInputModifiers.MiddleMouseButton:RawInputModifiers.LeftMouseButton,middle?PointerUpdateKind.MiddleButtonPressed:PointerUpdateKind.LeftButtonPressed),Avalonia.Input.KeyModifiers.None));}
    private void Move(Point p,RawInputModifiers modifiers)=>Overlay.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent,Overlay,_pointer,Host,Root(p),0,new(modifiers,PointerUpdateKind.Other),Avalonia.Input.KeyModifiers.None));
    private void Up(Point p,bool middle)=>Overlay.RaiseEvent(new PointerReleasedEventArgs(Overlay,_pointer,Host,Root(p),0,new(RawInputModifiers.None,middle?PointerUpdateKind.MiddleButtonReleased:PointerUpdateKind.LeftButtonReleased),Avalonia.Input.KeyModifiers.None,middle?MouseButton.Middle:MouseButton.Left));
    private void Pan(Point a,Point b){Down(a,true);Move(b,RawInputModifiers.MiddleMouseButton);Up(b,true);}
    private void Wheel(Point p,Avalonia.Input.KeyModifiers modifiers,double delta=1)=>Overlay.RaiseEvent(new PointerWheelEventArgs(Overlay,_pointer,Host,Root(p),0,new(RawInputModifiers.None,PointerUpdateKind.Other),modifiers,new(0,delta)));
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private static string Fixture()=>"[Script Info]\nPlayResX: 320\nPlayResY: 180\n[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\nStyle: Default,Arial,20,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,2,1,2,10,10,10,1\n[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\nDialogue: 0,0:00:00.00,0:00:20.00,Default,,0,0,0,,{\\pos(160,90)\\unknown(foo)}日本 မြန်မာ preview\n";
}
