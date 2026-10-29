using Yoake.Core.Media;
using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class SubtitleLineOperationTests
{
    private static SubtitleEditor Fixture()=>new(AssDocument.Parse("[Events]\nFormat: Layer,Start,End,Style,Name,MarginL,MarginR,MarginV,Effect,Extra,Text\n"+
        "Dialogue: 0,0:00:00.00,0:00:00.30,Default,,0,0,0,,opaque,{\\future(opaque)\\distort(0,0,1,1)}日本語 é 👩‍👩‍👧‍👦\n"+
        "; source comment\nDialogue: 0,0:00:00.50,0:00:00.70,Default,,0,0,0,,opaque2,{\\b1}မြန်မာ\n"+
        "Dialogue: 0,0:00:00.80,0:00:01.00,Default,,0,0,0,,opaque3,last\n"));
    [Theory] [InlineData(true,70)][InlineData(false,140)]
    public void FrameSplitRetainsSourceMetadataAndHasOneExactUndo(bool before,long boundary)
    {
        var editor=Fixture();var line=editor.Document.Events[0];var source=editor.Document.Serialize();
        var copies=editor.SplitAtFrame([line],new([0,.04,.1,.18],25),2,before);
        Assert.Single(copies);Assert.Equal(boundary,line.EndMilliseconds);Assert.Equal(boundary,copies[0].StartMilliseconds);Assert.Equal(300,copies[0].EndMilliseconds);
        Assert.Equal(line.Text,copies[0].Text);Assert.Equal("opaque",copies[0].Get("Extra"));Assert.Contains("; source comment",editor.Document.Serialize());
        editor.Undo.Undo();Assert.Equal(source,editor.Document.Serialize());Assert.False(editor.Undo.CanUndo);editor.Undo.Redo();Assert.Same(copies[0],editor.Document.Events[1]);
    }
    [Fact] public void OutsideRangeAndUnrepresentableEdgeSplitsMakeUsefulOneFrameCopiesWithoutChangingOriginals()
    {
        var editor=Fixture();var row=editor.Document.Events[1];var original=row.FieldNames.Select(row.Get).ToArray();
        var copy=editor.SplitAtFrame([row],new([0,.04,.1,.18],25),2,true).Single();Assert.Equal(original,row.FieldNames.Select(row.Get).ToArray());
        Assert.Equal(70,copy.StartMilliseconds);Assert.Equal(140,copy.EndMilliseconds);editor.Undo.Undo();
        var first=editor.Document.Events[0];original=first.FieldNames.Select(first.Get).ToArray();copy=editor.SplitAtFrame([first],new([0,.001,.003,.005],1000),0,true).Single();
        Assert.Equal(original,first.FieldNames.Select(first.Get).ToArray());Assert.True(copy.EndMilliseconds>copy.StartMilliseconds);
        var unchanged=editor.Document.Serialize();Assert.Throws<ArgumentOutOfRangeException>(()=>editor.SplitAtFrame([first],new([0,.04],25),2,false));Assert.Equal(unchanged,editor.Document.Serialize());
    }
    [Fact] public void DuplicationHandlesSeparateSelectedBlocksAndRetainsCopyOrder()
    {
        var editor=Fixture();var rows=editor.Document.Events.ToArray();var source=editor.Document.Serialize();var copies=editor.Duplicate([rows[0],rows[2]]);
        Assert.Equal(new[]{rows[0],copies[0],rows[1],rows[2],copies[1]},editor.Document.Events);editor.Undo.Undo();Assert.Equal(source,editor.Document.Serialize());
    }
    [Theory] [InlineData(SubtitleJoinMode.Concatenate)][InlineData(SubtitleJoinMode.KeepFirst)][InlineData(SubtitleJoinMode.Karaoke)]
    public void JoinVariantsKeepFirstIdentityAndUndoEveryRemovedRecord(SubtitleJoinMode mode)
    {
        var editor=Fixture();var rows=editor.Document.Events.ToArray();var source=editor.Document.Serialize();
        Assert.Same(rows[0],editor.Join([rows[0],rows[1]],mode));Assert.Equal(700,rows[0].EndMilliseconds);Assert.Equal("opaque",rows[0].Get("Extra"));
        if(mode==SubtitleJoinMode.Karaoke){Assert.StartsWith("{\\k30}",rows[0].Text);Assert.Contains("{\\k20}{\\b1}မြန်မာ",rows[0].Text);}
        if(mode==SubtitleJoinMode.Concatenate)Assert.Contains(" {\\b1}မြန်မာ",rows[0].Text);
        if(mode==SubtitleJoinMode.KeepFirst)Assert.DoesNotContain("မြန်မာ",rows[0].Text);
        editor.Undo.Undo();Assert.Equal(source,editor.Document.Serialize());Assert.False(editor.Undo.CanUndo);
    }
    [Fact] public void ClearVisibleTextKeepsUnknownBlocksAndDrawingSyntaxAndRollsBackExactly()
    {
        var editor=Fixture();var line=editor.Document.Events[0];editor.SetField(line,"Text",@"{\future(x)\distort(0,0,1,1)}漢\{literal\}{\p1}m 0 0 l 10 10{\p0}字{unfinished","Fixture");
        var source=editor.Document.Serialize();editor.ClearText(line,true);
        Assert.Equal(@"{\future(x)\distort(0,0,1,1)}{\p1}m 0 0 l 10 10{\p0}{unfinished",line.Text);
        editor.Undo.Undo();Assert.Equal(source,editor.Document.Serialize());editor.ClearText(line,false);Assert.Equal("",line.Text);editor.Undo.Undo();Assert.Equal(source,editor.Document.Serialize());
    }
    [Theory] [InlineData(CursorSplitTiming.Preserve,0,300)][InlineData(CursorSplitTiming.Estimate,100,100)]
    public void CursorSplitCountsGraphemesAndCarriesOpaqueTagsInsteadOfSplittingACombiningSequence(CursorSplitTiming mode,long secondStart,long firstEnd)
    {
        var editor=Fixture();var line=editor.Document.Events[0];editor.SetField(line,"Text",@"{\future(x)}é👩‍👩‍👧‍👦漢","Fixture");var source=editor.Document.Serialize();
        var cursor=line.Text.IndexOf("é",StringComparison.Ordinal)+2;var second=editor.SplitAtCursor(line,cursor,mode);
        Assert.EndsWith("é",line.Text);Assert.Equal(@"{\future(x)}👩‍👩‍👧‍👦漢",second.Text);Assert.Equal(secondStart,second.StartMilliseconds);Assert.Equal(firstEnd,line.EndMilliseconds);
        editor.Undo.Undo();Assert.Equal(source,editor.Document.Serialize());
        Assert.Equal(line.Text.IndexOf("é",StringComparison.Ordinal),AssText.SafeSplitIndex(line.Text,cursor-1));
        Assert.Equal(3,AssTextOperations.VisibleGraphemeCount(line.Text));Assert.Equal(1,AssTextOperations.VisibleGraphemeCount("<漢|かん>"));
        Assert.Equal(1,AssText.SafeSplitIndex(@"a\{b",2));Assert.Equal(3,AssText.SafeSplitIndex(@"a\{b",3));
        Assert.Equal(5,AssText.SafeSplitIndex(@"{\p1}m 0 0 l 10 10{\p0}a",12));
    }
}
