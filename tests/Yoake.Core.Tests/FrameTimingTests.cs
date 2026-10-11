using Yoake.Core.Media;

namespace Yoake.Core.Tests;

public sealed class FrameTimingTests
{
    [Fact] public void VariableFrameBoundariesUseMidpointsAndReleaseStartEndRules()
    {
        var timing=new FrameTiming([0,.04,.1,.18],25);
        Assert.Equal(70,timing.MillisecondsAtFrame(2,FrameTimeKind.Start));Assert.Equal(140,timing.MillisecondsAtFrame(2,FrameTimeKind.End));
        Assert.Equal(2,timing.FrameAtMilliseconds(70,FrameTimeKind.Start));Assert.Equal(1,timing.FrameAtMilliseconds(70,FrameTimeKind.End));
        Assert.Equal(2,timing.FrameAtMilliseconds(140,FrameTimeKind.End));Assert.Equal(3,timing.FrameAtMilliseconds(140,FrameTimeKind.Start));
        Assert.Equal(240,timing.MillisecondsAtFrame(4));Assert.Equal(-60,timing.MillisecondsAtFrame(-1));
        for(var frame=0;frame<timing.Count;frame++)
        {
            Assert.Equal(frame,timing.FrameAtMilliseconds(timing.MillisecondsAtFrame(frame,FrameTimeKind.Start),FrameTimeKind.Start));
            Assert.Equal(frame,timing.FrameAtMilliseconds(timing.MillisecondsAtFrame(frame,FrameTimeKind.End),FrameTimeKind.End));
        }
    }
    [Fact] public void OneFrameExtrapolationAndNonzeroProviderOriginRemainOnTheSameTimeline()
    {
        var single=new FrameTiming([0],25);Assert.Equal(-20,single.MillisecondsAtFrame(0,FrameTimeKind.Start));Assert.Equal(20,single.MillisecondsAtFrame(0,FrameTimeKind.End));
        Assert.Equal(0,single.SubtitleBoundary(0,FrameTimeKind.Start));Assert.Equal(1,single.FrameAtMilliseconds(20,FrameTimeKind.Start));
        var offset=new FrameTiming([10,10.04,10.1],20);Assert.Equal(10070,offset.MillisecondsAtFrame(2,FrameTimeKind.Start));Assert.Equal(2,offset.FrameAtMilliseconds(10070,FrameTimeKind.Start));
    }
    [Fact] public void RoundedMillisecondBoundariesRoundUpToAssCentisecondsWithoutChangingTheGeneralFormatter()
    {
        var timing=new FrameTiming([0,.05,.11],25);Assert.Equal(25,timing.MillisecondsAtFrame(1,FrameTimeKind.Start));
        Assert.Equal(30,timing.SubtitleBoundary(1,FrameTimeKind.Start));Assert.Equal("0:00:00.02",Yoake.Core.Subtitles.AssTime.Format(25));
    }
    [Fact] public void InvalidAndCanceledTimelinesCannotProduceUsableBoundaryState()
    {
        Assert.Throws<ArgumentException>(()=>new FrameTiming([],25));Assert.Throws<ArgumentException>(()=>new FrameTiming([0,double.NaN],25));
        Assert.Throws<ArgumentException>(()=>new FrameTiming([0,.1,.05],25));Assert.Throws<ArgumentException>(()=>new FrameTiming([1,1],25));
        Assert.Throws<ArgumentException>(()=>new FrameTiming([0],double.NaN));Assert.Throws<ArgumentException>(()=>new FrameTiming([0],0));
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();Assert.ThrowsAny<OperationCanceledException>(()=>new FrameTiming([0,.1],25,cancellation.Token));
    }
}
