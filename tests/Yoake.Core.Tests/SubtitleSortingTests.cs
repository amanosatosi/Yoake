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
    private static string MetadataDocument()=>AssDocument.CreateEmpty().Serialize()+
        "Comment: 9,0:00:09.00,0:00:09.50,Default,,0,0,0,mangetsu-style,{\\future(style)}\n"+
        "Comment: 8,0:00:08.00,0:00:08.50,Default,Nene,0,0,0,mangetsu-colorcoding,{\\1c&H123456&}\n"+
        "Comment: 7,0:00:07.00,0:00:07.50,Default,,0,0,0,{uketsugu},first 日本語\n"+
        "Dialogue: 6,0:00:06.00,0:00:06.50,Default,,0,0,0,{uketsugu},second မြန်မာ\n"+
        "Dialogue: 3,0:00:03.00,0:00:06.00,C,C,0,0,0,C,{\\i1}C\n"+
        "Comment: 5,0:00:05.00,0:00:05.50,Default,,0,0,0,mangetsu-style,{\\future(inactive)}\n"+
        "Dialogue: 1,0:00:01.00,0:00:04.00,A,A,0,0,0,A,{\\i1}A\n"+
        "Dialogue: 2,0:00:02.00,0:00:05.00,B,B,0,0,0,B,{\\i1}B\n";
    [Theory]
    [InlineData(SubtitleSortKey.Start)][InlineData(SubtitleSortKey.End)][InlineData(SubtitleSortKey.Style)]
    [InlineData(SubtitleSortKey.Actor)][InlineData(SubtitleSortKey.Effect)][InlineData(SubtitleSortKey.Layer)]
    [InlineData(SubtitleSortKey.Text)][InlineData(SubtitleSortKey.TextStripped)]
    public void SortAllRetainsMetadataAndInheritedRecordsInExactSlots(SubtitleSortKey key)
    {
        var source=MetadataDocument();var document=AssDocument.Parse(source);var editor=new SubtitleEditor(document);var rows=document.Events.ToArray();
        editor.ApplySort(editor.CaptureSort(key).Calculate());
        Assert.Equal(new[] { rows[0],rows[1],rows[2],rows[3],rows[6],rows[5],rows[7],rows[4] },document.Events);
        Assert.Equal(new[] { "first 日本語","second မြန်မာ" },document.Events.Where(MangetsuMetadata.IsInherited).Select(e=>e.Text));
        Assert.Equal(2,MangetsuMetadata.Initial(document).Length);Assert.Equal(@"{\future(inactive)}",document.Events[5].Text);
        var after=document.Serialize();Assert.Equal(after,AssDocument.Parse(after).Serialize());editor.Undo.Undo();Assert.Equal(source,document.Serialize());editor.Undo.Redo();Assert.Equal(after,document.Serialize());
    }
    [Fact]
    public void ATextTokenDoesNotFreezeAnOrdinaryDialogueSortSlot()
    {
        var document=AssDocument.Parse(AssDocument.CreateEmpty().Serialize()+
            "Dialogue: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,,{uketsugu}Z\n"+
            "Dialogue: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,,A\n");
        var editor=new SubtitleEditor(document);var ordinary=document.Events[0];editor.ApplySort(editor.CaptureSort(SubtitleSortKey.Text).Calculate());
        Assert.Same(ordinary,document.Events[1]);Assert.False(MangetsuMetadata.IsInherited(ordinary));Assert.Equal("",ordinary.Effect);
    }
    [Fact]
    public void SelectedMetadataDoesNotBecomeSortableOrBridgeUnselectedGaps()
    {
        var document=AssDocument.Parse(MetadataDocument());var editor=new SubtitleEditor(document);var rows=document.Events.ToArray();var before=document.Serialize();
        editor.ApplySort(editor.CaptureSort(SubtitleSortKey.Text,rows.Take(4)).Calculate());Assert.Equal(before,document.Serialize());Assert.False(editor.Undo.CanUndo);
        editor.ApplySort(editor.CaptureSort(SubtitleSortKey.Text,[rows[4],rows[6],rows[7]]).Calculate());Assert.Equal(before,document.Serialize());
        editor.ApplySort(editor.CaptureSort(SubtitleSortKey.Text,[rows[4],rows[5],rows[6],rows[7]]).Calculate());Assert.Same(rows[6],document.Events[4]);Assert.Same(rows[5],document.Events[5]);Assert.Same(rows[4],document.Events[7]);
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
