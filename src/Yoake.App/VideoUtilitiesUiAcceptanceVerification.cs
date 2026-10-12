using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yoake.Core.Commands;
using Yoake.Core.Logging;
using Yoake.Core.Media;
using Yoake.Core.Subtitles;
using Yoake.Native;
using Yoake.UI;
using Yoake.UI.Controls;
using Yoake.UI.ViewModels;

namespace Yoake.App;

// Separate packaged integration process; no extra startup-verifier responsibilities.
internal sealed class VideoUtilitiesUiAcceptanceVerification(IClassicDesktopStyleApplicationLifetime desktop,
    MainWindow window, MainWindowViewModel model, StartupOptions options)
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Pointer _pointer = new(947, PointerType.Mouse, true);
    private bool _ready, _ticking; private int _stage;
    private Task<bool>? _opening;
    private string _source = "", _undo = "", _audio = "";
    private object? _draft, _selected, _samples;
    private Yoake.Core.Audio.AudioClipInterval? _pending;
    private byte[]? _raw, _full, _withoutMask;
    private VideoFrameCapture? _subs;
    private VisualOverlayControl Overlay => window.FindControl<VisualOverlayControl>("VisualOverlay")!;
    private VideoPresentationControl Video => window.FindControl<VideoPresentationControl>("VideoImage")!;
    public void Start() { window.LayoutUpdated += (_, _) => _ready = window.IsVisible && window.Bounds.Width > 0; _timer.Tick += Tick; _timer.Start(); }
    private async void Tick(object? sender, EventArgs args)
    {
        if (!_ready || _ticking) return; _ticking = true;
        try
        {
            switch (_stage++)
            {
                case 0:
                    Require(!model.Actions["video/frame/copy"].CanExecute(null) && !model.Actions["video/details"].CanExecute(null), "Source utilities require Video.");
                    File.WriteAllText(Path.Combine(options.ProfileDirectory!, "utilities.ass"), Fixture());
                    Require(model.OpenSubtitle(Path.Combine(options.ProfileDirectory!, "utilities.ass")), "Open native image fixture.");
                    _audio = Path.Combine(options.VerificationMedia!, "av.avi"); _opening = model.OpenMediaAsync(_audio); return;
                case 1:
                    if (_opening?.IsCompleted != true) { _stage--; return; } Require(await _opening, "Real providers load.");
                    await Invoke(CommandIds.ViewAudioVideo); await Invoke("time/lead/out"); model.Draft!.Layer = "unfinished";
                    _source = model.ActiveEditor!.Document.Serialize(); _undo = model.ActiveEditor.Undo.NextUndoName ?? "";
                    _draft = model.Draft; _selected = model.SelectedEvent; _pending = model.PrimaryAudioRange; _samples = model.WaveformSamples;
                    var tc = Path.Combine(options.ProfileDirectory!, "utilities-vfr.txt");
                    File.WriteAllText(tc, "# timecode format v2\n" + string.Join('\n', Enumerable.Range(0, model.FrameTimes.Count).Select(i => i * 100 + i % 2 * 20)) + "\n");
                    await Invoke("timecode/open", tc); await Invoke("video/jump", new VideoJumpRequest(Frame: 3));
                    var raw = await model.ExtractCurrentVideoFrameAsync(FrameImageKind.Raw); _raw = raw.Image.Pixels;
                    Require(raw.Identity.Frame == 3 && raw.Identity.Seconds == model.FrameTimes[3] && raw.Image.Width == 160 && raw.Image.Height == 90, "Extraction captures native source dimensions/current external VFR frame time.");
                    using (var provider = DocumentMedia.Open(_audio, tracks: MediaTracks.Video)) Require(_raw.SequenceEqual(provider.VideoProvider!.GetFrame(3).Pixels), "Raw extraction decodes the exact source frame index under external timecodes.");
                    _subs = await model.ExtractCurrentVideoFrameAsync(FrameImageKind.Subtitles); VerifyCoverage(_subs.Image);
                    var full = await model.ExtractCurrentVideoFrameAsync(FrameImageKind.Full);
                    _full = full.Image.Pixels.ToArray();
                    Require(!full.Image.Pixels.SequenceEqual(_raw) && full.Image.Pixels.Where((_, i) => i % 4 == 3).All(a => a == 255), "Full export composes real subtitles while preserving source alpha.");
                    foreach (var suffix in new[] { "", "/raw", "/subs" })
                    {
                        await Invoke("video/frame/copy" + suffix); using var data = await window.Clipboard!.TryGetDataAsync();
                        using var bitmap = data is null ? null : await data.TryGetBitmapAsync(); Require(bitmap is not null && bitmap.PixelSize == new PixelSize(160, 90), "Windows image clipboard: " + suffix);
                        var copied = Pixels(bitmap!);
                        if (suffix == "/subs") Require(copied.Where((_,i) => i%4==3).Any(a => a is >0 and <255) && copied[3] == 0, "Windows clipboard retains subtitle alpha.");
                        if (suffix == "/raw") Require(copied.SequenceEqual(_raw), "Windows raw clipboard pixels equal decoded source.");
                        var destination = Path.Combine(options.ProfileDirectory!, "日本語 မြန်မာ" + (suffix.Length == 0 ? "full" : suffix[1..]) + ".png");
                        await Invoke("video/frame/save" + suffix, destination); VerifyPng(destination, 160, 90, suffix == "/subs");
                    }
                    var previous = model.Dialogs; model.Dialogs = new ClipboardFailureDialogs(previous!);
                    Require(!await model.Registry.InvokeAsync("video/frame/copy/raw", new(model.ActiveDocumentId)), "Clipboard failure crosses centralized command reporting."); model.Dialogs = previous; Unchanged();
                    var validClipboardCapture=true;
                    var clipboardReady=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var delayedCopy=window.WriteFrameClipboardAsync(_subs.Image,()=>{clipboardReady.TrySetResult();return validClipboardCapture;},CancellationToken.None);
                    await clipboardReady.Task;
                    validClipboardCapture=false;
                    var canceledClipboard=false;
                    try { await delayedCopy; } catch(OperationCanceledException) { canceledClipboard=true; }
                    Require(canceledClipboard, "Delayed Windows clipboard rendering rejects an obsolete capture.");
                    using(var staleData=await window.Clipboard!.TryGetDataAsync())
                    { using var staleBitmap=staleData is null?null:await staleData.TryGetBitmapAsync();Require(staleBitmap is null,"Obsolete delayed clipboard rendering cannot persist old image pixels."); }
                    var badDestination = Path.Combine(options.ProfileDirectory!, "missing", "failed.png");
                    Require(!await model.Registry.InvokeAsync("video/frame/save/raw", new(model.ActiveDocumentId), badDestination) && !File.Exists(badDestination), "Failed PNG has no half-written destination.");
                    Require(!Directory.GetFiles(options.ProfileDirectory!, "*.tmp").Any(), "PNG failures clean sibling temporary files.");
                    window.FindControl<MenuItem>("VideoMenu")!.IsSubMenuOpen = true; return;
                case 2:
                    window.FindControl<MenuItem>("VideoAspectMenu")!.IsSubMenuOpen = true; return;
                case 3:
                    Press(window.FindControl<MenuItem>("VideoAspectwideMenu")!, Key.Enter); return;
                case 4:
                    Require(model.VideoAspect.Kind == VideoAspectKind.Wide && window.FindControl<MenuItem>("VideoAspectwideMenu")!.IsChecked, "Aspect menu Enter applies checked radio state.");
                    foreach (var preset in new[] { "default", "full", "cinematic" }) { await Invoke("video/aspect/" + preset); Require(model.Actions["video/aspect/" + preset].IsChecked, "Aspect preset: " + preset); }
                    window.FindControl<MenuItem>("VideoMenu")!.IsSubMenuOpen = true; return;
                case 5:
                    window.FindControl<MenuItem>("VideoAspectMenu")!.IsSubMenuOpen = true; return;
                case 6:
                    Press(window.FindControl<MenuItem>("VideoAspectcustomMenu")!, Key.Enter); return;
                case 7:
                    var aspect = window.OwnedWindows.OfType<VideoAspectWindow>().Single(); var input = aspect.FindControl<TextBox>("AspectValue")!;
                    input.Text = "16:0"; Press(aspect.FindControl<Button>("AspectConfirm")!, Key.Enter);
                    Require(window.OwnedWindows.Contains(aspect) && !string.IsNullOrEmpty(aspect.FindControl<TextBlock>("AspectError")!.Text), "Invalid custom aspect stays editable.");
                    input.Text = "853x480"; Require(input.Focus(), "Aspect input focus.");
                    var presenter = input.GetVisualDescendants().OfType<TextPresenter>().First(); presenter.PreeditText = "日本";
                    Press(input, Key.Enter); Require(window.OwnedWindows.Contains(aspect) && presenter.PreeditText == "日本", "Aspect IME Enter cannot accept or lose preedit.");
                    Press(input, Key.Escape); Require(window.OwnedWindows.Contains(aspect), "Aspect IME Escape cannot close."); presenter.PreeditText = null;
                    Press(aspect.FindControl<Button>("AspectConfirm")!, Key.Enter); return;
                case 8:
                    Require(!window.OwnedWindows.OfType<VideoAspectWindow>().Any() && Math.Abs(model.VideoAspectRatio - 853d / 480) < 1e-9 && model.Actions["video/aspect/custom"].IsChecked, "Custom aspect is applied through the command.");
                    var viewport = model.VideoViewport(Overlay.Bounds.Width, Overlay.Bounds.Height); var expected = new AssPoint(40.125, 20.5); var mapped = viewport.FromScript(expected, model.ScriptSize.Width, model.ScriptSize.Height);
                    Move(new(mapped.X, mapped.Y)); await Invoke("video/copy_coordinates"); Require(await window.ReadClipboardAsync() == "40.125,20.5", "Actual pointer copies script coordinates through aspect/letterbox.");
                    Move(new(viewport.X - 2, viewport.Y + viewport.Height / 2)); await Invoke("video/copy_coordinates");
                    Require((await window.ReadClipboardAsync())!.StartsWith('-'), "Letterbox pointer extrapolates script coordinates, never control pixels.");
                    _withoutMask = RenderVideo(); window.FindControl<MenuItem>("VideoMenu")!.IsSubMenuOpen = true; return;
                case 9:
                    Press(window.FindControl<MenuItem>("VideoOverscanMenu")!, Key.Enter); return;
                case 10:
                    Require(model.ShowVideoOverscan && window.FindControl<MenuItem>("VideoOverscanMenu")!.IsChecked && !RenderVideo().SequenceEqual(_withoutMask!), "Overscan menu draws a real presentation mask.");
                    Require((await model.ExtractCurrentVideoFrameAsync(FrameImageKind.Raw)).Image.Pixels.SequenceEqual(_raw!), "Aspect and overscan never enter source export."); Unchanged();
                    Require((await model.ExtractCurrentVideoFrameAsync(FrameImageKind.Full)).Image.Pixels.SequenceEqual(_full!), "Full export also excludes aspect and overscan presentation state.");
                    window.FindControl<MenuItem>("VideoMenu")!.IsSubMenuOpen = true; return;
                case 11:
                    Press(window.FindControl<MenuItem>("VideoDetailsMenu")!, Key.Enter); return;
                case 12:
                    var details = window.OwnedWindows.OfType<VideoDetailsWindow>().Single();
                    Require(details.FindControl<TextBox>("DetailsSource")!.Text == _audio && details.FindControl<TextBox>("DetailsResolution")!.Text!.StartsWith("160×90") && details.FindControl<TextBox>("DetailsDecoder")!.Text!.Contains("FFMS2"), "Details dialog exposes source-backed native metadata.");
                    Press(details.FindControl<Button>("DetailsConfirm")!, Key.Enter); window.FindControl<MenuItem>("VideoMenu")!.IsSubMenuOpen = true; return;
                case 13:
                    Press(window.FindControl<MenuItem>("OpenDummyVideoMenu")!, Key.Enter); return;
                case 14:
                    var dummy = window.OwnedWindows.OfType<DummyVideoWindow>().Single();
                    Require(dummy.FindControl<TextBox>("DummyWidth")!.Text == "640" && dummy.FindControl<TextBox>("DummyHeight")!.Text == "480" && dummy.FindControl<TextBox>("DummyFps")!.Text == "23.976" && dummy.FindControl<TextBox>("DummyFrames")!.Text == "40000", "Dummy dialog uses exact release defaults.");
                    dummy.FindControl<TextBox>("DummyWidth")!.Text = "0"; Press(dummy.FindControl<Button>("DummyConfirm")!, Key.Enter);
                    Require(window.OwnedWindows.Contains(dummy), "Dummy invalid resolution stays editable.");
                    dummy.FindControl<TextBox>("DummyWidth")!.Text = "320"; dummy.FindControl<TextBox>("DummyHeight")!.Text = "180";
                    dummy.FindControl<TextBox>("DummyFps")!.Text = "25"; dummy.FindControl<TextBox>("DummyFrames")!.Text = "200"; dummy.FindControl<CheckBox>("DummyPattern")!.IsChecked = true;
                    var fpsInput = dummy.FindControl<TextBox>("DummyFps")!; Require(fpsInput.Focus(), "Dummy FPS input focus.");
                    var preedit = fpsInput.GetVisualDescendants().OfType<TextPresenter>().First(); preedit.PreeditText = "မြန်မာ";
                    Press(fpsInput, Key.Enter); Require(window.OwnedWindows.Contains(dummy), "Dummy IME Enter cannot accept."); preedit.PreeditText = null;
                    Press(dummy.FindControl<Button>("DummyConfirm")!, Key.Enter); return;
                case 15:
                    if (model.CurrentVideoDetails?.Width != 320 || model.VideoFrame is null) { Require(_timer.IsEnabled, "Dummy publication must finish."); _stage--; return; }
                    Require(model.AudioSourcePath == _audio && ReferenceEquals(_samples, model.WaveformSamples) && model.FrameTimes.Count == 200 && model.VideoAspect.Kind == VideoAspectKind.Default, "Dummy replaces only Video and resets aspect.");
                    Require(model.RecentVideoItems[0].Path == model.VideoSourcePath && DummyVideoOptions.TrySourceName(model.RecentVideoItems[0].Path, out _), "Source-derived Recent Video stores a reopenable typed dummy configuration.");
                    Require(model.CurrentVideoDetails!.Decoder.Contains("dummy") && model.CurrentVideoDetails.FramesPerSecond == 25, "Dummy details are real source values.");
                    var dummyRaw = await model.ExtractCurrentVideoFrameAsync(FrameImageKind.Raw); Require(dummyRaw.Image.Width == 320 && dummyRaw.Image.Height == 180, "Dummy extraction dimensions.");
                    Require(!dummyRaw.Image.Pixels[..4].SequenceEqual(dummyRaw.Image.Pixels[32..36]), "Dummy source generates its 8×8 checkerboard.");
                    await Invoke("video/jump", new VideoJumpRequest(Frame: 2)); await Invoke("video/play"); return;
                case 16:
                    Require(model.IsPlaying && model.VideoFrame is not null, "Dummy uses central Video playback with retained Audio.");
                    using (var canceledCommand = new CancellationTokenSource())
                    {
                        canceledCommand.Cancel();
                        foreach(var command in new[]{"video/aspect/full","video/details","video/open/dummy","video/frame/copy/raw"})
                            Require(!await model.Registry.InvokeAsync(command,new(model.ActiveDocumentId),cancellationToken:canceledCommand.Token),"Canceled utility cannot open a dialog, copy or stop playback: "+command);
                        Require(model.IsPlaying && model.VideoAspect.Kind == VideoAspectKind.Default && !window.OwnedWindows.Any(),"Canceled utilities retain playback and presentation.");
                    }
                    await Invoke("video/stop");
                    await Invoke("video/jump", new VideoJumpRequest(Frame: 3)); var dummySubs = await model.ExtractCurrentVideoFrameAsync(FrameImageKind.Subtitles); VerifyCoverage(dummySubs.Image);
                    await Invoke("timecode/close"); await Invoke("video/jump", new VideoJumpRequest(Frame: 175));
                    var blank = await model.ExtractCurrentVideoFrameAsync(FrameImageKind.Subtitles);
                    Require(blank.Image.Pixels.All(p => p == 0), "A blank subtitle frame is entirely transparent black.");
                    await Invoke("video/jump", new VideoJumpRequest(Frame: 3));
                    foreach (var suffix in new[] { "", "/raw", "/subs" }) await Invoke("video/frame/save" + suffix);
                    Require(Directory.GetFiles(options.ProfileDirectory!, "dummy_*_3.png").Length == 3, "Automatic dummy snapshots use script folder and collision-safe release naming.");
                    foreach (var path in Directory.GetFiles(options.ProfileDirectory!, "dummy_*_3.png")) VerifyPng(path, 320, 180, false);
                    var stale = model.ExtractCurrentVideoFrameAsync(FrameImageKind.Subtitles); await Invoke("video/frame/next");
                    Require(await Rejected(stale), "A stale frame/preview extraction cannot publish.");
                    var replaced = model.ExtractCurrentVideoFrameAsync(FrameImageKind.Raw); await Invoke("video/close"); Require(await Rejected(replaced), "Closed provider extraction cannot publish.");
                    Require(model.VideoSourcePath is null && model.AudioSourcePath == _audio && model.WaveformSamples is not null, "Dummy Close releases only Video.");
                    Unchanged(); await Invoke("recent/video", model.RecentVideoItems[0].Path);
                    Require(model.CurrentVideoDetails?.Width == 320 && model.AudioSourcePath == _audio, "Typed dummy Recent Video reopens only Video through the same backend.");
                    await Invoke("video/jump", new VideoJumpRequest(Frame: 3));
                    await window.WriteClipboardAsync("frame menu marker"); window.FindControl<MenuItem>("VideoMenu")!.IsSubMenuOpen = true; return;
                case 17:
                    window.FindControl<MenuItem>("VideoFrameImageMenu")!.IsSubMenuOpen = true; return;
                case 18:
                    Press(window.FindControl<MenuItem>("FramecopysubsMenu")!, Key.Enter); return;
                case 19:
                    using (var clipboardData = await window.Clipboard!.TryGetDataAsync())
                    using (var menuBitmap = clipboardData is null ? null : await clipboardData.TryGetBitmapAsync())
                        Require(menuBitmap?.PixelSize == new PixelSize(320,180), "Actual Frame Copy Subtitles menu Enter publishes an image through the semantic command.");
                    window.FindControl<MenuItem>("VideoMenu")!.IsSubMenuOpen = true; return;
                case 20:
                    window.FindControl<MenuItem>("VideoFrameImageMenu")!.IsSubMenuOpen = true; return;
                case 21:
                    Press(window.FindControl<MenuItem>("FramesavesubsMenu")!, Key.Enter); return;
                case 22:
                    var automatic = Directory.GetFiles(options.ProfileDirectory!, "dummy_*_3.png");
                    Require(automatic.Length == 4, "Actual Save Subtitles menu produces another unique automatic snapshot.");
                    VerifyPng(automatic.Order().Last(),320,180,true);
                    Require(StartupDiagnostics.BindingWarningCount == 0 && StartupDiagnostics.FrameworkErrorCount == 0, "Video utility windows, controls and menus have no binding/framework errors.");
                    _timer.Stop(); options.FinishVerification("PASS: separate packaged Video utilities; native current-frame/VFR raw/full/subtitles source extraction and real RGBA coverage (opaque, antialias, outline, shadow, ASS alpha, clipping, blank); Windows image clipboard variants with transparency and reported failure; Unicode atomic PNG headers/dimensions/RGBA and failure cleanup; real aspect menu radio/custom validation/IME; shared aspect/letterbox script pointer clipboard; real overscan presentation-only pixels; native Video Details; exact Dummy defaults/dialog validation/IME, internal checkerboard/navigation/playback/subtitles/automatic PNG naming/close with independent Audio; stale frame/provider rejection and document/draft/pending/selection/undo retention.\n"); StartupDiagnostics.Complete(); desktop.Shutdown(0); return;
            }
        }
        catch (Exception e) { _timer.Stop(); Program.Fatal("Packaged Video utilities at stage " + (_stage - 1), e); desktop.Shutdown(1); }
        finally { _ticking = false; }
    }
    private static string Fixture() => "[Script Info]\nScriptType: v4.00+\nPlayResX: 160\nPlayResY: 90\n[V4+ Styles]\nFormat: " + string.Join(',', AssDocument.StyleFormat) + "\nStyle: Default,Arial,22,&H00FFFFFF,&H0000FFFF,&H00FF0000,&H0000FF00,0,0,0,0,100,100,0,0,1,2,3,7,0,0,0,1\n[Events]\nFormat: " + string.Join(',', AssDocument.EventFormat) + "\n" +
        "Dialogue: 0,0:00:00.00,0:00:04.00,Default,A,0,0,0,,{\\future(x)\\an7\\pos(8,8)\\bord0\\shad0\\1c&H0000FF&\\p1}m 0 0 l 20 0 20 20 0 20\n" +
        "Dialogue: 0,0:00:00.00,0:00:04.00,Default,B,0,0,0,,{\\an7\\pos(40,8)\\bord0\\shad0\\1c&H0000FF&\\alpha&H80&\\p1}m 0 0 l 20 0 20 20 0 20\n" +
        "Dialogue: 0,0:00:00.00,0:00:04.00,Default,C,0,0,0,,{\\an7\\pos(90,10)\\1c&HFFFFFF&\\3c&HFF0000&\\4c&H00FF00&}H\n" +
        "Dialogue: 0,0:00:00.00,0:00:04.00,Default,D,0,0,0,,{\\an7\\pos(8,50)\\bord0\\shad0\\clip(12,55,22,65)\\1c&H0000FF&\\p1}m 0 0 l 30 0 30 30 0 30\n";
    private static void VerifyCoverage(DecodedVideoFrame image)
    {
        int Index(int x, int y) => ((y * image.Height / 90) * image.Width + x * image.Width / 160) * 4;
        byte Alpha(int x, int y) => image.Pixels[Index(x,y)+3];
        Require(Alpha(12,12) == 255 && Alpha(0,0) == 0, "Opaque drawing interior and transparent outside.");
        Require(Alpha(45,12) is >=126 and <=128, "ASS alpha is rendered as actual partial coverage.");
        Require(Alpha(15,60) == 255 && Alpha(10,60) == 0 && Alpha(25,60) == 0, "Subtitle export obeys rectangular clipping.");
        var glyph = Enumerable.Range(0,image.Width*image.Height).Where(i => i%image.Width >= 80*image.Width/160).ToArray();
        Require(glyph.Any(i => image.Pixels[i*4+3] is >0 and <255), "Glyph antialiasing retains fractional alpha.");
        Require(glyph.Any(i => image.Pixels[i*4+3] == 255 && image.Pixels[i*4]>240 && image.Pixels[i*4+1]>240 && image.Pixels[i*4+2]>240), "Opaque white glyph interiors retain full alpha.");
        Require(glyph.Any(i => image.Pixels[i*4+3]>0 && image.Pixels[i*4]>image.Pixels[i*4+2]+20), "Rendered blue outline coverage.");
        Require(glyph.Any(i => image.Pixels[i*4+3]>0 && image.Pixels[i*4+1]>image.Pixels[i*4+2]+20), "Rendered green shadow coverage.");
    }
    private static void VerifyPng(string path, int width, int height, bool transparent)
    {
        var encoded = File.ReadAllBytes(path); Require(encoded.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}), "Valid PNG signature.");
        Require(BinaryPrimitives.ReadInt32BigEndian(encoded.AsSpan(16,4)) == width && BinaryPrimitives.ReadInt32BigEndian(encoded.AsSpan(20,4)) == height, "PNG IHDR is source resolution.");
        using var bitmap = new Bitmap(path); Require(bitmap.PixelSize == new PixelSize(width,height), "PNG decodes completely.");
        if (transparent) { var pixels = Pixels(bitmap); Require(encoded[25] == 6 && pixels[3] == 0 && pixels.Where((_,i) => i%4==3).Any(a => a is >0 and <255), "PNG is actual RGBA with partial and transparent coverage."); }
    }
    private static byte[] Pixels(Bitmap bitmap)
    {
        var result = new byte[bitmap.PixelSize.Width*bitmap.PixelSize.Height*4]; var pinned = GCHandle.Alloc(result, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), pinned.AddrOfPinnedObject(), result.Length, bitmap.PixelSize.Width*4); }
        finally { pinned.Free(); }
        if (bitmap.Format == PixelFormat.Rgba8888) for(var i=0;i<result.Length;i+=4)(result[i],result[i+2])=(result[i+2],result[i]);
        return result;
    }
    private byte[] RenderVideo()
    { using var bitmap = new RenderTargetBitmap(new PixelSize(Math.Max(1,(int)Video.Bounds.Width),Math.Max(1,(int)Video.Bounds.Height)), new Vector(96,96)); bitmap.Render(Video); return Pixels(bitmap); }
    private void Move(Point local)
    { var root=Overlay.TranslatePoint(local,window)!.Value; Overlay.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent,Overlay,_pointer,window,root,0,new(RawInputModifiers.None,PointerUpdateKind.Other),KeyModifiers.None)); }
    private void Unchanged() => Require(ReferenceEquals(_draft,model.Draft) && ReferenceEquals(_selected,model.SelectedEvent) && ReferenceEquals(_samples,model.WaveformSamples) && _pending==model.PrimaryAudioRange && _source==model.ActiveEditor!.Document.Serialize() && _undo==(model.ActiveEditor.Undo.NextUndoName??""), "Video utility actions retain exact editing and independent Audio state.");
    private async Task Invoke(string id,object? parameter=null) => Require(await model.Registry.InvokeAsync(id,new(model.ActiveDocumentId),parameter), "Video utilities command: " + id);
    private static async Task<bool> Rejected(Task<VideoFrameCapture> work) { try { await work; return false; } catch(OperationCanceledException) { return true; } }
    private static void Press(Control control,Key key)
    { (TopLevel.GetTopLevel(control) as Window)?.Activate(); Require(control.Focus() && control.IsKeyboardFocusWithin, "Video utility input focus: " + control.Name); control.RaiseEvent(new KeyEventArgs { RoutedEvent=InputElement.KeyDownEvent, Source=control, Key=key }); }
    private static void Require(bool condition,string message) { if(!condition)throw new InvalidOperationException(message); }
    private sealed class ClipboardFailureDialogs(Yoake.UI.Services.IEditorDialogs inner) : Yoake.UI.Services.IEditorDialogs
    {
        public Task WriteFrameClipboardAsync(DecodedVideoFrame image,Func<bool> current,CancellationToken token)=>throw new IOException("Injected clipboard failure.");
        public Task<string?> OpenSubtitleAsync()=>inner.OpenSubtitleAsync(); public Task<string?> OpenMediaAsync()=>inner.OpenMediaAsync();
        public Task<string?> SaveSubtitleAsync(string name)=>inner.SaveSubtitleAsync(name); public Task<Yoake.UI.Services.UnsavedChoice> ConfirmUnsavedAsync(string title)=>inner.ConfirmUnsavedAsync(title);
        public Task<bool> ConfirmRevertAsync()=>inner.ConfirmRevertAsync(); public Task<string?> ReadClipboardAsync()=>inner.ReadClipboardAsync(); public Task WriteClipboardAsync(string text)=>inner.WriteClipboardAsync(text);
        public Task ShowStylesAsync(SubtitleEditor editor)=>inner.ShowStylesAsync(editor); public Task ShowScriptInfoAsync(SubtitleEditor editor)=>inner.ShowScriptInfoAsync(editor); public Task ShowFindAsync(MainWindowViewModel viewModel)=>inner.ShowFindAsync(viewModel);
    }
}
