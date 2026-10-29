using Yoake.Core.Media;

namespace Yoake.Core.Tests;

public sealed class VideoNavigationTests
{
    [Theory]
    [InlineData(1,1,5)][InlineData(5,1,9)][InlineData(9,1,19)]
    [InlineData(9,-1,5)][InlineData(1,-1,1)][InlineData(0,-1,1)]
    public void KeyframeCommandsUseReleaseFirstAndEofFallback(int frame,int direction,int expected)
    {Assert.Equal(expected,FrameNavigation.StepKeyframe(new[]{1,5,9},frame,direction,20));}
    [Fact] public void EmptyKeyframesReachBeginningOrEndAndExternalKeysClampAtTheSeekBoundary()
    {
        Assert.Equal(0,FrameNavigation.StepKeyframe([],7,-1,20));Assert.Equal(19,FrameNavigation.StepKeyframe([],7,1,20));
        Assert.Equal(90,FrameNavigation.StepKeyframe(new[]{90},7,1,20));
    }
    private static FrameTiming Map()=>new(new[]{0d,.033,.080,.125,.200,.340,.500,.700},24);
    [Theory]
    [InlineData(0,true,2)][InlineData(2,true,5)][InlineData(5,true,null)]
    [InlineData(7,false,5)][InlineData(5,false,2)][InlineData(2,false,null)]
    public void BoundariesVisitCurrentLineInExactStartEndOrderBeforeChangingLines(int frame,bool next,int? expected)
    {Assert.Equal(expected,FrameNavigation.Boundary(Map(),frame,70,500,next));}
    [Fact] public void LargeNavigationIsFrameBasedConfigurableAndClamped()
    {
        Assert.Equal(11,FrameNavigation.LargeStep(1,1,10,20));Assert.Equal(1,FrameNavigation.LargeStep(11,-1,10,20));
        Assert.Equal(19,FrameNavigation.LargeStep(11,1,10,20));Assert.Equal(0,FrameNavigation.LargeStep(1,-1,10,20));
        Assert.Equal(3,FrameNavigation.LargeStep(0,1,3,8));Assert.Throws<ArgumentException>(()=>FrameNavigation.LargeStep(0,1,0,8));
    }
    [Fact] public void VideoLineRangeUsesVfrFrameStartEndAndClipsToActualMedia()
    {
        double[] times=[0,.033,.080,.125,.200,.340,.500,.700];
        Assert.Equal((.080,.500),FrameNavigation.LineRange(Map(),times,70,500,.9));
        Assert.Equal((.700,.9),FrameNavigation.LineRange(Map(),times,600,1200,.9));
        Assert.Null(FrameNavigation.LineRange(Map(),times,1000,1200,.9));
        Assert.Null(FrameNavigation.LineRange(Map(),times,500,500,.9));
    }
}
