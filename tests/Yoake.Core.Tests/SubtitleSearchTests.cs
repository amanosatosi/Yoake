using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class SubtitleSearchTests
{
    private static SubtitleEditor Fixture()=>new(AssDocument.Parse("[Events]\nFormat: Layer,Start,End,Style,Name,MarginL,MarginR,MarginV,Effect,Extra,Text\n"+
        "Dialogue: 0,0:00:01.00,0:00:02.00,Default,Actor,0,0,0,Effect,opaque,a{\\future(x)\\distort(0,0,1,1)}bc abc\n"+
        "Comment: 0,0:00:02.00,0:00:03.00,Other,Other,0,0,0,Other,opaque2,abc 日本語\n"+
        "Dialogue: 0,0:00:03.00,0:00:04.00,Default,Actor,0,0,0,Effect,opaque3,abc é 👩‍👩‍👧‍👦\n"));
    [Fact] public void VisibleReplacementAcrossOverridesRetainsEveryProtectedSpanAndUndoesExactly()
    {
        var editor=Fixture();var line=editor.Document.Events[0];var original=editor.Document.Serialize();
        var search=new SubtitleSearch(new("abc",SkipTags:true));var found=search.Find(editor.Document.Events,line,0,false)!;
        Assert.Equal(0,found.Index);Assert.True(found.Length>3);search.Replace(editor,found,"XYZ");
        Assert.Equal("X{\\future(x)\\distort(0,0,1,1)}YZ abc",line.Text);Assert.Equal("opaque",line.Get("Extra"));
        editor.Undo.Undo();Assert.Equal(original,editor.Document.Serialize());
        Assert.Equal(3,search.ReplaceAll(editor,editor.Document.Events,"日本語"));Assert.Contains("\\future(x)\\distort(0,0,1,1)",line.Text);
        editor.Undo.Undo();Assert.Equal(original,editor.Document.Serialize());Assert.False(editor.Undo.CanUndo);
    }
    [Fact] public void VisibleSearchDecodesEscapesAndPreservesFuriganaAndDrawingSource()
    {
        var editor=Fixture();var line=editor.Document.Events[0];editor.SetField(line,"Text",@"<漢|かん>\N字{\p1}m 0 0 l 10 10{\p0} end","Fixture");var original=line.Text;
        var search=new SubtitleSearch(new("漢\n字",SkipTags:true));Assert.NotNull(search.Find([line],line,0,false));
        search.ReplaceAll(editor,[line],"日本");Assert.Contains("|かん>",line.Text);Assert.Contains(@"{\p1}m 0 0 l 10 10{\p0}",line.Text);
        editor.Undo.Undo();Assert.Equal(original,line.Text);
        Assert.Null(new SubtitleSearch(new("m 0",SkipTags:true)).Find([line],line,0,false));
        Assert.NotNull(new SubtitleSearch(new("m 0",SkipTags:false)).Find([line],line,0,false));
        var identity=new SubtitleSearch(new("漢\n字",RegularExpression:true,SkipTags:true));Assert.Equal(0,identity.ReplaceAll(editor,[line],"$&"));Assert.Equal(original,line.Text);
    }
    [Fact] public void FindWrapsInBothDirectionsAndRespectsCommentAndFieldScope()
    {
        var editor=Fixture();var rows=editor.Document.Events;var search=new SubtitleSearch(new("abc",IgnoreComments:true,SkipTags:true));
        Assert.Same(rows[0],search.Find(rows,rows[2],rows[2].Text.Length,false)!.Line);
        Assert.Same(rows[2],search.Find(rows,rows[0],0,true)!.Line);
        Assert.Null(new SubtitleSearch(new("abc",WholeField:true,IgnoreComments:true)).Find(rows,null,0,false));
        var actor=new SubtitleSearch(new("Actor",Field:SubtitleSearchField.Actor));Assert.Same(rows[2],actor.Find(rows,rows[0],0,false)!.Line);
        var original=editor.Document.Serialize();Assert.Equal(2,actor.ReplaceAll(editor,rows,"日本語"));Assert.Equal("日本語",rows[0].Actor);Assert.Contains("\\future(x)",rows[0].Text);
        editor.Undo.Undo();Assert.Equal(original,editor.Document.Serialize());
    }
    [Fact] public void RegexAndLiteralReplacementHaveDistinctSemanticsAndUnicodeFragmentsCannotBePublished()
    {
        var editor=Fixture();var line=editor.Document.Events[2];editor.SetField(line,"Text","é😀","Fixture");var source=editor.Document.Serialize();
        Assert.Null(new SubtitleSearch(new("e")).Find([line],line,0,false));
        Assert.Equal(0,new SubtitleSearch(new(".",RegularExpression:true)).ReplaceAll(editor,[line],"X"));
        Assert.Equal(1,new SubtitleSearch(new("é")).ReplaceAll(editor,[line],"$& $1"));Assert.StartsWith("$& $1",line.Text);editor.Undo.Undo();
        Assert.Throws<ArgumentException>(()=>new SubtitleSearch(new("(.){2}",RegularExpression:true)).ReplaceAll(editor,[line],"$1"));Assert.Equal(source,editor.Document.Serialize());
        var groups=new SubtitleSearch(new("(é)",RegularExpression:true));groups.ReplaceAll(editor,[line],"<$1>");Assert.StartsWith("<é>",line.Text);editor.Undo.Undo();Assert.Equal(source,editor.Document.Serialize());
        Assert.Throws<ArgumentException>(()=>new SubtitleSearch(new("[",RegularExpression:true)));
    }
    [Fact] public void SnapshotsCannotApplyToChangedOrForeignDocumentsAndCancellationLeavesHistoryUntouched()
    {
        var editor=Fixture();var search=new SubtitleSearch(new("abc",SkipTags:true));var inputs=search.CaptureInputs(editor.Document.Events);
        var found=search.FindSnapshot(inputs,inputs[0].Line,0,false)!;var edits=search.PrepareSnapshotReplacements(inputs,"changed");var source=editor.Document.Serialize();
        var foreign=Fixture();Assert.Throws<InvalidOperationException>(()=>SubtitleSearch.ApplyReplacements(foreign,edits));Assert.Equal(source,foreign.Document.Serialize());
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();Assert.ThrowsAny<OperationCanceledException>(()=>search.FindSnapshot(inputs,null,0,false,cancellation.Token));
        Assert.ThrowsAny<OperationCanceledException>(()=>search.PrepareSnapshotReplacements(inputs,"changed",cancellation.Token));Assert.False(editor.Undo.CanUndo);
        editor.SetField(inputs[0].Line,"Text","different","Edit");var changed=editor.Document.Serialize();
        Assert.Throws<InvalidOperationException>(()=>search.Replace(editor,found,"replacement"));Assert.Throws<InvalidOperationException>(()=>SubtitleSearch.ApplyReplacements(editor,edits));Assert.Equal(changed,editor.Document.Serialize());
    }
    [Fact] public void LaterFieldValidationFailureRollsBackTheWholeReplacementTransaction()
    {
        var editor=Fixture();var rows=editor.Document.Events;var original=editor.Document.Serialize();
        SubtitleSearch.Replacement[] edits=[new(rows[0],rows[0].Text,"first changed"),new(rows[1],rows[1].Actor,"invalid,actor","Name")];
        Assert.Throws<ArgumentException>(()=>SubtitleSearch.ApplyReplacements(editor,edits));Assert.Equal(original,editor.Document.Serialize());Assert.False(editor.Undo.CanUndo);
    }
    [Fact] public void SearchDoesNotNormalizeExactSourceAndLargeReplaceAllBuildsOneLogicalUndo()
    {
        var editor=Fixture();var line=editor.Document.Events[2];var original=editor.Document.Serialize();
        new SubtitleSearch(new("é")).Find(editor.Document.Events,null,0,false);Assert.Equal(original,editor.Document.Serialize());Assert.False(editor.Undo.CanUndo);
        editor.SetField(line,"Text",string.Concat(Enumerable.Repeat("漢 ",5000))+"{\\future(x)}","Fixture");original=editor.Document.Serialize();
        var search=new SubtitleSearch(new("漢",SkipTags:true));Assert.Equal(1,search.ReplaceAll(editor,[line],"မြန်မာ"));Assert.EndsWith("{\\future(x)}",line.Text);
        Assert.Equal(5000,line.Text.Split("မြန်မာ").Length-1);editor.Undo.Undo();Assert.Equal(original,editor.Document.Serialize());
    }
}
