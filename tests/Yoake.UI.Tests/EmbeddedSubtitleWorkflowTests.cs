using Xunit;
using Yoake.Core.Commands;
using Yoake.Core.Subtitles;
using Yoake.UI.ViewModels;

namespace Yoake.UI.Tests;

public sealed partial class EditorWorkflowTests
{
    [Fact]
    public async Task EmbeddedSubtitleImportPreservesExistingInvalidDraftAndHasIndependentSaveDestination()
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="existing 日本語";await Command(CommandIds.EditCommit);
        var originalId=_model.ActiveDocumentId!.Value;var editor=_model.ActiveEditor!;var before=editor.Document.Serialize();var undo=editor.Undo.NextUndoName;
        _model.Draft!.Start="unfinished";var draft=_model.Draft;
        var fixture=Path.Combine(AppContext.BaseDirectory,"Fixtures","Matroska","episode.mkv");var bytes=File.ReadAllBytes(fixture);
        await Command("subtitle/open/video",new OpenEmbeddedSubtitlesRequest(fixture,2));
        Assert.NotEqual(originalId,_model.ActiveDocumentId);Assert.Null(_workspace.ActiveDocument!.Path);Assert.True(_workspace.ActiveDocument.IsDirty);
        Assert.Equal(2,_model.Events.Count);Assert.False(_model.ActiveEditor!.Undo.CanUndo);Assert.Null(_model.VideoSourcePath);Assert.Equal(before,editor.Document.Serialize());
        _model.Draft!.Text+=" မြန်မာ";await Command(CommandIds.EditCommit);await Command(CommandIds.EditUndo);Assert.True(_workspace.ActiveDocument.IsDirty);
        var importedId=_model.ActiveDocumentId!.Value;var output=Path.Combine(_root,"extracted.ass");Assert.True(_model.SaveActiveSubtitle(output));Assert.False(_workspace.ActiveDocument.IsDirty);
        Assert.Equal(_model.ActiveEditor.Document.Serialize(),AssDocument.Load(output).Serialize());Assert.Equal(bytes,File.ReadAllBytes(fixture));
        _workspace.Activate(originalId);Assert.Same(editor,_model.ActiveEditor);Assert.Same(draft,_model.Draft);Assert.Equal("unfinished",_model.Draft!.Start);Assert.Equal(undo,editor.Undo.NextUndoName);
        _workspace.Activate(importedId);Assert.Equal(output,_workspace.ActiveDocument!.Path);
    }
    [Fact]
    public async Task InvalidContainerOrTrackDoesNotReplaceExistingDocumentOrDraft()
    {
        var id=_model.ActiveDocumentId;var editor=_model.ActiveEditor;var draft=_model.Draft;var count=_workspace.Documents.Count;
        var fixture=Path.Combine(AppContext.BaseDirectory,"Fixtures","Matroska","episode.mkv");
        Assert.False(await _model.Registry.InvokeAsync("subtitle/open/video",new(id),new OpenEmbeddedSubtitlesRequest(fixture,999)));
        Assert.Equal(id,_model.ActiveDocumentId);Assert.Same(editor,_model.ActiveEditor);Assert.Same(draft,_model.Draft);Assert.Equal(count,_workspace.Documents.Count);Assert.False(_model.OpeningSubtitle);
    }
}
