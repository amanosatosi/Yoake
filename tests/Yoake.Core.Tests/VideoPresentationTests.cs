using Xunit;
using Yoake.Core.Media;
using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class VideoPresentationTests
{
    [Theory]
    [InlineData(.5, 1)] [InlineData(1, 1)] [InlineData(2, 1)]
    [InlineData(.5, 1.25)] [InlineData(1, 1.5)] [InlineData(2, 2)]
    public void PercentagesMeanSourcePhysicalHeightNotFit(double zoom, double dpi)
    {
        var state = new VideoViewState(); state.SetWindowZoom(zoom);
        var image = state.Layout(300, 200, dpi, 480, 16d/9);
        Assert.Equal(480 * zoom / dpi, image.Height, 8);
        Assert.Equal(image.Height * 16 / 9, image.Width, 8);
        var larger = state.Layout(900, 700, dpi, 480, 16d/9);
        Assert.Equal(image.Width, larger.Width); Assert.Equal(image.Height, larger.Height);
        var point = new AssPoint(203, 147);
        var screen = image.FromScript(point, 640, 480);
        var roundtrip = image.ToScript(screen.X, screen.Y, 640, 480);
        Assert.Equal(point.X, roundtrip.X, 8); Assert.Equal(point.Y, roundtrip.Y, 8);
    }
    [Fact] public void PanWorksWhenFittedClampsAndResetPreservesWindowZoom()
    {
        var state = new VideoViewState(); state.SetWindowZoom(.5); var initial = state.Layout(800, 600, 2, 480, 4d/3);
        state.Pan(25, -10); var moved = state.Layout(800, 600, 2, 480, 4d/3);
        Assert.Equal(initial.X+25,moved.X);Assert.Equal(initial.Y-10,moved.Y);
        state.Pan(100000, -100000);Assert.Equal(.5*initial.Width*2+.4*1600,state.PanX,8);Assert.Equal(-(.5*initial.Height*2+.4*1200),state.PanY,8);
        state.ResetContent();Assert.Equal(.5,state.WindowZoom);Assert.Equal(1,state.ContentZoom);Assert.Equal(0,state.PanX);Assert.Equal(0,state.PanY);
        Assert.Equal(initial,state.Layout(800,600,2,480,4d/3));
    }
    [Fact] public void PointerAnchoredContentZoomResizesWithoutDrifting()
    {
        var state = new VideoViewState();state.SetWindowZoom(1);var before=state.Layout(800,600,1,480,4d/3);
        var anchor=before.ToScript(237,174,640,480);state.ZoomContent(2,237,174);
        var after=state.Layout(800,600,1,480,4d/3);var mapped=after.FromScript(anchor,640,480);
        Assert.Equal(237,mapped.X,8);Assert.Equal(174,mapped.Y,8);
        state.SetWindowZoom(1.125);Assert.Equal(1.125,state.WindowZoom);Assert.Equal(2,state.ContentZoom);
        state.ResetContent();Assert.Equal(1.125,state.WindowZoom);
    }
    [Fact] public void DetachedResizePreservesContentHeightAndPanUntilReset()
    {
        var state=new VideoViewState{Detached=true};state.Layout(800,600,1,480,4d/3);state.ZoomContent(2,400,300);state.Pan(15,20);
        var before=state.Layout(800,600,1,480,4d/3);var after=state.Layout(400,300,1,480,4d/3);
        Assert.Equal(before.Height,after.Height);Assert.Equal(15,state.PanX);Assert.Equal(20,state.PanY);Assert.Equal(.625,state.EffectiveWindowZoom);
        state.ResetContent();Assert.Equal(300,state.Layout(400,300,1,480,4d/3).Height);
    }
    [Theory] [InlineData(-1)] [InlineData(0)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void InvalidZoomIsAtomic(double value)
    {var state=new VideoViewState();state.SetWindowZoom(1);Assert.Throws<ArgumentException>(()=>state.SetWindowZoom(value));Assert.Equal(1,state.WindowZoom);}
    [Fact] public void WindowZoomIsBoundedAndRescalesPan()
    {var state=new VideoViewState();state.SetWindowZoom(1);state.Layout(800,600,1,480,4d/3);state.Pan(30,20);state.SetWindowZoom(2);Assert.Equal(60,state.PanX);Assert.Equal(40,state.PanY);state.SetWindowZoom(.01);Assert.Equal(.125,state.WindowZoom);state.SetWindowZoom(50);Assert.Equal(10,state.WindowZoom);}
    [Fact] public void MonitorDpiChangePreservesLogicalSizeAndPanAsReferenceDoes()
    {
        var state=new VideoViewState();state.SetWindowZoom(1);state.Layout(800,600,1,480,4d/3);state.Pan(20,15);
        var before=state.Layout(800,600,1,480,4d/3);var after=state.Layout(800,600,1.5,480,4d/3);
        Assert.Equal(before,after);Assert.Equal(1.5,state.WindowZoom);Assert.Equal(30,state.PanX);Assert.Equal(22.5,state.PanY);
    }
}
