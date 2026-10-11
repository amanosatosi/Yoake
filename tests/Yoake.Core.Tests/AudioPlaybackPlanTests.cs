using Yoake.Core.Audio;
using Xunit;

namespace Yoake.Core.Tests;

public sealed class AudioPlaybackPlanTests
{
    [Theory]
    [InlineData(AudioPlaybackKind.Current,1000,1800,false)]
    [InlineData(AudioPlaybackKind.Line,1000,1800,false)]
    [InlineData(AudioPlaybackKind.Selection,1000,1800,true)]
    [InlineData(AudioPlaybackKind.Before,500,1000,false)]
    [InlineData(AudioPlaybackKind.After,1800,2300,false)]
    [InlineData(AudioPlaybackKind.Begin,1000,1500,false)]
    [InlineData(AudioPlaybackKind.End,1300,1800,true)]
    [InlineData(AudioPlaybackKind.ToEnd,1000,3000,false)]
    [InlineData(AudioPlaybackKind.Cursor,750,3000,false)]
    public void ExactPreviewRangesAndFixedVersusFollowingEndpoints(AudioPlaybackKind kind,long start,long end,bool follows)
    {
        var plan=AudioPlaybackPlan.Create(kind,1000,3000,new(1000,1800),.75);
        Assert.Equal(start,plan.StartFrame);Assert.Equal(end,plan.EndFrame);Assert.Equal(follows,plan.FollowsSelectionEnd);
        var extended=plan.WithSelectionEnd(2500);Assert.Equal(start,extended.StartFrame);Assert.Equal(follows?2500:end,extended.EndFrame);
        Assert.Equal(end,plan.EndFrame);Assert.Equal(follows?start:end,plan.WithSelectionEnd(0).EndFrame);
        if(!follows)Assert.Same(plan,extended);
    }
    [Fact] public void ShortLinesFileEdgesAndCeilingSamplesAreBoundedWithoutVideoTimecodes()
    {
        var interval=new AudioClipInterval(1,100);
        var begin=AudioPlaybackPlan.Create(AudioPlaybackKind.Begin,11025,1000,interval);
        Assert.Equal(12,begin.StartFrame);Assert.Equal(1000,begin.EndFrame);
        var end=AudioPlaybackPlan.Create(AudioPlaybackKind.End,11025,2000,interval);
        Assert.Equal(12,end.StartFrame);Assert.Equal(1103,end.EndFrame);
        Assert.Equal(0,AudioPlaybackPlan.Create(AudioPlaybackKind.Before,1000,2000,new(100,300)).StartFrame);
        Assert.Equal(2000,AudioPlaybackPlan.Create(AudioPlaybackKind.After,1000,2000,new(1800,1950)).EndFrame);
        Assert.True(AudioPlaybackPlan.Create(AudioPlaybackKind.Selection,11025,1000,new(2000,long.MaxValue)).IsEmpty);
        Assert.True(AudioPlaybackPlan.Create(AudioPlaybackKind.Line,48000,96000,new(0,0)).IsEmpty);
        Assert.Equal(1000,AudioPlaybackPlan.Create(AudioPlaybackKind.Cursor,1000,1000,new(0,0),double.MaxValue).StartFrame);
        Assert.Equal(0,AudioPlaybackPlan.FrameAtSeconds(-10,48000,96000));
        Assert.Equal(12,AudioPlaybackPlan.CeilingFrame(1,11025,1000));
        Assert.Equal(long.MaxValue,AudioPlaybackPlan.CeilingFrame(long.MaxValue,int.MaxValue,long.MaxValue));
    }
    [Fact] public void DecodeChunksCannotCrossTheEndpointAndInvalidPlansNeverPublish()
    {
        Assert.Equal(17,AudioPlaybackPlan.FramesToQueue(1000,960,96000,1017));
        Assert.Equal(0,AudioPlaybackPlan.FramesToQueue(1017,960,96000,1017));
        Assert.Equal(3,AudioPlaybackPlan.FramesToQueue(97,960,100,long.MaxValue));
        Assert.Throws<ArgumentException>(()=>AudioPlaybackPlan.Create((AudioPlaybackKind)99,48000,96000,new(0,100)));
        Assert.Throws<ArgumentException>(()=>AudioPlaybackPlan.Create(AudioPlaybackKind.Selection,0,96000,new(0,100)));
        Assert.Throws<ArgumentException>(()=>AudioPlaybackPlan.Create(AudioPlaybackKind.Selection,48000,96000,new(100,0)));
        Assert.Throws<ArgumentException>(()=>AudioPlaybackPlan.Create(AudioPlaybackKind.Cursor,48000,96000,new(0,100),double.NaN));
        Assert.Throws<ArgumentException>(()=>AudioPlaybackPlan.Create(AudioPlaybackKind.Cursor,48000,96000,new(0,100),double.PositiveInfinity));
        Assert.Throws<ArgumentException>(()=>AudioPlaybackPlan.FramesToQueue(-1,960,96000,2000));
        var plan=AudioPlaybackPlan.Create(AudioPlaybackKind.Selection,48000,96000,new(300,500));
        Assert.Throws<ArgumentException>(()=>plan.WithSelectionEnd(-1));Assert.Equal(24000,plan.EndFrame);
    }
}
