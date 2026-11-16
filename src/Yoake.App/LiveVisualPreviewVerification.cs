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
        File.WriteAllText(path,"[Script Info]\nPlayResX: 384\nPlayResY: 288\n[Events]\nFormat: "+string.Join(',',AssDocument.EventFormat)+"\nDialogue: 0,0:00:00.00,0:00:05.00,Default,A,0,0,0,,{\\pos(100,140)\\bord2\\future(preserve)}Live 日本 မြန်မာ\n");
        Require(model.OpenSubtitle(path),"Open an independent live-drag fixture.");
        await Invoke("video/open/dummy",new DummyVideoOptions { Width=640,Height=360,FrameCount=150,FramesPerSecond=25,Checkerboard=true });
        await Invoke("video/tool/position");await Invoke("video/presentation/zoom","100%");
        await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout,DispatcherPriority.Background);
        await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout,DispatcherPriority.Background);
        var overlay=window.FindControl<VisualOverlayControl>("VisualOverlay")!;
        var image=window.FindControl<VideoPresentationControl>("VideoImage")!;
        var rect=image.VideoArea;var area=new VideoPresentation(rect.X,rect.Y,rect.Width,rect.Height);
        Point PointAt(double x)=>new(area.FromScript(new(x,140),384,288).X,area.FromScript(new(x,140),384,288).Y);
        Point Root(Point point)=>overlay.TranslatePoint(point,window)??throw new InvalidOperationException("Embedded visual control must belong to the native window.");
        var pointer=new Pointer(957,PointerType.Mouse,true);
        var original=model.ActiveEditor!.Document.Serialize();var initialRevision=model.PreviewRevision;
        var revisions=new HashSet<long>();var pixels=new HashSet<string>();
        void Frame(object? sender,EventArgs args)
        {
            if(!model.HasGesture||model.DisplayedPreviewRevision<=initialRevision||model.VideoFrame is not {} bitmap)return;
            revisions.Add(model.DisplayedPreviewRevision);
            using var buffer=bitmap.Lock();var bytes=new byte[buffer.RowBytes*buffer.Size.Height];
            Marshal.Copy(buffer.Address,bytes,0,bytes.Length);pixels.Add(Convert.ToHexString(SHA256.HashData(bytes)));
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
            if(OperatingSystem.IsWindows())Require(pixels.Count>=2,"Actual Mangetsu subtitle pixels must move during the drag, not only after release.");
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
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
