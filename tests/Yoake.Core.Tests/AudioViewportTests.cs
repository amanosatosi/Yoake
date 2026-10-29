using Yoake.Core.Audio;

namespace Yoake.Core.Tests;

public sealed class AudioViewportTests
{
    [Fact] public void ScrollingUsesReleasePixelDistanceAndCurrentZoomWithoutChangingTheRange()
    {
        Assert.Equal(12,AudioViewportMath.ScrollPixels(10,10,640,128,100));
        Assert.Equal(8,AudioViewportMath.ScrollPixels(10,10,640,-128,100));
        Assert.Equal(14,AudioViewportMath.ScrollPixels(10,20,640,128,100));
        Assert.Equal(90,AudioViewportMath.ScrollPixels(89,10,640,128,100));Assert.Equal(0,AudioViewportMath.ScrollPixels(1,10,640,-128,100));
        Assert.Equal(0,AudioViewportMath.ScrollPixels(0,10,640,128,2));
    }
    [Theory]
    [InlineData(10,10,12,18,10)] // already inside the 5% gutters
    [InlineData(10,10,30,34,27)] // fitting range is centered
    [InlineData(10,10,0,50,10)]  // retain a middle viewport of a long range
    [InlineData(10,10,0,18,8.5)] // bring the right edge to the inner right edge
    [InlineData(10,10,15,40,14.5)] // bring the left edge to the inner left edge
    [InlineData(10,10,1,2,0)]
    [InlineData(10,10,99,100,90)]
    public void GoToRangeMatchesReleaseVisibilityAndGutterSemantics(double start,double span,double begin,double end,double expected)
        =>Assert.Equal(expected,AudioViewportMath.RangeInView(start,span,begin,end,100));
    [Fact] public void EndpointCenteringClampsAndInvalidGeometryDoesNotPublishNaN()
    {
        Assert.Equal(25,AudioViewportMath.Center(30,10,100));Assert.Equal(0,AudioViewportMath.Center(2,10,100));Assert.Equal(90,AudioViewportMath.Center(99,10,100));
        Assert.Throws<ArgumentException>(()=>AudioViewportMath.ScrollPixels(0,10,0,128,100));Assert.Throws<ArgumentException>(()=>AudioViewportMath.ScrollPixels(0,10,double.NaN,128,100));
        Assert.Throws<ArgumentException>(()=>AudioViewportMath.RangeInView(0,10,5,3,100));Assert.Throws<ArgumentException>(()=>AudioViewportMath.Center(double.NaN,10,100));
        Assert.Throws<ArgumentException>(()=>AudioViewportMath.Center(0,0,100));
    }
}
