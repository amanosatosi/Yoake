using Xunit;
using Yoake.Core.Commands;
using Yoake.Core.Hotkeys;
using Yoake.Core.Subtitles;

namespace Yoake.UI.Tests;

public sealed partial class EditorWorkflowTests
{
    [Fact]
    public async Task AltCUsesMixedSelectionAndPreservesActiveCheckboxSemantics()
    {
        await Command(CommandIds.GridInsertAfter); var first = _model.SelectedEvent!;
        await Command(CommandIds.GridDuplicate); var second = _model.SelectedEvent!;
        _model.ActiveEditor!.ToggleComment([second]);
        _model.SetSelectedEvents([first, second]);
        var before = _model.ActiveEditor.Document.Serialize();
        var binding = _model.Hotkeys.Resolve(HotkeyGesture.Parse("Alt-C"), [HotkeyContext.Default, HotkeyContext.SubtitleEdit]);
        Assert.Equal(CommandIds.GridToggleComment, binding!.CommandId);
        await Command(binding.CommandId);
        Assert.True(first.IsComment); Assert.True(second.IsComment);
        Assert.Equal(new[] { first, second }, _model.SelectedEvents);
        await Command(CommandIds.EditUndo); Assert.Equal(before, _model.ActiveEditor.Document.Serialize());
        await Command(CommandIds.EditRedo); Assert.True(first.IsComment); Assert.True(second.IsComment);
        await Command(CommandIds.EditComment, false);
        Assert.False(second.IsComment); Assert.True(first.IsComment);
        Assert.Equal(new[] { first, second }, _model.SelectedEvents);
    }

    [Fact]
    public async Task MultilineFormattingSplitAndRowPositionsUseSourceOffsets()
    {
        await Command(CommandIds.GridInsertAfter); var first = _model.SelectedEvent!;
        _model.Draft!.Text = @"Hello\NWorld"; await Command(CommandIds.EditCommit);
        var projection = new AssEditorProjection(first.Text);
        _model.TextSelectionStart = projection.ToSource(6); _model.TextSelectionEnd = projection.ToSource(11); _model.TextCursor = projection.ToSource(11);
        await Command(CommandIds.FormatBold);
        Assert.Contains(@"Hello\N{\b1}World", first.Text);
        await Command(CommandIds.EditUndo); Assert.Equal(@"Hello\NWorld", first.Text);
        _model.TextCursor = _model.TextSelectionStart = _model.TextSelectionEnd = 7;
        await Command(CommandIds.GridInsertAfter); var second = _model.SelectedEvent!;
        _model.TextCursor = _model.TextSelectionStart = _model.TextSelectionEnd = 0;
        _model.SelectedEvent = first;
        Assert.Equal(7, _model.TextCursor); Assert.Equal(7, _model.TextSelectionStart);
        await Command("edit/line/split/preserve");
        Assert.Equal(@"Hello\N", first.Text); Assert.Equal("World", _model.SelectedEvent!.Text);
        Assert.Contains(second, _model.Events);
    }
}
