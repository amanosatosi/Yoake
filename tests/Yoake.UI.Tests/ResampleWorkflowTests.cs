using Xunit;
using Yoake.Core.Commands;
using Yoake.Core.Subtitles;

namespace Yoake.UI.Tests;

public sealed partial class EditorWorkflowTests
{
    [Fact] public async Task ResamplePreviewPublishesValidDraftInOneUndoAndPreservesDocumentSelection()
    {
        await Command(CommandIds.GridInsertAfter);var editor=_model.ActiveEditor!;var line=_model.SelectedEvent;var original=editor.Document.Serialize();
        _model.Draft!.Text="{\\pos(10,20)\\distort(0,0,1,0,1,1,0,1)}draft 日本語";_model.Draft.Actor="draft actor";
        await Command("resample/preview",new ResampleOptions(1920,1080,3840,2160));Assert.NotNull(_model.ResamplePreview);Assert.Equal(original,editor.Document.Serialize());
        await Command("resample/apply");Assert.Same(line,_model.SelectedEvent);Assert.Same(editor,_model.ActiveEditor);Assert.Contains("\\pos(20,40)",_model.Draft!.Text);Assert.Equal("draft actor",_model.Draft.Actor);
        await Command(CommandIds.EditUndo);Assert.Equal(original,editor.Document.Serialize());Assert.Equal("Insert subtitle",editor.Undo.NextUndoName);
        await Command(CommandIds.EditRedo);Assert.Contains("draft 日本語",_model.Draft!.Text);
    }
    [Fact] public async Task ResampleRejectsInvalidDraftAndDimensionInputWithoutMutation()
    {
        await Command(CommandIds.GridInsertAfter);var editor=_model.ActiveEditor!;var source=editor.Document.Serialize();_model.Draft!.Start="unfinished";var draft=_model.Draft;
        Assert.False(await _model.Registry.InvokeAsync("resample/preview",new(),new ResampleOptions(1920,1080,1280,720)));Assert.Same(draft,_model.Draft);Assert.Equal("unfinished",draft.Start);Assert.Equal(source,editor.Document.Serialize());
        draft.Start=_model.SelectedEvent!.Start;Assert.False(await _model.Registry.InvokeAsync("resample/preview",new(),new ResampleOptions(1920,1080,0,720)));Assert.Equal(source,editor.Document.Serialize());
    }
    [Fact] public async Task ResampleRequiresReviewedUnchangedDraftAndOwner()
    {
        await Command(CommandIds.GridInsertAfter);await Command("resample/preview",new ResampleOptions(1920,1080,1280,720));var source=_model.ActiveEditor!.Document.Serialize();
        Assert.False(await _model.Registry.InvokeAsync("resample/apply",new(Guid.NewGuid())));_model.Draft!.Text="changed after preview";
        Assert.False(await _model.Registry.InvokeAsync("resample/apply",new()));Assert.Equal(source,_model.ActiveEditor.Document.Serialize());Assert.Equal("changed after preview",_model.Draft.Text);
        await Command("resample/invalidate");Assert.Null(_model.ResamplePreview);Assert.False(_model.Actions["resample/apply"].CanExecute(null));
    }
    [Fact] public async Task ResampleCanceledPreviewRetainsRawDraftAndCannotPublishToAnotherTab()
    {
        await Command(CommandIds.GridInsertAfter);var id=_model.ActiveDocumentId;var editor=_model.ActiveEditor!;_model.Draft!.Text="unique draft";
        await Command("resample/preview",new ResampleOptions(1920,1080,1280,720));await Command("resample/cancel");Assert.Null(_model.ResamplePreview);Assert.Equal("unique draft",_model.Draft.Text);
        await Command("resample/preview",new ResampleOptions(1920,1080,1280,720));await Command(CommandIds.SubtitleNew);var second=_model.ActiveEditor;
        Assert.False(await _model.Registry.InvokeAsync("resample/apply",new(id)));Assert.Same(second,_model.ActiveEditor);Assert.Equal("1920",editor.Document.GetScriptInfo("PlayResX"));
    }
}
