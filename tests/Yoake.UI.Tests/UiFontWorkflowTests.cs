using Xunit;
using Yoake.Core.Commands;
using Yoake.Core.Settings;
using Yoake.UI.ViewModels;

namespace Yoake.UI.Tests;

public sealed partial class EditorWorkflowTests
{
    [Fact]
    public async Task UiFontPreferenceDoesNotModifySubtitleOrUndoHistory()
    {
        await Command(CommandIds.GridInsertAfter);
        _model.Draft!.Text="日本語 မြန်မာ {\\fnSubtitle Font}text";
        await Command(CommandIds.EditCommit);
        var editor=_model.ActiveEditor!;
        var source=editor.Document.Serialize();
        var history=editor.Undo.NextUndoName;
        await Command("settings/ui-font",new UiFontOptions("Noto Sans Myanmar",15));
        Assert.Equal(source,editor.Document.Serialize());
        Assert.Equal(history,editor.Undo.NextUndoName);
        var saved=new SettingsStore(Path.Combine(_root,"settings.json")).Load();
        Assert.Equal("Noto Sans Myanmar",saved.UiFontFamily);
        Assert.Equal(15,saved.UiFontSize);
        Assert.False(await _model.Registry.InvokeAsync("settings/ui-font",new(),new UiFontOptions(null,100)));
        Assert.Equal("Noto Sans Myanmar",_model.UiFontFamily);
        Assert.Equal(source,editor.Document.Serialize());
        // A font preference is independent of the full preview document.
        await Command("settings/ui-font",new UiFontOptions("Pyidaungsu",12));
        _model.Draft!.Text="miriam-dono မြန်မာစာ";
        var preview=_model.PreviewSource()!;
        Assert.Contains("miriam-dono မြန်မာစာ",preview);
        Assert.Equal(editor.Document.Events.Count,Yoake.Core.Subtitles.AssDocument.Parse(preview).Events.Count);
        Assert.Equal(source,editor.Document.Serialize());
        await Command("settings/ui-font",new UiFontOptions(null,null));
        Assert.Equal(preview,_model.PreviewSource());
    }
}
