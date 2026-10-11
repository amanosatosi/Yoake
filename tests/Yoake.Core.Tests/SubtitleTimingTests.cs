using Yoake.Core.Media;
using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class SubtitleTimingTests
{
    private static SubtitleEditor Fixture()=>new(AssDocument.Parse("[Events]\nFormat: Layer,Start,End,Style,Name,MarginL,MarginR,MarginV,Effect,Extra,Text\n"+
        "Dialogue: 0,0:00:00.00,0:00:00.04,Default,日本語,0,0,0,,opaque,{\\future(x)\\distort(0,0,1,1)}é 👩‍👩‍👧‍👦\n"+
        "Comment: 0,0:00:00.07,0:00:00.14,Default,,0,0,0,,second,မြန်မာ\n"+
        "Dialogue: 0,0:00:00.20,0:00:00.30,Default,,0,0,0,,third,漢\n"));
    private static FrameTiming Vfr()=>new([0,.04,.1,.18,.25],25);
    [Fact] public void BulkTimingApplyUndoAndRedoPublishOneDocumentChangeEach()
    {
        var editor=Fixture();var count=0;editor.Document.Changed+=(_,_)=>count++;
        editor.ApplyTiming(editor.CaptureTiming(editor.Document.Events).Shift(new(100)));
        Assert.Equal(1,count);editor.Undo.Undo();Assert.Equal(2,count);editor.Undo.Redo();Assert.Equal(3,count);
    }
    [Theory][InlineData(SubtitleTimingScope.All,3)][InlineData(SubtitleTimingScope.Selected,1)][InlineData(SubtitleTimingScope.SelectionOnward,2)]
    public void ShiftScopeIsExplicitAndUndoRestoresLosslessSource(SubtitleTimingScope scope,int count)
    {
        var editor=Fixture();var rows=editor.Document.Events.ToArray();var source=editor.Document.Serialize();
        var plan=editor.CaptureTiming([rows[1]]).Shift(new(100,Scope:scope));Assert.Equal(count,editor.ApplyTiming(plan));
        Assert.Equal(170,rows[1].StartMilliseconds);Assert.Equal("second",rows[1].Get("Extra"));Assert.Same(rows[0],editor.Document.Events[0]);
        Assert.Contains("\\future(x)\\distort(0,0,1,1)",rows[0].Text);Assert.Equal("日本語",rows[0].Actor);
        editor.Undo.Undo();Assert.Equal(source,editor.Document.Serialize());Assert.False(editor.Undo.CanUndo);
        editor.Undo.Redo();Assert.Equal(170,rows[1].StartMilliseconds);
    }
    [Fact] public void FrameShiftUsesDistinctVfrEndpointConversionsAndClampsNegativeTimes()
    {
        var editor=Fixture();var rows=editor.Document.Events;var timing=Vfr();var source=editor.Document.Serialize();
        editor.ApplyTiming(editor.CaptureTiming([rows[1]]).Shift(new(1,SubtitleShiftUnit.Frames),timing));
        Assert.Equal(140,rows[1].StartMilliseconds);Assert.Equal(220,rows[1].EndMilliseconds);
        editor.Undo.Undo();Assert.Equal(source,editor.Document.Serialize());
        editor.ApplyTiming(editor.CaptureTiming(rows).Shift(new(-1000)));Assert.All(rows,row=>{Assert.Equal(0,row.StartMilliseconds);Assert.Equal(0,row.EndMilliseconds);});
        editor.Undo.Undo();Assert.Equal(source,editor.Document.Serialize());
    }
    [Fact] public void EndpointOnlyShiftPreservesTheExactOtherEndpointAndRejectsInvertedRangesAtomically()
    {
        var editor=Fixture();var line=editor.Document.Events[1];editor.SetField(line,"End","0:00:00.145","Fixture");var original=editor.Document.Serialize();
        editor.ApplyTiming(editor.CaptureTiming([line]).Shift(new(25,Fields:SubtitleTimingFields.Start)));
        Assert.Equal(100,line.StartMilliseconds);Assert.Equal("0:00:00.145",line.End);editor.Undo.Undo();Assert.Equal(original,editor.Document.Serialize());
        var history=editor.Undo.NextUndoName;
        Assert.Throws<ArgumentException>(()=>editor.CaptureTiming(editor.Document.Events).Shift(new(1000,Fields:SubtitleTimingFields.Start)));
        Assert.Equal(original,editor.Document.Serialize());Assert.Equal(history,editor.Undo.NextUndoName);
    }
    [Theory][InlineData(true)][InlineData(false)]
    public void AdjoinOneLineUsesItsOutsideNeighborWhileBlocksChangeOnlyInternalBoundaries(bool start)
    {
        var editor=Fixture();var rows=editor.Document.Events;var original=editor.Document.Serialize();
        editor.ApplyTiming(editor.CaptureTiming([rows[1]]).Adjoin(start));
        Assert.Equal(start?40:70,rows[1].StartMilliseconds);Assert.Equal(start?140:200,rows[1].EndMilliseconds);
        editor.Undo.Undo();Assert.Equal(original,editor.Document.Serialize());
        editor.ApplyTiming(editor.CaptureTiming([rows[0],rows[1]]).Adjoin(start));
        Assert.Equal(start?40:70,start?rows[1].StartMilliseconds:rows[0].EndMilliseconds);Assert.Equal(140,rows[1].EndMilliseconds);
        editor.Undo.Undo();Assert.Equal(original,editor.Document.Serialize());
        Assert.False(editor.CaptureTiming([rows[0],rows[2]]).CanAdjoin);Assert.Throws<ArgumentException>(()=>editor.CaptureTiming([rows[0],rows[2]]).Adjoin(start));
        Assert.False(editor.Undo.CanUndo);
    }
    [Fact] public void SnapsExtendTheOtherBoundaryWhenNeededAndApplyToEverySelectedRow()
    {
        var editor=Fixture();var rows=editor.Document.Events;var source=editor.Document.Serialize();var timing=Vfr();
        editor.ApplyTiming(editor.CaptureTiming([rows[0],rows[2]]).Snap(timing,2,true));
        Assert.Equal(70,rows[0].StartMilliseconds);Assert.Equal(140,rows[0].EndMilliseconds);Assert.Equal(70,rows[2].StartMilliseconds);Assert.Equal(300,rows[2].EndMilliseconds);
        Assert.Equal(70,rows[1].StartMilliseconds);editor.Undo.Undo();Assert.Equal(source,editor.Document.Serialize());
        editor.ApplyTiming(editor.CaptureTiming([rows[0],rows[2]]).Snap(timing,1,false));
        Assert.Equal(70,rows[2].EndMilliseconds);Assert.Equal(20,rows[2].StartMilliseconds);editor.Undo.Undo();Assert.Equal(source,editor.Document.Serialize());
    }
    [Theory][InlineData(0,0,70)][InlineData(1,0,70)][InlineData(2,70,280)][InlineData(4,70,280)]
    public void SceneIncludesCurrentKeyframeThroughTheFrameBeforeNextCut(int frame,long start,long end)
    {
        var editor=Fixture();var rows=editor.Document.Events;var source=editor.Document.Serialize();
        editor.ApplyTiming(editor.CaptureTiming(rows).SnapScene(Vfr(),frame,[0,2]));
        Assert.All(rows,row=>{Assert.Equal(start,row.StartMilliseconds);Assert.Equal(end,row.EndMilliseconds);});
        editor.Undo.Undo();Assert.Equal(source,editor.Document.Serialize());
    }
    [Fact] public void ShiftToFrameRetainsRelativeDurationsAndPlansRejectForeignStaleAndCancelledWork()
    {
        var editor=Fixture();var rows=editor.Document.Events;var source=editor.Document.Serialize();var snapshot=editor.CaptureTiming([rows[0],rows[1]]);
        var plan=snapshot.ShiftToFrame(rows[0],Vfr(),2);editor.ApplyTiming(plan);
        Assert.Equal(70,rows[0].StartMilliseconds);Assert.Equal(110,rows[0].EndMilliseconds);Assert.Equal(140,rows[1].StartMilliseconds);Assert.Equal(210,rows[1].EndMilliseconds);
        editor.Undo.Undo();Assert.Equal(source,editor.Document.Serialize());
        Assert.Throws<InvalidOperationException>(()=>editor.ApplyTiming(plan));Assert.Throws<InvalidOperationException>(()=>Fixture().ApplyTiming(plan));
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(()=>snapshot.Shift(new(100),token:cancellation.Token));Assert.False(editor.Undo.CanUndo);
        Assert.Throws<ArgumentException>(()=>snapshot.Shift(new(1,SubtitleShiftUnit.Frames)));
        Assert.Throws<ArgumentException>(()=>snapshot.SnapScene(Vfr(),1,[2,1]));Assert.Equal(source,editor.Document.Serialize());
    }
}
