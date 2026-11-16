using Xunit;
using Yoake.Core.Commands;
using Yoake.Core.Settings;
using Yoake.Core.Subtitles;
using Yoake.UI.ViewModels;

namespace Yoake.UI.Tests;

public sealed partial class EditorWorkflowTests
{
    [Fact]
    public async Task PostprocessorPreviewAppliesAndPersistsWithoutLosingUnicodeDraft()
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Start="0:00:01.00";_model.Draft.End="0:00:02.00";_model.Draft.Text=@"{\future(x)}日本語";await Command(CommandIds.EditCommit);
        var editor=_model.ActiveEditor!;var before=editor.Document.Serialize();var history=editor.Undo.NextUndoName;
        _model.Draft!.Text=@"{\future(x)}မြန်မာ";var draft=_model.Draft;
        var options=new SubtitlePostprocessOptions { MakeAdjacent=false,AddLeadIn=true,LeadIn=90,AddLeadOut=true,LeadOut=110 };
        var request=new SubtitlePostprocessToolRequest(_model.ActiveDocumentId!.Value,options,true);
        await Command("tool/time/postprocess/apply",request);Assert.Equal(before,editor.Document.Serialize());Assert.Same(draft,_model.Draft);Assert.Equal(history,editor.Undo.NextUndoName);
        await Command("tool/time/postprocess/apply",request with { PreviewOnly=false });
        Assert.Equal(910,_model.SelectedEvent!.StartMilliseconds);Assert.Equal(2110,_model.SelectedEvent.EndMilliseconds);Assert.Equal(@"{\future(x)}မြန်မာ",_model.SelectedEvent.Text);
        Assert.Equal(90,new SettingsStore(Path.Combine(_root,"settings.json")).Load().PostprocessPreferences!.LeadIn);
        await Command(CommandIds.EditUndo);Assert.Equal(before,editor.Document.Serialize());
    }
    [Fact]
    public async Task PostprocessorRejectsForeignOwnersAndPreservesDraftsOutsideStyleScope()
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="original";await Command(CommandIds.EditCommit);
        var editor=_model.ActiveEditor!;var source=editor.Document.Serialize();_model.Draft!.Text="pending မြန်မာ";var draft=_model.Draft;
        var request=new SubtitlePostprocessToolRequest(Guid.NewGuid(),new() { AddLeadIn=true });
        Assert.False(await _model.Registry.InvokeAsync("tool/time/postprocess/apply",new(),request));
        request=request with { Owner=_model.ActiveDocumentId!.Value,Options=new() { Styles=[],AddLeadIn=true } };
        await Command("tool/time/postprocess/apply",request);Assert.Equal(source,editor.Document.Serialize());Assert.Same(draft,_model.Draft);Assert.Equal("pending မြန်မာ",draft.Text);
    }
}
