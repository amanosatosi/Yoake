using Xunit;
using Yoake.Core.Commands;
using Yoake.Core.Subtitles;

namespace Yoake.UI.Tests;

public sealed partial class EditorWorkflowTests
{
    [Fact] public async Task InvalidInlineInsertionDoesNotCommitAnUnappliedDraft()
    {
        await Command(CommandIds.GridInsertAfter);
        var source=_model.ActiveEditor!.Document.Serialize();var undo=_model.ActiveEditor.Undo.NextUndoName;
        _model.Draft!.Text=@"{\future(unfinished";var draft=_model.Draft;
        _model.TextSelectionStart=_model.TextSelectionEnd=5;
        Assert.False(await _model.Registry.InvokeAsync("text/insert/brackets/corner",new()));
        Assert.Equal(source,_model.ActiveEditor.Document.Serialize());Assert.Equal(undo,_model.ActiveEditor.Undo.NextUndoName);Assert.Same(draft,_model.Draft);
        _model.Draft.Text="日本語";_model.TextSelectionStart=0;_model.TextSelectionEnd=3;
        _dialogs.FuriganaDialog=_=>Task.FromResult<FuriganaReading?>(new(""));
        Assert.False(await _model.Registry.InvokeAsync("text/insert/furigana",new()));
        Assert.Equal(source,_model.ActiveEditor.Document.Serialize());Assert.Equal(undo,_model.ActiveEditor.Undo.NextUndoName);Assert.Same(draft,_model.Draft);
    }

    [Fact] public async Task JapaneseBracketsRetainRawSourceAndHaveOneUndoWithSelectionRestoration()
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text=@"{\future(x)}日本語 မြန်မာ é";await Command(CommandIds.EditCommit);
        var original=_model.SelectedEvent!.Text;var start=original.IndexOf("日本語",StringComparison.Ordinal);
        _model.TextSelectionStart=start;_model.TextSelectionEnd=start+3;_model.TextCursor=start+3;
        const string command="text/insert/brackets/double-corner";
        Assert.False(await _model.Registry.InvokeAsync(command,new(Guid.NewGuid())));
        await Command(command);Assert.Equal(original[..start]+"『日本語』 မြန်မာ é",_model.SelectedEvent.Text);
        Assert.Equal("日本語",_model.Draft!.Text[_model.TextSelectionStart.._model.TextSelectionEnd]);
        await Command(CommandIds.EditUndo);Assert.Equal(original,_model.SelectedEvent.Text);
        Assert.Equal(start,_model.TextSelectionStart);Assert.Equal(start+3,_model.TextSelectionEnd);
        await Command(CommandIds.EditRedo);Assert.Contains("『日本語』",_model.SelectedEvent.Text);
    }
    [Theory]
    [InlineData("apply")]
    [InlineData("cancel")]
    [InlineData("tab")]
    [InlineData("draft")]
    public async Task FuriganaDialogUsesOwnedSelectionAndPreservesTimingAndUnicode(string action)
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text=@"{\k30\future(x)}日本語 မြန်မာ é";await Command(CommandIds.EditCommit);
        var editor=_model.ActiveEditor!;var original=_model.SelectedEvent!.Text;var start=original.IndexOf("日本語",StringComparison.Ordinal);
        Assert.False(_model.Actions["text/insert/furigana"].CanExecute(null));
        _model.TextSelectionStart=start;_model.TextSelectionEnd=start+3;_model.TextCursor=start+3;
        var answer=new TaskCompletionSource<FuriganaReading?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _dialogs.FuriganaDialog=token=>answer.Task.WaitAsync(token);
        using var cancellation=new CancellationTokenSource();
        var pending=_model.Registry.InvokeAsync("text/insert/furigana",new(_model.ActiveDocumentId),cancellationToken:cancellation.Token);
        Assert.False(pending.IsCompleted);
        if(action=="tab")await Command(CommandIds.SubtitleNew);
        if(action=="draft")_model.Draft!.Actor="new actor";
        if(action=="cancel")cancellation.Cancel();
        var active=_model.ActiveEditor!;var before=active.Document.Serialize();var undo=active.Undo.NextUndoName;var draft=_model.Draft;
        answer.TrySetResult(new("にほんご","Japanese"));
        Assert.Equal(action!="cancel",await pending);
        if(action=="apply")
        {
            Assert.Equal(original[..start]+"<日本語|にほんご|Japanese> မြန်မာ é",_model.SelectedEvent!.Text);
            await Command(CommandIds.EditUndo);Assert.Equal(original,_model.SelectedEvent.Text);
            await Command(CommandIds.EditRedo);Assert.Contains("<日本語|にほんご|Japanese>",_model.SelectedEvent.Text);
        }
        else
        {
            Assert.Equal(before,active.Document.Serialize());Assert.Equal(undo,active.Undo.NextUndoName);Assert.Same(draft,_model.Draft);
            Assert.Equal(original,editor.Document.Events.Last().Text);
        }
    }
}
