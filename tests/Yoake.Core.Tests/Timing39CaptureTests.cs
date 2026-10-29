using Xunit;
using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class Timing39CaptureTests
{
    [Fact] public void CountdownBlocksInputAndDiscardCannotRestart()
    {
        var session=new Timing39Capture(100000,110000);
        Assert.False(session.Key(Timing39Key.F,true,100100));Assert.False(session.Begin(100000));
        Assert.False(session.TickCountdown());Assert.False(session.TickCountdown());Assert.True(session.TickCountdown());
        Assert.False(session.TickCountdown());session.Discard();Assert.False(session.Begin(100000));
        Assert.Equal(Timing39State.Discarded,session.State);
    }
    [Fact] public void TwoLanesKeepAbsoluteTimesHandleTakeoverAndIgnoreRepeat()
    {
        var s=new Timing39Capture(100000,110000);s.TickCountdown();s.TickCountdown();s.TickCountdown();Assert.True(s.Begin(100000));
        Assert.True(s.Key(Timing39Key.F,true,100100));Assert.False(s.Key(Timing39Key.F,true,100110));
        Assert.True(s.Key(Timing39Key.D,true,100120));Assert.True(s.Key(Timing39Key.J,true,100200));
        Assert.True(s.Key(Timing39Key.F,false,100250));Assert.True(s.Key(Timing39Key.J,false,100300));
        Assert.True(s.Key(Timing39Key.D,false,100350));Assert.True(s.Stop(100400));Assert.False(s.Stop(100500));
        Assert.Equal(new[]{new Timing39Block(100000,100100,true),new(100100,100200,false),new(100200,100300,false),new(100300,100400,true)},s.Blocks(0));
        Assert.Equal(new[]{new Timing39Block(100000,100120,true),new(100120,100350,false),new(100350,100400,true)},s.Blocks(1));
    }
    [Fact] public void BackwardTimeCannotCreateOverlappingBlocksAndUntouchedLaneStaysEmpty()
    {
        var s=new Timing39Capture(0,1000);s.TickCountdown();s.TickCountdown();s.TickCountdown();s.Begin(0);
        Assert.True(s.Key(Timing39Key.F,true,200));Assert.False(s.Key(Timing39Key.J,true,199));
        Assert.True(s.Stop(250));Assert.Empty(s.Blocks(1));Assert.Equal(new Timing39Block(200,250,false),s.Blocks(0)[1]);
    }
    [Fact] public void PartitionPreservesRawTimesRejectsTiesAndMarksClippedNotesForReview()
    {
        Timing39Block[] raw=[new(900,1100,false),new(1100,1200,true),new(1200,1450,false),new(1450,1550,false)];
        var result=Timing39Partitioner.Partition(raw,1000,1500);
        Assert.Equal(new[]{new Timing39Block(1100,1200,true),new(1200,1450,false)},result.Blocks);
        Assert.Equal(new[]{1,2},result.RawIndices);Assert.False(result.NeedsReview);Assert.Equal(new Timing39Block(900,1100,false),raw[0]);
        var clipped=Timing39Partitioner.Partition(raw,980,1520);
        Assert.True(clipped.NeedsReview);Assert.Equal(new Timing39Block(980,1100,false),clipped.Blocks[0]);
        Assert.Empty(Timing39Partitioner.Partition([new Timing39Block(0,100,true)],0,100).Blocks);
    }
    [Fact] public void SetupUsesEarliestCommentMarkerAndSnapshotsOnlyExplicitLyricSelection()
    {
        const string source="[Events]\nFormat: Start, End, Style, Text\nComment: 0:00:05.00,0:00:06.00,Default, 39 mode start here \nComment: 0:00:02.00,0:00:03.00,Default,{\\i1}39 MODE START HERE\nDialogue: 0:00:03.00,0:00:04.00,A,あい\nDialogue: 0:00:04.00,0:00:07.00,B,うえ\nDialogue: 0:00:00.00,0:00:01.00,C,39 mode start here\n";
        var doc=AssDocument.Parse(source);var setup=Timing39SessionSetup.Create(doc.Events,[doc.Events[2],doc.Events[3]],doc.Events[2],10000);
        Assert.Equal(2000,setup.Start);Assert.Equal(7000,setup.End);Assert.Equal(1,setup.MarkerRow);Assert.True(setup.ExplicitSelection);
        Assert.Equal(new[]{2,3},setup.Targets.Select(target=>target.Row));Assert.Equal("A",setup.ActiveStyle);
        doc.Events[2].Text="changed";Assert.Equal("あい",setup.Targets[0].Text);
        Assert.Throws<ArgumentException>(()=>Timing39SessionSetup.Create(doc.Events,[AssDocument.Parse(source).Events[2]],doc.Events[2],10000));
        Assert.Throws<ArgumentException>(()=>Timing39SessionSetup.Create(doc.Events,[],doc.Events[2],2000));
    }
}
