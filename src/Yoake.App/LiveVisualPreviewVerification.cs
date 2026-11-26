using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Yoake.Core.Commands;
using Yoake.Core.Media;
using Yoake.Core.Subtitles;
using Yoake.Native;
using Yoake.UI;
using Yoake.UI.Controls;
using Yoake.UI.ViewModels;

namespace Yoake.App;

internal static class LiveVisualPreviewVerification
{
    public static async Task VerifyAsync(MainWindow window,MainWindowViewModel model,StartupOptions options)
    {
        async Task Invoke(string id,object? parameter=null)=>Require(await model.Registry.InvokeAsync(id,new(model.ActiveDocumentId),parameter),"Live preview command: "+id);
        var path=Path.Combine(options.ProfileDirectory!,"live-drag.ass");
        var fixture=AssDocument.CreateEmpty();new SubtitleEditor(fixture).SetScriptInfo(new Dictionary<string,string> { ["PlayResX"]="384",["PlayResY"]="288" });
        File.WriteAllText(path,fixture.Serialize()+"Dialogue: 0,0:00:00.00,0:00:05.00,Default,A,0,0,0,,{\\pos(100,140)\\bord2\\future(preserve)}Live 日本 မြန်မာ\n");
        Require(model.OpenSubtitle(path),"Open an independent live-drag fixture.");
        var dummyOptions=new DummyVideoOptions { Width=640,Height=360,FrameCount=150,FramesPerSecond=25,Checkerboard=true };
        await Invoke("video/open/dummy",dummyOptions);
        Require(model.VideoSourcePath==dummyOptions.SourceName,"The independent dummy video must open: "+model.MediaStatus);
        await Invoke("video/tool/position");await Invoke("video/presentation/zoom","100%");
        // Opening media publishes the provider before rendering is guaranteed to
        // complete: a newer preview request can supersede the initial refresh.
        // Seek to the fixture's active frame and let the UI pump publish it.
        await model.SeekPlaybackAsync(0);
        for(var i=0;i<600&&(model.VideoFrame is null||model.DisplayedPreviewRevision!=model.PreviewRevision);i++)await Task.Delay(16);
        var initialFrame=model.VideoFrame??throw new InvalidOperationException("Initial live-drag frame was not published: "+model.MediaStatus);
        Require(model.DisplayedPreviewRevision==model.PreviewRevision,"Initial live-drag frame must match the current preview revision: "+model.MediaStatus);
        Require(initialFrame.PixelSize.Width==dummyOptions.Width&&initialFrame.PixelSize.Height==dummyOptions.Height,"Initial frame must belong to the independent dummy video.");
        if(OperatingSystem.IsWindows())
        {
            using var raw=new DummyVideoProvider(dummyOptions);
            var expected=raw.GetFrame(0).Pixels;
            for(var i=3;i<expected.Length;i+=4)expected[i]=0;
            Require(!CaptureVisiblePixels(initialFrame).SequenceEqual(expected),"The initial composed frame must visibly contain subtitles before testing drag movement: "+model.MediaStatus);
        }
        await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout,DispatcherPriority.Background);
        await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout,DispatcherPriority.Background);
        var overlay=window.FindControl<VisualOverlayControl>("VisualOverlay")??throw new InvalidOperationException("Missing live-drag visual overlay.");
        var image=window.FindControl<VideoPresentationControl>("VideoImage")??throw new InvalidOperationException("Missing live-drag video image.");
        var rect=image.VideoArea;Require(rect.Width>0&&rect.Height>0,"The live-drag video area must be laid out.");
        var area=new VideoPresentation(rect.X,rect.Y,rect.Width,rect.Height);
        Point PointAt(double x)=>new(area.FromScript(new(x,140),384,288).X,area.FromScript(new(x,140),384,288).Y);
        Point Root(Point point)=>overlay.TranslatePoint(point,window)??throw new InvalidOperationException("Embedded visual control must belong to the native window.");
        var pointer=new Pointer(957,PointerType.Mouse,true);
        var original=model.ActiveEditor!.Document.Serialize();var initialRevision=model.PreviewRevision;
        var revisions=new HashSet<long>();var pixels=new HashSet<string>();
        void Frame(object? sender,EventArgs args)
        {
            if(!model.HasGesture||model.DisplayedPreviewRevision<=initialRevision||model.VideoFrame is not {} bitmap)return;
            revisions.Add(model.DisplayedPreviewRevision);
            pixels.Add(Convert.ToHexString(SHA256.HashData(CaptureVisiblePixels(bitmap))));
        }
        model.FrameReady+=Frame;
        try
        {
            Require(overlay.Focus(),"Focus the actual visual tool.");
            overlay.RaiseEvent(new PointerPressedEventArgs(overlay,pointer,window,Root(PointAt(100)),0,new(RawInputModifiers.LeftMouseButton,PointerUpdateKind.LeftButtonPressed),KeyModifiers.None));
            Require(model.HasGesture&&ReferenceEquals(pointer.Captured,overlay),"Position drag owns a real pointer capture and transaction.");
            for(var i=1;i<=120;i++)
            {
                overlay.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent,overlay,pointer,window,Root(PointAt(100+i*.5)),0,new(RawInputModifiers.LeftMouseButton,PointerUpdateKind.Other),KeyModifiers.None));
                await Task.Delay(8);
            }
            Require(model.HasGesture&&revisions.Count>=2,"Continuous pointer motion must display multiple updated subtitle frames before release; revisions="+revisions.Count);
            if(OperatingSystem.IsWindows())Require(pixels.Count>=2,"Actual Mangetsu subtitle pixels must move during the drag, not only after release; revisions="+revisions.Count+", pixel states="+pixels.Count+", status="+model.MediaStatus);
            overlay.RaiseEvent(new PointerReleasedEventArgs(overlay,pointer,window,Root(PointAt(160)),0,new(RawInputModifiers.None,PointerUpdateKind.LeftButtonReleased),KeyModifiers.None,MouseButton.Left));
            Require(!model.HasGesture&&pointer.Captured is null,"Release finishes the one gesture and pointer capture.");
            for(var i=0;i<600&&model.DisplayedPreviewRevision!=model.PreviewRevision;i++)await Task.Delay(16);
            Require(model.DisplayedPreviewRevision==model.PreviewRevision,"After release, the exact final revision must be visible.");
            Require(AssVisualTags.Position(model.SelectedEvent!.Text) is {} position&&Math.Abs(position.X-160)<.01&&Math.Abs(position.Y-140)<.01,"Drag commits the exact final script position.");
            await Invoke(CommandIds.EditUndo);Require(model.ActiveEditor.Document.Serialize()==original,"The entire live drag undoes in one operation, preserving unknown tags and Unicode.");
            Console.WriteLine($"PASS: continuous native position drag displayed {revisions.Count} revisions and {pixels.Count} pixel states before release; final revision and one-step undo verified.");
        }
        finally
        {
            model.FrameReady-=Frame;
            if(model.HasGesture)model.CancelGesture();pointer.Capture(null);
        }
    }
    // Compare actual visible RGB, excluding row padding and alpha-only changes.
    private static byte[] CaptureVisiblePixels(Avalonia.Media.Imaging.WriteableBitmap bitmap)
    {
        using var buffer=bitmap.Lock();
        var rowBytes=checked(buffer.Size.Width*4);
        var pixels=new byte[checked(rowBytes*buffer.Size.Height)];
        for(var y=0;y<buffer.Size.Height;y++)Marshal.Copy(buffer.Address+y*buffer.RowBytes,pixels,y*rowBytes,rowBytes);
        for(var i=3;i<pixels.Length;i+=4)pixels[i]=0;
        return pixels;
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
