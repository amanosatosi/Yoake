using Yoake.Core.Media;
using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class SubtitleSelectionTests
{
    private static SubtitleEditor Fixture()=>new(AssDocument.Parse("[Events]\nFormat: "+string.Join(',',AssDocument.EventFormat)+"\n"+
        "Dialogue: 0,0:00:00.00,0:00:00.12,Default,Actor,0,0,0,fx,{\\future(x)\\distort(0,0,1,1)}日本語 é\n"+
        "Comment: 0,0:00:00.07,0:00:00.21,Sign,Other,0,0,0,,日本語\n"+
        "Dialogue: 0,0:00:00.07,0:00:00.21,Sign,Actor,0,0,0,fx,မြန်မာ 👩‍👩‍👧‍👦\n"+
        "Dialogue: 0,0:00:00.22,0:00:00.35,Default,,0,0,0,,{\\future(opaque)}\n"));
    [Theory][InlineData(SubtitleSelectionAction.Replace,0)][InlineData(SubtitleSelectionAction.Add,0,2)][InlineData(SubtitleSelectionAction.Subtract,2)][InlineData(SubtitleSelectionAction.Intersect)]
    public void SetOperationsKeepDocumentOrderAndNeverMutateSourceOrUndo(SubtitleSelectionAction action,params int[] expected)
    {
        var editor=Fixture();var rows=editor.Document.Events.ToArray();var source=editor.Document.Serialize();
        var result=SubtitleSelectionSnapshot.Capture(editor,[rows[2]],new(){Query="日本語",Action=action}).Match();
        result.Validate(editor);Assert.Equal(expected.Select(index=>rows[index]),result.Lines);Assert.Equal(1,result.MatchCount);
        Assert.Equal(source,editor.Document.Serialize());Assert.False(editor.Undo.CanUndo);Assert.Throws<NotSupportedException>(()=>((IList<AssEvent>)result.Lines).Clear());
    }
    [Fact] public void CriteriaRespectCommentFilteringInversionFieldsVisibleProjectionRawSourceAndEmptyExactMatches()
    {
        var editor=Fixture();var rows=editor.Document.Events.ToArray();
        IReadOnlyList<AssEvent> Select(SubtitleSelectionOptions options)=>SubtitleSelectionSnapshot.Capture(editor,[],options).Match().Lines;
        Assert.Equal(new[]{rows[0],rows[1]},Select(new(){Query="日本語",IncludeComments=true}));
        Assert.Equal(new[]{rows[2],rows[3]},Select(new(){Query="日本語",Invert=true}));
        Assert.Equal(new[]{rows[1]},Select(new(){Query="日本語",IncludeDialogue=false,IncludeComments=true,Mode=SubtitleSelectionMode.Exact}));
        Assert.Equal(new[]{rows[0],rows[2]},Select(new(){Query="ACTOR",Field=SubtitleSearchField.Actor,Mode=SubtitleSelectionMode.Exact}));
        Assert.Empty(Select(new(){Query="ACTOR",Field=SubtitleSearchField.Actor,MatchCase=true}));
        Assert.Equal(new[]{rows[2]},Select(new(){Query="Sign",Field=SubtitleSearchField.Style}));
        Assert.Equal(new[]{rows[0],rows[2]},Select(new(){Query="^fx$",Field=SubtitleSearchField.Effect,Mode=SubtitleSelectionMode.RegularExpression}));
        Assert.Empty(Select(new(){Query="future"}));Assert.Equal(new[]{rows[0],rows[3]},Select(new(){Query="future",RawSource=true}));
        Assert.Equal(new[]{rows[3]},Select(new(){Mode=SubtitleSelectionMode.Exact}));Assert.Equal(new[]{rows[0],rows[2],rows[3]},Select(new()));
    }
    [Fact] public void UnicodeFragmentsInvalidRegexCancellationAndStaleOwnershipCannotPublishUnsafeResults()
    {
        var editor=Fixture();var rows=editor.Document.Events.ToArray();var source=editor.Document.Serialize();
        Assert.Empty(SubtitleSelectionSnapshot.Capture(editor,[],new(){Query="e"}).Match().Lines);
        Assert.Single(SubtitleSelectionSnapshot.Capture(editor,[],new(){Query="é"}).Match().Lines);
        Assert.Single(SubtitleSelectionSnapshot.Capture(editor,[],new(){Query="👩‍👩‍👧‍👦"}).Match().Lines);
        var invalid=Assert.Throws<ArgumentException>(()=>SubtitleSelectionSnapshot.Capture(editor,[],new(){Query="[",Mode=SubtitleSelectionMode.RegularExpression}));Assert.IsType<System.Text.RegularExpressions.RegexParseException>(invalid.InnerException);
        Assert.Throws<ArgumentException>(()=>SubtitleSelectionSnapshot.Capture(editor,[],new(){IncludeDialogue=false,IncludeComments=false}));Assert.Equal(source,editor.Document.Serialize());Assert.False(editor.Undo.CanUndo);
        var snapshot=SubtitleSelectionSnapshot.Capture(editor,[],new(){Query="pending"},rows[0],new Dictionary<string,string>{{"Text","pending 日本語"}});Assert.Single(snapshot.Match().Lines);
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();Assert.ThrowsAny<OperationCanceledException>(()=>snapshot.Match(cancellation.Token));
        var result=snapshot.Match();Assert.Throws<InvalidOperationException>(()=>result.Validate(Fixture()));editor.SetField(rows[0],"Name","changed","Edit actor");Assert.Throws<InvalidOperationException>(()=>result.Validate(editor));
    }
    [Fact] public void VisibleSelectionUsesInclusiveReleaseVfrBoundariesAndIncludesTimedCommentRows()
    {
        var editor=Fixture();var rows=editor.Document.Events.ToArray();var timing=new FrameTiming([0,.04,.11,.17],25);
        var snapshot=SubtitleSelectionSnapshot.Capture(editor,[],new());
        Assert.Equal(new[]{rows[0],rows[1],rows[2]},snapshot.Visible(timing,2).Lines);
        Assert.Same(rows[0],snapshot.Visible(timing,2).ActiveLine(rows[2],firstVisible:true));
        Assert.Same(rows[2],snapshot.Visible(timing,2).ActiveLine(rows[2]));
        Assert.Equal(new[]{rows[1],rows[2]},snapshot.Visible(timing,3).Lines);
        Assert.Same(rows[1],snapshot.Visible(timing,3).ActiveLine(rows[2],firstVisible:true));
        Assert.Same(rows[2],snapshot.Visible(timing,10).ActiveLine(rows[2],firstVisible:true));
        Assert.Empty(snapshot.Visible(timing,10).Lines);Assert.False(editor.Undo.CanUndo);
    }
}
