using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class SubtitlePasteOverTests
{
    private const string Source="[Events]\r\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text, Future\n"+
        "  Dialogue:\t3,0:00:01.00,0:00:02.00,Default,A,01,02,03,fx,{\\future(x)\\distort(1,2)}日本語,opaque\r"+
        "; untouched source\nDialogue: 4,0:00:03.00,0:00:04.00,Default,B,04,05,06,fx,second,raw\n"+
        "Future: keep,verbatim\nComment: 5,0:00:05.00,0:00:06.00,Default,C,07,08,09,fx,third,keep\n";
    [Fact] public void SingleSelectionPastesDownwardAndUndoRestoresExactSource()
    {
        var editor=new SubtitleEditor(AssDocument.Parse(Source));var lines=editor.Document.Events.ToArray();
        var pasted=SubtitlePasteClipboard.Parse(" မြန်မာ 👩‍👩‍👧\r\n日本語\nlast\rexcess");
        Assert.Equal(3,editor.PasteOver(pasted,[lines[0]],lines[0],PasteOverFields.Text));
        Assert.Equal("မြန်မာ 👩‍👩‍👧",lines[0].Text);Assert.Equal("opaque",lines[0].Get("Future"));Assert.Equal("01",lines[0].MarginL);
        Assert.True(lines[2].IsComment);Assert.Contains("Future: keep,verbatim",editor.Document.Serialize());
        editor.Undo.Undo();Assert.Equal(Source,editor.Document.Serialize());Assert.False(editor.Undo.CanUndo);
        editor.Undo.Redo();Assert.Equal("last",lines[2].Text);
    }
    [Fact] public void MultipleSelectionOnlyTouchesSortedSelectedRows()
    {
        var editor=new SubtitleEditor(AssDocument.Parse(Source));var lines=editor.Document.Events.ToArray();
        Assert.Equal(2,editor.PasteOver(SubtitlePasteClipboard.Parse("a\nb\nc"),[lines[2],lines[0]],lines[2],PasteOverFields.Text));
        Assert.Equal("a",lines[0].Text);Assert.Equal("second",lines[1].Text);Assert.Equal("b",lines[2].Text);
    }
    [Fact] public void CustomFormatAndCommentCarryOnlySelectedFields()
    {
        var editor=new SubtitleEditor(AssDocument.Parse(Source));var line=editor.Document.Events[0];
        var clipboard=SubtitlePasteClipboard.Parse("[Script Info]\nTitle: ignored\n[Events]\nFormat: Text,End,Actor,Start,Custom\nComment: {\\box(1,2,3,4)}a,b,0:00:08.00,日本語,0:00:07.00,unknown\n");
        editor.PasteOver(clipboard,[line],line,PasteOverFields.Times|PasteOverFields.Actor|PasteOverFields.Comment);
        Assert.True(line.IsComment);Assert.Equal("日本語",line.Actor);Assert.Equal("0:00:07.00",line.Start);Assert.Contains("future",line.Text);Assert.Equal("opaque",line.Get("Future"));
        editor.Undo.Undo();Assert.Equal(Source,editor.Document.Serialize());
    }
    [Fact] public void YoakeCopiedRecordsRoundTripThroughPasteOver()
    {
        var editor=new SubtitleEditor(AssDocument.Parse(Source));var rows=editor.Document.Events.ToArray();
        var clipboard=SubtitlePasteClipboard.Parse(editor.Copy([rows[2]]));
        editor.PasteOver(clipboard,[rows[0]],rows[0],PasteOverFields.All);
        Assert.Equal(rows[2].Text,rows[0].Text);Assert.True(rows[0].IsComment);Assert.Equal("opaque",rows[0].Get("Future"));
    }
    [Theory]
    [InlineData("Dialogue: broken")]
    [InlineData("Format: Start,End,Text\nDialogue: invalid,0:00:01.00,hello")]
    [InlineData("[Events]\nFormat: Text,Text\nDialogue: a,b")]
    [InlineData("Dialogue: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,,hello\nplain second")]
    public void MalformedStructuredClipboardIsRejected(string text)=>Assert.Throws<ArgumentException>(()=>SubtitlePasteClipboard.Parse(text));
    [Fact] public void BracketedLyricsRemainPlainText()
    {
        var lines=SubtitlePasteClipboard.Parse("[Chorus]\r\n日本語");Assert.Equal(new[]{"[Chorus]","日本語"},lines.Select(l=>l.Text));
    }
    [Fact] public void CancelledClipboardAndPublicationNeverMutateSource()
    {
        var editor=new SubtitleEditor(AssDocument.Parse(Source));var line=editor.Document.Events[0];using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>SubtitlePasteClipboard.Parse("new text",cancellation.Token));
        Assert.Throws<OperationCanceledException>(()=>editor.PasteOver(SubtitlePasteClipboard.Parse("new text"),[line],line,PasteOverFields.Text,token:cancellation.Token));
        Assert.Equal(Source,editor.Document.Serialize());Assert.False(editor.Undo.CanUndo);
    }
    [Fact] public void CancellationDuringPublicationOnlyNotifiesObserversAfterRollback()
    {
        var editor=new SubtitleEditor(AssDocument.Parse(Source));var line=editor.Document.Events[0];using var cancellation=new CancellationTokenSource();var observed=new List<string>();
        editor.Document.Changed+=(_,_)=>observed.Add(editor.Document.Serialize());
        line.PropertyChanged+=(_,_)=>{if(line.Text=="new text")cancellation.Cancel();};
        Assert.Throws<OperationCanceledException>(()=>editor.PasteOver(SubtitlePasteClipboard.Parse("new text"),[line],line,PasteOverFields.Text,token:cancellation.Token));
        Assert.Equal(Source,editor.Document.Serialize());Assert.False(editor.Undo.CanUndo);Assert.Single(observed);Assert.All(observed,value=>Assert.Equal(Source,value));
    }
    [Fact] public void InvalidLaterResultRollsBackBeforeFirstRowAndKeepsDraft()
    {
        var editor=new SubtitleEditor(AssDocument.Parse(Source));var rows=editor.Document.Events.ToArray();
        var data=SubtitlePasteClipboard.Parse("Format: Start,End,Text\nDialogue: 0:00:00.00,0:00:01.00,a\nDialogue: 0:00:09.00,0:00:10.00,b");
        Assert.Throws<ArgumentException>(()=>editor.PasteOver(data,[rows[0]],rows[0],PasteOverFields.Start));
        Assert.Equal(Source,editor.Document.Serialize());Assert.False(editor.Undo.CanUndo);
    }
    [Fact] public void ActiveDraftAndPasteAreOneUndoOperation()
    {
        var editor=new SubtitleEditor(AssDocument.Parse(Source));var line=editor.Document.Events[0];
        var draft=new Dictionary<string,string>{{"Name","draft actor"},{"Text","draft text"}};
        editor.PasteOver(SubtitlePasteClipboard.Parse("new text"),[line],line,PasteOverFields.Text,draft);
        Assert.Equal("draft actor",line.Actor);Assert.Equal("new text",line.Text);
        editor.Undo.Undo();Assert.Equal(Source,editor.Document.Serialize());Assert.False(editor.Undo.CanUndo);
    }
    [Fact] public void NoneEmptyAndForeignTargetsDoNotMutate()
    {
        var editor=new SubtitleEditor(AssDocument.Parse(Source));var line=editor.Document.Events[0];var clipboard=SubtitlePasteClipboard.Parse("x");
        Assert.Equal(0,editor.PasteOver(clipboard,[],line,PasteOverFields.All));Assert.Equal(0,editor.PasteOver(clipboard,[line],line,PasteOverFields.None));
        Assert.Equal(0,editor.PasteOver([],[line],line,PasteOverFields.All));
        var foreign=AssDocument.Parse(Source).Events[0];Assert.Throws<InvalidOperationException>(()=>editor.PasteOver(clipboard,[foreign],foreign,PasteOverFields.Text));
        Assert.Equal(Source,editor.Document.Serialize());Assert.False(editor.Undo.CanUndo);
    }
    [Fact] public void PartialClipboardFormatPreservesAbsentFields()
    {
        var editor=new SubtitleEditor(AssDocument.Parse(Source));var line=editor.Document.Events[0];
        editor.PasteOver(SubtitlePasteClipboard.Parse("Format: Text\nDialogue: a,b"),[line],line,PasteOverFields.All);
        Assert.Equal("a,b",line.Text);Assert.Equal("3",line.Layer);Assert.Equal("A",line.Actor);Assert.Equal("0:00:01.00",line.Start);
    }
}
