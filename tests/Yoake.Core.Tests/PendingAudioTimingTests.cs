using Xunit;
using Yoake.Core.Audio;
using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class PendingAudioTimingTests
{
    private static SubtitleEditor Editor()=>new(AssDocument.Parse("[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Extra, Text\n"+
        "Dialogue: 0,0:00:00.300,0:00:01.400,Default,actor,0001,0002,0003,effect,opaque,{\\future(x)\\distort(1,2,3,4,5,6,7,8)}日本語 é မြန်မာ 👩‍👩‍👧\n"));
    [Fact] public void MarkersAreTransientAndCommitIsOneLosslessUndoOperation()
    {
        var e=Editor();var timing=new PendingAudioTiming(e);var line=e.Document.Events[0];var before=e.Document.Serialize();
        timing.Stage(line,new(200,1750));Assert.Equal(300,line.StartMilliseconds);Assert.Equal(1400,line.EndMilliseconds);
        Assert.Equal(before,e.Document.Serialize());Assert.False(e.Undo.CanUndo);Assert.Equal(new AudioClipInterval(200,1750),timing.Get(line));
        Assert.Equal(1,timing.Commit(timing.CaptureCommit()));Assert.Equal(0,timing.Count);
        Assert.Equal(200,line.StartMilliseconds);Assert.Equal(1750,line.EndMilliseconds);Assert.Equal("opaque",line.Get("Extra"));Assert.Equal("0001",line.MarginL);
        var after=e.Document.Serialize();Assert.True(e.Undo.Undo());Assert.Equal(before,e.Document.Serialize());Assert.False(e.Undo.CanUndo);
        Assert.True(e.Undo.Redo());Assert.Equal(after,e.Document.Serialize());
    }
    [Fact] public void UnchangedEndpointsRetainTheirExactSourceAndInvalidRangesCannotPublish()
    {
        var e=Editor();var timing=new PendingAudioTiming(e);var line=e.Document.Events[0];
        Assert.Throws<ArgumentException>(()=>timing.Stage(line,new(-10,20)));Assert.Throws<ArgumentException>(()=>timing.Stage(line,new(100,99)));
        Assert.Throws<ArgumentException>(()=>timing.Stage(line,new(0,long.MaxValue)));Assert.False(e.Undo.CanUndo);
        timing.Stage(line,new(300,1410));timing.Commit(timing.CaptureCommit());Assert.Equal("0:00:00.300",line.Start);
        timing.Stage(line,new(300,1410));Assert.Equal(0,timing.Count);var history=e.Undo.NextUndoName;timing.Commit(timing.CaptureCommit());Assert.Equal(history,e.Undo.NextUndoName);
    }
    [Fact] public void ChangedMarkersRevisionForeignOwnerAndCancellationRejectStalePublication()
    {
        var e=Editor();var line=e.Document.Events[0];var timing=new PendingAudioTiming(e);timing.Stage(line,new(300,1500));
        var old=timing.CaptureCommit();timing.Stage(line,new(300,1600));Assert.Throws<InvalidOperationException>(()=>timing.Commit(old));Assert.False(e.Undo.CanUndo);
        old=timing.CaptureCommit();e.SetField(line,"Name","new actor","Actor");Assert.Throws<InvalidOperationException>(()=>timing.Commit(old));Assert.Equal(1400,line.EndMilliseconds);
        Assert.Equal(new AudioClipInterval(300,1600),timing.Get(line));
        var other=new PendingAudioTiming(Editor());Assert.Throws<InvalidOperationException>(()=>other.Commit(timing.CaptureCommit()));
        using var cancel=new CancellationTokenSource();cancel.Cancel();Assert.Throws<OperationCanceledException>(()=>timing.CaptureCommit(cancel.Token));
        var current=timing.CaptureCommit();Assert.Throws<OperationCanceledException>(()=>timing.Commit(current,cancel.Token));Assert.Equal(1400,line.EndMilliseconds);
        Assert.Throws<InvalidOperationException>(()=>timing.Stage(Editor().Document.Events[0],new(0,10)));
    }
    [Fact] public void GestureCheckpointRestoresPriorPendingMarkersButNeverObsoleteSource()
    {
        var e=Editor();var line=e.Document.Events[0];var timing=new PendingAudioTiming(e);var clean=timing.Capture(line);
        timing.Stage(line,new(300,1500));var previous=timing.Capture(line);timing.Stage(line,new(100,1900));timing.Restore(previous);Assert.Equal(new AudioClipInterval(300,1500),timing.Get(line));
        timing.Restore(clean);Assert.Null(timing.Get(line));Assert.False(e.Undo.CanUndo);
        timing.Stage(line,new(300,1500));previous=timing.Capture(line);e.SetField(line,"End","0:00:02.00","External timing");timing.Restore(previous);Assert.Null(timing.Get(line));
        timing.Stage(line,new(300,2100));e.Delete([line]);Assert.Equal(0,timing.Count);
    }
    [Theory]
    [InlineData(0,0,-10,0,0,0)]
    [InlineData(10,10,10,0,10,10)]
    [InlineData(10,10,0,-10,10,10)]
    [InlineData(300,1400,-100,350,200,1750)]
    [InlineData(300,1400,10,0,310,1400)]
    [InlineData(300,1400,0,-10,300,1390)]
    public void AdjustmentsClampAtZeroAndNeverInvert(long start,long end,long deltaStart,long deltaEnd,long expectedStart,long expectedEnd)
        =>Assert.Equal(new AudioClipInterval(expectedStart,expectedEnd),PendingAudioTiming.Adjust(new(start,end),deltaStart,deltaEnd));
    [Fact] public void TimingSuccessorUsesStoredEndCurrentStyleAndThreeSecondsWithoutChangingGenericInsertion()
    {
        var e=Editor();var line=e.Document.Events[0];var next=e.InsertTimingSuccessor(line,3000);
        Assert.Equal(1400,next.StartMilliseconds);Assert.Equal(4400,next.EndMilliseconds);Assert.Equal(line.Style,next.Style);Assert.Empty(next.Text);Assert.NotEqual(line.Actor,next.Actor);
        var generic=e.Insert(line,true,1400);Assert.Equal(3400,generic.EndMilliseconds);Assert.Equal(line.Actor,generic.Actor);Assert.Equal("opaque",generic.Get("Extra"));
    }
}
