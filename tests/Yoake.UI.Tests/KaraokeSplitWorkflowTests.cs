using Xunit;
using Yoake.Core.Commands;

namespace Yoake.UI.Tests;

public sealed partial class EditorWorkflowTests
{
    [Fact]
    public async Task KaraokeSplitCommitsDraftSelectsResultsAndUndoesAsOneEdit()
    {
        await Command(CommandIds.GridInsertAfter);
        _model.Draft!.Text=@"{\k20}日{\kf30}本";
        _model.Draft.Start="0:00:01.00";_model.Draft.End="0:00:02.00";
        Assert.False(await _model.Registry.InvokeAsync("edit/line/split/by_karaoke",new(Guid.NewGuid())));
        await Command("edit/line/split/by_karaoke");
        Assert.Equal(2,_model.SelectedEvents.Count);
        Assert.Contains(@"\k20",_model.SelectedEvents[0].Text);
        Assert.Contains(@"\kf30",_model.SelectedEvents[1].Text);
        await Command(CommandIds.EditUndo);
        Assert.Contains(_model.Events,line=>line.Text==@"{\k20}日{\kf30}本");
    }
}
