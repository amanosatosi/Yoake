using Xunit;
using Yoake.Core.Commands;
using Yoake.Core.Subtitles;
using Yoake.UI.ViewModels;

namespace Yoake.UI.Tests;

public sealed partial class EditorWorkflowTests
{
    private (AssEvent Source,AssEvent Target) PrepareKanjiPair()
    {
        var editor=_model.ActiveEditor!;
        foreach(var name in new[] { "Romaji","Kanji" }) { var style=editor.Document.Styles[0].Clone();style.Name=name;editor.AddStyle(style); }
        var source=editor.Insert(null,true);source.Style="Romaji";source.Text=@"{\k10}to{\k20}u{\k30}kyo{\k40}u";
        var target=editor.Insert(null,true);target.Style="Kanji";target.Text=@"{\future(opaque)\bord2}東京";
        _model.SelectedEvent=target;return(source,target);
    }
    [Fact]
    public async Task KanjiTimingStagesLinksAndAcceptsThenAppliesOneExactUndoableTextEdit()
    {
        var (source,target)=PrepareKanjiPair();var editor=_model.ActiveEditor!;var before=editor.Document.Serialize();var sourceText=source.Text;
        using var session=_model.BeginKanjiTiming();await Command("tool/time/kanji/start",new KanjiTimingStart(session,"Romaji","Kanji"));
        await Command("tool/time/kanji/link",new KanjiTimingGroup(session,2,1));await Command("tool/time/kanji/unlink",session);
        Assert.Equal(4,session.Alignment!.SourceRemaining);Assert.Equal(before,editor.Document.Serialize());
        await Command("tool/time/kanji/link",new KanjiTimingGroup(session,2,1));await Command("tool/time/kanji/link",new KanjiTimingGroup(session,2,1));
        await Command("tool/time/kanji/accept",session);Assert.Equal(1,session.AcceptedCount);Assert.Equal(before,editor.Document.Serialize());
        await Command("tool/time/kanji/previous",session);Assert.Equal(0,session.AcceptedCount);Assert.Equal(4,session.Alignment!.SourceRemaining);
        await Command("tool/time/kanji/link",new KanjiTimingGroup(session,2,1));await Command("tool/time/kanji/link",new KanjiTimingGroup(session,2,1));await Command("tool/time/kanji/accept",session);
        await Command("tool/time/kanji/apply",session);
        Assert.Equal(@"{\k30}{\future(opaque)\bord2}東{\k70}京",target.Text);Assert.Equal(sourceText,source.Text);Assert.Equal("Kanji Timing Copier",editor.Undo.NextUndoName);
        Assert.Equal(target.Text,_model.Draft!.Text);var after=editor.Document.Serialize();await Command(CommandIds.EditUndo);Assert.Equal(before,editor.Document.Serialize());await Command(CommandIds.EditRedo);Assert.Equal(after,editor.Document.Serialize());
    }
    [Fact]
    public async Task MultipleAcceptedKanjiLinesCanBeCancelledOrAppliedAsOneUndoTransaction()
    {
        PrepareKanjiPair();var editor=_model.ActiveEditor!;
        var source=editor.Insert(null,true);source.Style="Romaji";source.Text=@"{\k5}o{\k7}sa{\k9}ka";
        var target=editor.Insert(null,true);target.Style="Kanji";target.Text="大阪 မြန်မာ";
        var before=editor.Document.Serialize();
        async Task Match(KanjiTimingSession session)
        {
            await Command("tool/time/kanji/start",new KanjiTimingStart(session,"Romaji","Kanji"));
            await Command("tool/time/kanji/link",new KanjiTimingGroup(session,4,2));await Command("tool/time/kanji/accept",session);
            await Command("tool/time/kanji/link",new KanjiTimingGroup(session,3,session.Alignment!.DestinationRemaining));await Command("tool/time/kanji/accept",session);
            Assert.Equal(2,session.AcceptedCount);Assert.Equal(before,editor.Document.Serialize());
        }
        using(var cancelled=_model.BeginKanjiTiming())await Match(cancelled);
        Assert.Equal(before,editor.Document.Serialize());
        using var applied=_model.BeginKanjiTiming();await Match(applied);await Command("tool/time/kanji/apply",applied);
        Assert.Equal(@"{\k21}大阪 မြန်မာ",target.Text);Assert.Equal(@"{\k5}o{\k7}sa{\k9}ka",source.Text);
        await Command(CommandIds.EditUndo);Assert.Equal(before,editor.Document.Serialize());
    }
    [Fact]
    public async Task KanjiTimingRejectsStaleDraftsForeignSessionsAndCancelledUnappliedWork()
    {
        PrepareKanjiPair();var editor=_model.ActiveEditor!;var before=editor.Document.Serialize();
        using var session=_model.BeginKanjiTiming();await Command("tool/time/kanji/start",new KanjiTimingStart(session,"Romaji","Kanji"));
        await Command("tool/time/kanji/link",new KanjiTimingGroup(session,4,2));await Command("tool/time/kanji/accept",session);
        _model.Draft!.Start="unfinished";
        Assert.False(await _model.Registry.InvokeAsync("tool/time/kanji/apply",new(),session));Assert.Equal(before,editor.Document.Serialize());Assert.Equal(1,session.AcceptedCount);
        _model.Draft.Start=_model.SelectedEvent!.Start;
        editor.SetField(editor.Document.Events[^1],"Name","External","External edit");var external=editor.Document.Serialize();
        Assert.False(await _model.Registry.InvokeAsync("tool/time/kanji/apply",new(),session));Assert.Equal(external,editor.Document.Serialize());
        await Command("tool/time/kanji/start",new KanjiTimingStart(session,"Romaji","Kanji"));Assert.Equal(0,session.AcceptedCount);
        await Command("tool/time/kanji/skip-source",session);Assert.Null(session.Alignment);await Command("tool/time/kanji/previous",session);Assert.NotNull(session.Alignment);
        var oldId=_model.ActiveDocumentId;_workspace.CreateUntitled();
        Assert.False(await _model.Registry.InvokeAsync("tool/time/kanji/apply",new(oldId),session));Assert.Equal(external,editor.Document.Serialize());
    }
}
