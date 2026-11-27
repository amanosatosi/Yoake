using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Yoake.Core.Commands;
using Yoake.Core.Media;
using Yoake.UI;
using Yoake.UI.ViewModels;

namespace Yoake.App;

// Shipping controls in a real native window. Shared by the Windows provider
// acceptance and the provider-independent GUI workflow used on Linux.
internal static class EmbeddedVideoWorkspaceVerification
{
    private sealed record Geometry(double VideoWidth,double UpperHeight,Point VerticalDivider,Point HorizontalDivider,Rect Editor,Rect Grid,Rect Canvas);
    public static async Task VerifyAsync(MainWindow window,MainWindowViewModel model,string? audioPath=null)
    {
        async Task Invoke(string id,object? parameter=null) => Require(await model.Registry.InvokeAsync(id,new(model.ActiveDocumentId),parameter),"Workspace command: "+id);
        async Task Layout()
        {
            await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout,DispatcherPriority.Background);
            await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout,DispatcherPriority.Background);
        }
        async Task NativeSize(Window target,double width,double height)
        {
            var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            bool Matches()=>Math.Abs(target.Bounds.Width-width)<1&&Math.Abs(target.Bounds.Height-height)<1;
            void Changed(object? sender,SizeChangedEventArgs args){if(Matches())ready.TrySetResult();}
            target.SizeChanged+=Changed;
            try
            {
                target.Width=width;target.Height=height;if(Matches())ready.TrySetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));await Layout();
            }
            finally{target.SizeChanged-=Changed;}
        }
        T Get<T>(string name) where T:Control => window.FindControl<T>(name)??throw new InvalidOperationException("Missing layout control: "+name);
        Rect Bounds(Control control) => new(control.TranslatePoint(default,window)??throw new InvalidOperationException("Control is outside the main window: "+control.Name),control.Bounds.Size);
        Geometry Read()
        {
            var upper=Get<Grid>("UpperWorkspace");
            return new(upper.ColumnDefinitions[0].ActualWidth,upper.Bounds.Height,
                Bounds(Get<GridSplitter>("VideoSplitter")).Position,Bounds(Get<GridSplitter>("WorkspaceSplitter")).Position,
                Bounds(Get<Grid>("TemporalTextColumn")),Bounds(Get<Border>("SubtitleGridRegion")),Bounds(Get<Border>("VideoCanvasHost")));
        }
        void Usable()
        {
            var upper=Get<Grid>("UpperWorkspace");var root=(Grid)upper.Parent!;var geometry=Read();
            var right=Get<Grid>("TemporalTextColumn");
            Require(right.Bounds.Width>=upper.ColumnDefinitions[2].MinWidth-.5,"Video zoom must retain the editor's minimum width.");
            Require(right.RowDefinitions[2].ActualHeight>=right.RowDefinitions[2].MinHeight-.5,"Video zoom must retain editor height.");
            var textInput=Get<TextBox>("SubtitleText");
            Require(textInput.Bounds.Height>=63&&Bounds(textInput).Bottom<=geometry.Editor.Bottom+1,"The real subtitle text input remains usable and inside its panel.");
            Require(geometry.Grid.Height>=root.RowDefinitions[4].MinHeight-13&&Get<ListBox>("SubtitleRows").Bounds.Height>=139,"Video zoom must retain actual visible subtitle grid rows.");
            Require(geometry.Grid.Bottom<=root.Bounds.Height+1,"The subtitle grid must stay inside the main client area.");
            Require(geometry.Canvas.Right<=Bounds(Get<Border>("VideoDock")).Right+1,"The video canvas stays inside its allocated column.");
            if(model.HasAudio)Require(right.RowDefinitions[0].ActualHeight>=right.RowDefinitions[0].MinHeight-.5&&Get<Control>("AudioDisplay").Bounds.Height>30,"Audio waveform remains usable.");
        }
        async Task Zoom(string percentage)
        { await Invoke("video/presentation/zoom",percentage);await Layout();Usable(); }
        static void Key(GridSplitter splitter,Avalonia.Input.Key key)
        {
            Require(splitter.Focus()&&splitter.IsKeyboardFocusWithin,"The actual splitter must own keyboard focus.");
            splitter.RaiseEvent(new KeyEventArgs { RoutedEvent=InputElement.KeyDownEvent,Source=splitter,Key=key });
            splitter.RaiseEvent(new KeyEventArgs { RoutedEvent=InputElement.KeyUpEvent,Source=splitter,Key=key });
        }
        if(model.IsVideoDetached)await Invoke("video/detach");
        if(audioPath is not null)Require(await model.OpenAudioAsync(audioPath),"Open independent audio for workspace verification.");
        window.WindowState=WindowState.Normal;await NativeSize(window,1440,900);
        var dpi=window.RenderScaling;
        await Invoke("video/open/dummy",new DummyVideoOptions { Width=(int)Math.Round(1600*dpi),Height=(int)Math.Round(1000*dpi),FrameCount=3,FramesPerSecond=25 });
        await Invoke(model.HasAudio?CommandIds.ViewAudioVideo:CommandIds.ViewVideo);await Layout();
        var source=model.ActiveEditor!.Document.Serialize();var undo=model.ActiveEditor.Undo.NextUndoName;
        var draft=model.Draft;var audio=model.AudioSourcePath;var samples=model.WaveformSamples;
        await Zoom("37.5%");var small=Read();
        await Invoke("video/tool/crosshair");await Layout();
        var stableCanvas=Bounds(Get<Border>("VideoCanvasHost"));
        var stablePlayback=Bounds(Get<Grid>("VideoPlaybackControls"));
        var stablePrimary=Bounds(Get<WrapPanel>("VisualPrimaryTools"));
        var stableToolbar=Bounds(Get<Border>("VisualToolsBar"));
        foreach(var tool in new[]{"position","rotatez","scale","shift","crosshair"})
        {
            await Invoke("video/tool/"+tool);await Layout();
            Require(Bounds(Get<Border>("VideoCanvasHost"))==stableCanvas&&Bounds(Get<Grid>("VideoPlaybackControls"))==stablePlayback&&
                Bounds(Get<WrapPanel>("VisualPrimaryTools"))==stablePrimary&&Bounds(Get<Border>("VisualToolsBar"))==stableToolbar,
                "Switching a one-row visual tool must not shift the canvas, playback or primary toolbar: "+tool);
            var context=Get<WrapPanel>("VisualContextTools");
            if(context.Bounds.Width>0)Require(Math.Abs(Bounds(context).Right-Bounds(Get<Control>("VisualToolGroups")).Right)<=1,
                "Contextual tools must remain right-aligned and visually separated from the primary tools: "+tool);
        }
        small=Read();
        await Zoom("50%");var large=Read();
        Require(large.VideoWidth>small.VideoWidth+100&&large.VerticalDivider.X>small.VerticalDivider.X+100,"37.5% to 50% must move the vertical video/editor splitter and widen the workspace column.");
        Require(large.UpperHeight>small.UpperHeight+70&&large.HorizontalDivider.Y>small.HorizontalDivider.Y+70&&large.Grid.Height<small.Grid.Height-70,"37.5% to 50% must move the horizontal workspace/grid splitter and reallocate real panel height.");
        await Zoom("37.5%");var restored=Read();
        Require(Math.Abs(restored.VideoWidth-small.VideoWidth)<2&&Math.Abs(restored.UpperHeight-small.UpperHeight)<2&&
            Math.Abs(restored.VerticalDivider.X-small.VerticalDivider.X)<2&&Math.Abs(restored.HorizontalDivider.Y-small.HorizontalDivider.Y)<2&&Math.Abs(restored.Grid.Height-small.Grid.Height)<2,
            "50% to 37.5% must restore both splitter positions and return space to editor/grid.");
        Console.WriteLine(FormattableString.Invariant($"Embedded zoom geometry (37.5/50/37.5): video columns {small.VideoWidth:F2}/{large.VideoWidth:F2}/{restored.VideoWidth:F2}; upper heights {small.UpperHeight:F2}/{large.UpperHeight:F2}/{restored.UpperHeight:F2}; vertical splitter X {small.VerticalDivider.X:F2}/{large.VerticalDivider.X:F2}/{restored.VerticalDivider.X:F2}; horizontal splitter Y {small.HorizontalDivider.Y:F2}/{large.HorizontalDivider.Y:F2}/{restored.HorizontalDivider.Y:F2}; grid heights {small.Grid.Height:F2}/{large.Grid.Height:F2}/{restored.Grid.Height:F2}."));
        Key(Get<GridSplitter>("VideoSplitter"),Avalonia.Input.Key.Right);Key(Get<GridSplitter>("WorkspaceSplitter"),Avalonia.Input.Key.Down);await Layout();var manual=Read();
        Require(Math.Abs(manual.VideoWidth-restored.VideoWidth)>1&&Math.Abs(manual.UpperHeight-restored.UpperHeight)>1,"Manual input must actually move both dividers.");
        await Invoke("video/frame/next");await Layout();var afterEdit=Read();
        Require(Math.Abs(afterEdit.VideoWidth-manual.VideoWidth)<1&&Math.Abs(afterEdit.UpperHeight-manual.UpperHeight)<1,"Ordinary frame/edit notifications must preserve manual splitter adjustments.");
        await Zoom("50%");var explicitZoom=Read();
        Require(Math.Abs(explicitZoom.VideoWidth-large.VideoWidth)<2&&Math.Abs(explicitZoom.UpperHeight-large.UpperHeight)<2,"An explicit zoom must reclaim both splitters after manual adjustments.");
        await Zoom("300%");
        Require(model.VideoWindowZoom==3&&Get<Border>("VideoCanvasHost").Bounds.Height<model.CurrentVideoDetails!.Height*3/window.RenderScaling,"An oversized zoom clips the viewport while retaining the requested scale and other panels.");
        await NativeSize(window,1040,800);Usable();
        await Zoom("37.5%");
        Require(model.ActiveEditor.Document.Serialize()==source&&model.ActiveEditor.Undo.NextUndoName==undo&&ReferenceEquals(model.Draft,draft)&&model.AudioSourcePath==audio&&ReferenceEquals(model.WaveformSamples,samples),"Workspace resizing must preserve subtitle source/draft/undo and independent audio.");
        await NativeSize(window,1440,900);
        await Invoke("video/detach");await Layout();
        var mainUpper=Get<Grid>("UpperWorkspace").Bounds;var mainDivider=Bounds(Get<GridSplitter>("WorkspaceSplitter"));
        var detached=window.DetachedVideoWindow??throw new InvalidOperationException("A real detached video window is required.");
        var oldDetached=detached.Bounds.Size;
        await Invoke("video/presentation/zoom","75%");
        await NativeSize(detached,detached.Width,detached.Height);
        Require(Get<Grid>("UpperWorkspace").Bounds==mainUpper&&Bounds(Get<GridSplitter>("WorkspaceSplitter"))==mainDivider&&detached.Bounds.Size!=oldDetached,
            $"Detached zoom resizes its native host while leaving the main workspace divider unchanged: main {mainUpper} -> {Get<Grid>("UpperWorkspace").Bounds}; divider {mainDivider} -> {Bounds(Get<GridSplitter>("WorkspaceSplitter"))}; host {oldDetached} -> {detached.Bounds.Size}.");
        await Invoke("video/detach");await Layout();
        await Invoke("video/presentation/zoom","50%");await Invoke("video/presentation/zoom","37.5%");await Layout();Usable();
        var latest=Read();Require(Math.Abs(latest.VideoWidth-small.VideoWidth)<2&&Math.Abs(latest.UpperHeight-small.UpperHeight)<2,
            "Rapid explicit zoom changes publish only the latest workspace size after reattachment.");
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
