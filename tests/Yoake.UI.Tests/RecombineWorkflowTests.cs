using Xunit;
using Yoake.Core.Commands;
using Yoake.UI.ViewModels;

namespace Yoake.UI.Tests;

public sealed partial class EditorWorkflowTests
{
    [Fact]
    public async Task RecombineUsesTheDocumentSelectionAndRetainsActiveLineAndUndo()
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="日本語";await Command(CommandIds.EditCommit);
        var editor=_model.ActiveEditor!;var first=_model.SelectedEvent!;var combined=editor.Insert(first,true);combined.Text=@"日本語\Nမြန်မာ";var last=editor.Insert(combined,true);last.Text="မြန်မာ";
        _model.SetSelectedEvents([first,combined,last]);var before=editor.Document.Serialize();
        Assert.False(await _model.Registry.InvokeAsync("edit/line/recombine",new(Guid.NewGuid())));
        await Command("edit/line/recombine");Assert.Equal(2,_model.SelectedEvents.Count);Assert.Same(first,_model.SelectedEvent);Assert.DoesNotContain(combined,_model.Events);
        await Command(CommandIds.EditUndo);Assert.Equal(before,editor.Document.Serialize());
    }
}
