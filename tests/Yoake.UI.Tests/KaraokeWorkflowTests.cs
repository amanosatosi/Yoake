using Xunit;
using Yoake.Core.Subtitles;
using Yoake.Core.Commands;

namespace Yoake.UI.Tests;

public sealed partial class EditorWorkflowTests
{
    [Fact] public async Task KaraokeRequiresAudioAndRejectsForeignContextWithoutChangingDraft()
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="<病|やまい>";var draft=_model.Draft;var source=_model.ActiveEditor!.Document.Serialize();
        Assert.False(_model.Actions["audio/karaoke"].CanExecute(null));Assert.False(await _model.Registry.InvokeAsync("audio/karaoke",new(Guid.NewGuid())));
        foreach(var id in new[]{"karaoke/commit","karaoke/split","karaoke/join","karaoke/tag","karaoke/pointer/begin"})Assert.False(await _model.Registry.InvokeAsync(id,new()));
        Assert.False(_model.IsKaraokeMode);Assert.Same(draft,_model.Draft);Assert.Equal(source,_model.ActiveEditor.Document.Serialize());
    }
    [Fact] public async Task RecoveredTimingPlanRetainsInvalidDraftOriginalAndIdentityWithoutMedia()
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="original 日本語";await Command(CommandIds.EditCommit);var original=_model.ActiveEditor;var firstId=_model.ActiveDocumentId;
        var doc=AssDocument.Parse("[Events]\nFormat: "+string.Join(',',AssDocument.EventFormat)+"\nDialogue: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,,<病|やまい>\n");
        var plan=new ToshikiTimingPlan(doc.Events[0].Text,0,1000);plan.Append(300);plan.SetTag("ko");var saved=plan.Capture(0);
        var store=new SubtitleRecoveryStore(_model.RecoveryDirectory);var lease=new RecoveryLease();
        var draft=new RecoveryDraft{Row=0,Values=new(){{"Text",doc.Events[0].Text},{"Start","unfinished"}},OriginalText="stable original"};
        var path=await store.WriteAsync(doc.CaptureSnapshot(),new(){DocumentId=Guid.NewGuid(),Sequence=1,Timestamp=DateTimeOffset.UtcNow,Title="Toshiki",SelectedRow=0,Karaoke=[saved],Draft=draft},lease,lease.Renew());
        await Command("recovery/open-version",path);var recoveredId=_model.ActiveDocumentId;
        Assert.NotEqual(firstId,recoveredId);Assert.True(_model.IsKaraokeMode);Assert.False(_model.ShowKaraoke);Assert.Equal(plan.PreviewText,_model.KaraokePlan!.PreviewText);
        Assert.Equal("unfinished",_model.Draft!.Start);Assert.Equal("stable original",_model.OriginalText);Assert.False(_model.Actions["karaoke/commit"].CanExecute(null));
        Assert.True(_workspace.Activate(firstId!.Value));Assert.Same(original,_model.ActiveEditor);Assert.False(_model.IsKaraokeMode);
        Assert.True(_workspace.Activate(recoveredId!.Value));Assert.Equal(plan.PreviewText,_model.KaraokePlan!.PreviewText);Assert.Equal("unfinished",_model.Draft!.Start);
        _model.ActiveEditor!.SetField(_model.SelectedEvent!,"Text","changed externally","External edit");
        Assert.Equal(plan.PreviewText,_model.KaraokePlan!.PreviewText);Assert.Contains("retained",_model.KaraokeStatus);Assert.Equal("unfinished",_model.Draft!.Start);
        await _model.RunAutosaveAsync();var versions=await _model.ListRecoveryAsync();Assert.Contains(versions,e=>e.Metadata?.DocumentId==recoveredId&&e.Metadata.Karaoke?.Length==1&&e.Metadata.Karaoke[0].StoredValues?.GetValueOrDefault("Text")==doc.Events[0].Text);
    }
    [Fact] public async Task RecoveryRejectsForeignKaraokeRowsAndPreservesTheOpenDocument()
    {
        await Command(CommandIds.GridInsertAfter);var editor=_model.ActiveEditor;var document=editor!.Document;var plan=new ToshikiTimingPlan("あい",0,1000);
        var lease=new RecoveryLease();var store=new SubtitleRecoveryStore(_model.RecoveryDirectory);
        await Assert.ThrowsAsync<InvalidDataException>(()=>store.WriteAsync(document.CaptureSnapshot(),new(){DocumentId=Guid.NewGuid(),Sequence=1,Timestamp=DateTimeOffset.UtcNow,Karaoke=[plan.Capture(999)]},lease,lease.Renew()));
        Assert.Same(editor,_model.ActiveEditor);Assert.False(_model.IsKaraokeMode);
    }
}
