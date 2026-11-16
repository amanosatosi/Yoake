using Xunit;
using Yoake.Core.Commands;
using Yoake.Core.Subtitles;
using Yoake.UI.ViewModels;

namespace Yoake.UI.Tests;

public sealed partial class EditorWorkflowTests
{
    [Fact]
    public async Task TranslationAssistantCommitsBlocksNavigatesAndUndoesWithoutChangingTags()
    {
        await Command(CommandIds.GridInsertAfter);
        _model.Draft!.Text=@"{\future(opaque)\bord2}日本語{\i1}二番";await Command(CommandIds.EditCommit);
        var first=_model.SelectedEvent!;var editor=_model.ActiveEditor!;
        var second=editor.Insert(first,true);second.Text="အဆုံး";var before=editor.Document.Serialize();
        using var session=_model.BeginAssistant(EditorAssistantMode.Translation);
        Assert.Equal("日本語",session.Original);session.Input="မြန်မာ\nစာ";
        await Command("tool/translation_assistant/commit",session);
        Assert.Equal(@"{\future(opaque)\bord2}မြန်မာ\Nစာ{\i1}二番",first.Text);
        Assert.Same(first,_model.SelectedEvent);Assert.Equal("二番",session.Original);
        session.Input="ဒုတိယ";await Command("tool/translation_assistant/commit",session);
        Assert.Same(second,_model.SelectedEvent);Assert.Equal("အဆုံး",session.Original);
        await Command("tool/translation_assistant/prev",session);Assert.Same(first,_model.SelectedEvent);Assert.Equal("ဒုတိယ",session.Original);
        await Command(CommandIds.EditUndo);Assert.Contains("二番",first.Text);
        Assert.False(await _model.Registry.InvokeAsync("tool/translation_assistant/commit",new(),session));
        session.Reload();await Command(CommandIds.EditUndo);Assert.Equal(before,editor.Document.Serialize());
    }
    [Fact]
    public async Task TranslationInsertOriginalUsesTheSelectionAndProtectsCombiningCharacters()
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="日本語";await Command(CommandIds.EditCommit);
        using var session=_model.BeginAssistant(EditorAssistantMode.Translation);
        session.Input="prefix á suffix";session.InputSelectionStart=8;session.InputSelectionEnd=9;
        await Command("tool/translation_assistant/insert_original",session);
        Assert.Equal("prefix 日本語 suffix",session.Input);Assert.Equal(10,session.InputSelectionEnd);
        Assert.Equal("日本語",_model.SelectedEvent!.Text);
    }
    [Fact]
    public async Task EmptyTranslationDoesNotSkipTheNextBlock()
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text=@"one{\i1}two{\i0}three";await Command(CommandIds.EditCommit);
        using var session=_model.BeginAssistant(EditorAssistantMode.Translation);session.Input="";
        await Command("tool/translation_assistant/commit",session);
        Assert.Equal("two",session.Original);Assert.Equal(@"{\i1}two{\i0}three",_model.SelectedEvent!.Text);
    }
    [Fact]
    public async Task AssistantsRejectForeignTabsAndUncommittedMainEditorChanges()
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="日本語";await Command(CommandIds.EditCommit);
        using var session=_model.BeginAssistant(EditorAssistantMode.Translation);session.Input="မြန်မာ";
        _model.Draft!.Start="unfinished";var before=_model.ActiveEditor!.Document.Serialize();
        Assert.False(await _model.Registry.InvokeAsync("tool/translation_assistant/preview",new(),session));
        Assert.Equal("unfinished",_model.Draft.Start);Assert.Equal(before,_model.ActiveEditor.Document.Serialize());
        _model.Draft.Start=_model.SelectedEvent!.Start;await Command(CommandIds.SubtitleNew);
        var other=_model.ActiveEditor!.Document.Serialize();
        Assert.False(await _model.Registry.InvokeAsync("tool/translation_assistant/preview",new(),session));Assert.Equal(other,_model.ActiveEditor.Document.Serialize());
    }
    [Fact]
    public async Task StylingAssistantValidatesStylesCommitsAndAdvancesWithOneStepUndo()
    {
        await Command(CommandIds.GridInsertAfter);var editor=_model.ActiveEditor!;var first=_model.SelectedEvent!;
        var style=editor.Document.Styles[0].Clone();style.Name="Burmese";editor.AddStyle(style);
        var second=editor.Insert(first,true);var before=editor.Document.Serialize();
        using var session=_model.BeginAssistant(EditorAssistantMode.Styling);
        session.Input="Missing";Assert.False(await _model.Registry.InvokeAsync("tool/styling_assistant/commit",new(),session));
        Assert.Equal(before,editor.Document.Serialize());Assert.Same(first,_model.SelectedEvent);
        session.Input="Burmese";await Command("tool/styling_assistant/commit",session);
        Assert.Equal("Burmese",first.Style);Assert.Same(second,_model.SelectedEvent);
        await Command(CommandIds.EditUndo);Assert.Equal(before,editor.Document.Serialize());
    }
}
