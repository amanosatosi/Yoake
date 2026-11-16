using Xunit;
using Yoake.Core.Commands;
using Yoake.Core.Subtitles;
using Yoake.UI.Services;

namespace Yoake.UI.Tests;

public sealed partial class EditorWorkflowTests
{
    public static IEnumerable<object[]> FormattingDialogChanges =>
        from command in new[] { CommandIds.FormatFont, CommandIds.FormatPrimary, CommandIds.FormatSecondary, CommandIds.FormatOutline, CommandIds.FormatShadow }
        from change in new[] { "tab", "line", "draft", "range", "revision", "cancel" }
        select new object[] { command, change };

    [Theory, MemberData(nameof(FormattingDialogChanges))]
    public async Task PendingFormattingDialogCannotRedirectOrCommitObsoleteText(string command,string change)
    {
        await Command(CommandIds.GridInsertAfter); _model.Draft!.Text = "日本語 é မြန်မာ";
        await Command(CommandIds.EditCommit);
        _model.TextSelectionStart=0; _model.TextSelectionEnd=3; _model.TextCursor=3;
        var editor=_model.ActiveEditor!;
        var result=new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _dialogs.FontDialog=async token=>(FontChoice?)await result.Task.WaitAsync(token);
        _dialogs.ColorDialog=async token=>(AssColor?)await result.Task.WaitAsync(token);
        using var cancellation=new CancellationTokenSource();
        var pending=_model.Registry.InvokeAsync(command,new(_model.ActiveDocumentId),cancellationToken:cancellation.Token);
        Assert.False(pending.IsCompleted);
        switch(change)
        {
            case "tab": await Command(CommandIds.SubtitleNew); break;
            case "line": await Command(CommandIds.GridDuplicate); break;
            case "draft": _model.Draft!.Actor="new actor"; break;
            case "range": _model.TextSelectionEnd=4; break;
            case "revision": editor.SetField(_model.SelectedEvent!,"Effect","changed while dialog open","External edit"); break;
            case "cancel": cancellation.Cancel(); break;
        }
        var originalSource=editor.Document.Serialize(); var originalUndo=editor.Undo.NextUndoName;
        var active=_model.ActiveEditor!; var source=active.Document.Serialize(); var undo=active.Undo.NextUndoName;
        var draft=_model.Draft; var text=draft?.Text; var actor=draft?.Actor;
        result.TrySetResult(command==CommandIds.FormatFont ? new FontChoice("Other font","31") : new AssColor(17,34,51,68));
        Assert.Equal(change!="cancel",await pending);
        Assert.Equal(originalSource,editor.Document.Serialize()); Assert.Equal(originalUndo,editor.Undo.NextUndoName);
        Assert.Equal(source,active.Document.Serialize()); Assert.Equal(undo,active.Undo.NextUndoName);
        Assert.Same(draft,_model.Draft); Assert.Equal(text,_model.Draft?.Text); Assert.Equal(actor,_model.Draft?.Actor);
    }

    [Theory]
    [InlineData(CommandIds.FormatFont)]
    [InlineData(CommandIds.FormatPrimary)]
    [InlineData(CommandIds.FormatSecondary)]
    [InlineData(CommandIds.FormatOutline)]
    [InlineData(CommandIds.FormatShadow)]
    public async Task FormattingDialogAppliesToItsOriginalRangeWithIndependentUndo(string command)
    {
        await Command(CommandIds.GridInsertAfter); _model.Draft!.Text=@"{\future(x)}日本語 é မြန်မာ";
        await Command(CommandIds.EditCommit);
        var original=_model.SelectedEvent!.Text; var start=original.IndexOf("日本語",StringComparison.Ordinal);
        _model.TextSelectionStart=start; _model.TextSelectionEnd=start+3; _model.TextCursor=start+3;
        _dialogs.FontDialog=_=>Task.FromResult<FontChoice?>(new("Missing subtitle font 日本語","31"));
        _dialogs.ColorDialog=_=>Task.FromResult<AssColor?>(new(17,34,51,68));
        Assert.False(await _model.Registry.InvokeAsync(command,new(Guid.NewGuid())));
        await Command(command);
        Assert.NotEqual(original,_model.SelectedEvent.Text); Assert.Contains(@"\future(x)",_model.SelectedEvent.Text);
        Assert.Contains("日本語",_model.SelectedEvent.Text); Assert.EndsWith(" é မြန်မာ",_model.SelectedEvent.Text);
        var formatted=_model.SelectedEvent.Text;
        await Command(CommandIds.EditUndo); Assert.Equal(original,_model.SelectedEvent.Text);
        await Command(CommandIds.EditRedo); Assert.Equal(formatted,_model.SelectedEvent.Text);
    }
}
