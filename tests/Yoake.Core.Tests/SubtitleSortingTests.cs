using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class SubtitleSortingTests
{
    private const string Source="[Events]\nFormat: Layer,Start,End,Style,Name,MarginL,MarginR,MarginV,Effect,Extra,Text\n"+
        "Dialogue: 4,0:00:04.00,0:00:08.00,Z,z,0,0,0,z,unknown4,{\\future(opaque)}日本語\n"+
        "; retain this source comment\nDialogue: 2,0:00:02.00,0:00:06.00,B,b,0,0,0,b,unknown2,မြန်မာ\n"+
        "Dialogue: 3,0:00:03.00,0:00:07.00,C,c,0,0,0,c,unknown3,middle\n"+
        "Dialogue: 1,0:00:01.00,0:00:05.00,A,a,0,0,0,a,unknown1,end 👩‍👩‍👧‍👦\n";
    [Theory]
    [InlineData(SubtitleSortKey.Start)][InlineData(SubtitleSortKey.End)][InlineData(SubtitleSortKey.Style)]
    [InlineData(SubtitleSortKey.Actor)][InlineData(SubtitleSortKey.Effect)][InlineData(SubtitleSortKey.Layer)]
    public void EveryKeySortsRecordsStablyAndUndoesTheExactSource(SubtitleSortKey key)
    {
        var editor=new SubtitleEditor(AssDocument.Parse(Source));var records=editor.Document.Events.ToArray();
        editor.ApplySort(editor.CaptureSort(key).Calculate());
        Assert.Equal(new[]{records[3],records[1],records[2],records[0]},editor.Document.Events);
        Assert.Equal("unknown4",records[0].Get("Extra"));Assert.Contains("; retain this source comment",editor.Document.Serialize());
        var reopened=AssDocument.Parse(editor.Document.Serialize());Assert.Equal("{\\future(opaque)}日本語",reopened.Events[^1].Text);
        editor.Undo.Undo();Assert.Equal(Source,editor.Document.Serialize());editor.Undo.Redo();Assert.Same(records[3],editor.Document.Events[0]);
    }
    [Fact] public void SelectedSortingDoesNotMoveRecordsAcrossUnselectedGaps()
    {
        var editor=new SubtitleEditor(AssDocument.Parse(Source));var rows=editor.Document.Events.ToArray();
        editor.ApplySort(editor.CaptureSort(SubtitleSortKey.Start,[rows[0],rows[1],rows[3]]).Calculate());
        Assert.Equal(new[]{rows[1],rows[0],rows[2],rows[3]},editor.Document.Events);editor.Undo.Undo();Assert.Equal(Source,editor.Document.Serialize());
        editor.SwapLines([rows[0],rows[3]]);Assert.Same(rows[3],editor.Document.Events[0]);editor.Undo.Undo();Assert.Equal(Source,editor.Document.Serialize());
    }
    [Fact] public void ChangedOrForeignSnapshotsAndCancellationCannotMutateTheDocument()
    {
        var editor=new SubtitleEditor(AssDocument.Parse(Source));var snapshot=editor.CaptureSort(SubtitleSortKey.Start);
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();Assert.ThrowsAny<OperationCanceledException>(()=>snapshot.Calculate(cancellation.Token));Assert.Equal(Source,editor.Document.Serialize());
        var order=snapshot.Calculate();editor.SetField(editor.Document.Events[0],"Text","new draft","Edit text");var changed=editor.Document.Serialize();
        Assert.Throws<InvalidOperationException>(()=>editor.ApplySort(order));Assert.Equal(changed,editor.Document.Serialize());
        var foreign=new SubtitleEditor(AssDocument.Parse(Source));Assert.Throws<InvalidOperationException>(()=>foreign.ApplySort(order));Assert.Equal(Source,foreign.Document.Serialize());
    }
    [Fact] public void EqualKeysRetainSourceOrderAndSupplementaryUnicodeSortsByScalarValue()
    {
        var editor=new SubtitleEditor(AssDocument.Parse(Source));var rows=editor.Document.Events.ToArray();
        editor.SetField(rows[0],"Style","A","Change style");editor.ApplySort(editor.CaptureSort(SubtitleSortKey.Style).Calculate());
        Assert.Equal(new[]{rows[0],rows[3],rows[1],rows[2]},editor.Document.Events);
        editor.SetField(rows[0],"Name","𐀀","Change actor");editor.SetField(rows[3],"Name","\uE000","Change actor");
        editor.ApplySort(editor.CaptureSort(SubtitleSortKey.Actor).Calculate());Assert.Equal(new[]{rows[1],rows[2],rows[3],rows[0]},editor.Document.Events);
    }
    [Fact] public void TextSortCanIncludeTagsOrCompareOnlyPlainBlocksWithoutChangingSource()
    {
        const string source="[Events]\nFormat: Layer,Start,End,Style,Name,MarginL,MarginR,MarginV,Effect,Text\n"+
            "Dialogue: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,,{\\future(x)}z\n"+
            "Dialogue: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,,a{\\p1}m 0 0 l 2 2{\\p0}b\n"+
            "Dialogue: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,,{comment}ab\n";
        var editor=new SubtitleEditor(AssDocument.Parse(source));var rows=editor.Document.Events.ToArray();
        editor.ApplySort(editor.CaptureSort(SubtitleSortKey.TextStripped).Calculate());
        Assert.Equal(new[]{rows[1],rows[2],rows[0]},editor.Document.Events);
        editor.Undo.Undo();Assert.Equal(source,editor.Document.Serialize());
        editor.ApplySort(editor.CaptureSort(SubtitleSortKey.Text).Calculate());
        Assert.Equal(new[]{rows[1],rows[0],rows[2]},editor.Document.Events);
        editor.Undo.Undo();Assert.Equal(source,editor.Document.Serialize());
        Assert.Equal("plain{unfinished",AssTextOperations.PlainTextForSorting("plain{unfinished"));
    }
}
