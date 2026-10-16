using Xunit;
using Yoake.Core.Commands;
using Yoake.Core.Subtitles;
using Yoake.UI.ViewModels;

namespace Yoake.UI.Tests;

public sealed partial class EditorWorkflowTests
{
    [Fact] public async Task AttachmentCommandsPreserveInvalidDraftOriginalSelectionAndOneUndoHistory()
    {
        await Command(CommandIds.GridInsertAfter);var line=_model.SelectedEvent;var draft=_model.Draft!;draft.Text="pending 日本語\ntext";draft.Start="incomplete";
        var source=_model.ActiveEditor!.Document.Serialize();var original=_model.OriginalText;var selection=_model.SelectedEvents.ToArray();
        var file=Path.Combine(_root,"fixture.bmp");File.WriteAllBytes(file,[0,1,128,255]);
        await Command("attachments/import",new AttachmentImportRequest(AssAttachmentKind.Graphic,[file]));
        Assert.Same(draft,_model.Draft);Assert.Equal("incomplete",draft.Start);Assert.Equal(original,_model.OriginalText);Assert.Equal(selection,_model.SelectedEvents);Assert.Same(line,_model.SelectedEvent);
        var imported=_model.ActiveEditor.Document.Serialize();Assert.Single(await _model.ListAttachmentsAsync(_model.ActiveDocumentId!.Value));
        await Command("attachments/remove",await _model.ListAttachmentsAsync(_model.ActiveDocumentId.Value));Assert.Empty(await _model.ListAttachmentsAsync(_model.ActiveDocumentId.Value));
        await Command("attachments/undo");Assert.Equal(imported,_model.ActiveEditor.Document.Serialize());Assert.Same(draft,_model.Draft);
        await Command("attachments/redo");Assert.Empty(await _model.ListAttachmentsAsync(_model.ActiveDocumentId.Value));Assert.Same(draft,_model.Draft);
        await Command("attachments/undo");await Command("attachments/undo");Assert.Equal(source,_model.ActiveEditor.Document.Serialize());Assert.Same(draft,_model.Draft);Assert.True(_workspace.ActiveDocument!.IsDirty);
    }
    [Fact] public async Task AttachmentImportsRejectDuplicatesFailuresCancellationAndForeignDocumentsAtomically()
    {
        var file=Path.Combine(_root,"fixture.bmp");File.WriteAllBytes(file,[1,2,3]);await Command("attachments/import",new AttachmentImportRequest(AssAttachmentKind.Graphic,[file]));
        var editor=_model.ActiveEditor!;var source=editor.Document.Serialize();var history=editor.Undo.NextUndoName;
        Assert.False(await _model.Registry.InvokeAsync("attachments/import",new(),new AttachmentImportRequest(AssAttachmentKind.Graphic,[file])));Assert.Equal(source,editor.Document.Serialize());Assert.Equal(history,editor.Undo.NextUndoName);
        Assert.False(await _model.Registry.InvokeAsync("attachments/import",new(),new AttachmentImportRequest(AssAttachmentKind.Graphic,[file,Path.Combine(_root,"missing.bmp")])));Assert.Equal(source,editor.Document.Serialize());
        Assert.False(await _model.Registry.InvokeAsync("attachments/import",new(Guid.NewGuid()),new AttachmentImportRequest(AssAttachmentKind.Graphic,[file])));
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();Assert.False(await _model.Registry.InvokeAsync("attachments/import",new(),new AttachmentImportRequest(AssAttachmentKind.Graphic,[file]),cancellation.Token));Assert.False(_model.AttachmentBusy);
    }
    [Fact] public async Task AttachmentRowsCannotMutateOtherTabsAndExtractionCannotDirtySourceOrHistory()
    {
        var file=Path.Combine(_root,"fixture.bmp");File.WriteAllBytes(file,[0,1,2]);await Command("attachments/import",new AttachmentImportRequest(AssAttachmentKind.Graphic,[file]));
        var first=_model.ActiveDocumentId;var attachments=await _model.ListAttachmentsAsync(first!.Value);var editor=_model.ActiveEditor!;var source=editor.Document.Serialize();var history=editor.Undo.NextUndoName;
        var directory=Path.Combine(_root,"output");Directory.CreateDirectory(directory);await Command("attachments/extract",new AttachmentExtractionRequest(attachments,directory));Assert.Equal(File.ReadAllBytes(file),File.ReadAllBytes(Path.Combine(directory,"fixture.bmp")));
        Assert.Equal(source,editor.Document.Serialize());Assert.Equal(history,editor.Undo.NextUndoName);
        await Command(CommandIds.SubtitleNew);var other=_model.ActiveEditor!;Assert.False(await _model.Registry.InvokeAsync("attachments/remove",new(),attachments));Assert.False(await _model.Registry.InvokeAsync("attachments/remove",new(first),attachments));Assert.Empty(other.Document.GetAttachments());Assert.Equal(source,editor.Document.Serialize());
    }
    [Fact] public async Task ImportedPayloadAndPendingDraftRecoverTogetherWithoutPhantomExtractedAttachments()
    {
        await Command(CommandIds.GridInsertAfter);var file=Path.Combine(_root,"fixture.bmp");var bytes=new byte[]{5,9,128,255};File.WriteAllBytes(file,bytes);
        _model.Draft!.Text="uncommitted";_model.Draft.Start="invalid";await Command("attachments/import",new AttachmentImportRequest(AssAttachmentKind.Graphic,[file]));
        await _model.RunAutosaveAsync();var entry=(await _model.ListRecoveryAsync()).Single();await Command("recovery/open-version",entry.Path);
        Assert.Equal("uncommitted",_model.Draft!.Text);Assert.Equal("invalid",_model.Draft.Start);Assert.Equal(bytes,Assert.Single(await _model.ListAttachmentsAsync(_model.ActiveDocumentId!.Value)).Decode());
        var output=Path.Combine(_root,"extracted");Directory.CreateDirectory(output);var rows=await _model.ListAttachmentsAsync(_model.ActiveDocumentId.Value);await Command("attachments/extract",new AttachmentExtractionRequest(rows,output));
        Assert.Single(await _model.ListAttachmentsAsync(_model.ActiveDocumentId.Value));Assert.Equal(bytes,File.ReadAllBytes(Path.Combine(output,"fixture.bmp")));
    }
}
