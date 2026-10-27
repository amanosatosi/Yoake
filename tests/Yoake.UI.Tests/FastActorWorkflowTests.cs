using Xunit;
using Yoake.Core.Commands;

namespace Yoake.UI.Tests;

public sealed partial class EditorWorkflowTests
{
    [Fact] public async Task FastActorAcceptAdvancesAndAutofillsEmptyLineAsOneUndo()
    {
        await Command(CommandIds.GridInsertAfter);var first=_model.SelectedEvent!;
        await Command(CommandIds.GridInsertAfter);var second=_model.SelectedEvent!;
        _model.SelectedEvent=first;var editor=_model.ActiveEditor!;var source=editor.Document.Serialize();
        await Command("actor/fast/toggle");_model.Draft!.Actor="  日本語 မြန်မာ  ";await Command("actor/fast/accept");
        Assert.Same(second,_model.SelectedEvent);Assert.Equal("日本語 မြန်မာ",first.Actor);Assert.Equal(first.Actor,second.Actor);
        Assert.Equal(new[]{"日本語 မြန်မာ"},_model.FastActorRecent);Assert.Equal("Fast actor naming",editor.Undo.NextUndoName);
        await Command(CommandIds.EditUndo);Assert.Equal(source,editor.Document.Serialize());
        await Command(CommandIds.EditRedo);Assert.Equal("日本語 မြန်မာ",first.Actor);Assert.Equal(first.Actor,second.Actor);
    }

    [Fact] public async Task FastActorRespectsExistingActorInvalidDraftAndTabOwnership()
    {
        await Command(CommandIds.GridInsertAfter);var first=_model.SelectedEvent!;
        await Command(CommandIds.GridInsertAfter);var second=_model.SelectedEvent!;
        _model.Draft!.Actor="Existing";await Command(CommandIds.EditCommit);_model.SelectedEvent=first;
        await Command("actor/fast/toggle");_model.Draft!.Actor="New";_model.Draft.Start="invalid";
        var source=_model.ActiveEditor!.Document.Serialize();await Command("actor/fast/accept");
        Assert.Same(first,_model.SelectedEvent);Assert.Equal(source,_model.ActiveEditor.Document.Serialize());Assert.Equal("invalid",_model.Draft.Start);
        _model.Draft.Start=first.Start;await Command("actor/fast/accept");Assert.Same(second,_model.SelectedEvent);Assert.Equal("Existing",second.Actor);
        var original=_model.ActiveDocumentId;await Command(CommandIds.SubtitleNew);await Command(CommandIds.GridInsertAfter);
        Assert.False(_model.FastActorEnabled);Assert.Empty(_model.FastActorRecent);
        await Command(CommandIds.WorkspaceActivateTab,original);Assert.True(_model.FastActorEnabled);Assert.Contains("New",_model.FastActorRecent);
    }

    [Fact] public async Task FastActorRecentSelectionAndEmptyInputAreExplicit()
    {
        await Command(CommandIds.GridInsertAfter);await Command("actor/fast/toggle");
        _model.Draft!.Actor="Alice";await Command("actor/fast/accept");
        _model.Draft!.Actor="Bob";await Command("actor/fast/accept");
        Assert.Equal(new[]{"Bob","Alice"},_model.FastActorRecent);
        await Command("actor/fast/step",1);Assert.Equal("Alice",_model.Draft!.Actor);
        _model.Draft.Actor="";Assert.Equal(-1,_model.FastActorSelectedIndex);
        await Command("actor/fast/accept");Assert.Equal("",_model.SelectedEvent!.Actor);
        Assert.False(await _model.Registry.InvokeAsync("actor/fast/accept",new(Guid.NewGuid())));
    }
}
