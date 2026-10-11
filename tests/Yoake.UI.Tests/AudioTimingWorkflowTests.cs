using Xunit;
using Yoake.Core.Audio;
using Yoake.Core.Commands;
using Yoake.Core.Settings;
using Yoake.Core.Subtitles;
using Yoake.UI.ViewModels;

namespace Yoake.UI.Tests;

public sealed partial class EditorWorkflowTests
{
    private async Task<AssEvent[]> TimingRows(bool zeroSecond=false)
    {
        var path=Path.Combine(_root,"audio-timing.ass");
        File.WriteAllText(path,"[Events]\nFormat: "+string.Join(',',AssDocument.EventFormat)+"\n"+
            "Dialogue: 0,0:00:00.30,0:00:01.40,Default,A,0001,0002,0003,fx,{\\future(x)\\distort(1,2,3,4,5,6,7,8)}日本語 é\n"+
            (zeroSecond?"Comment: 0,0:00:00.00,0:00:00.00":"Comment: 0,0:00:02.00,0:00:03.00")+",Default,B,0,0,0,,မြန်မာ\n");
        Assert.True(_model.OpenSubtitle(path));await Task.CompletedTask;return _model.Events.ToArray();
    }
    [Fact] public async Task LeadAndTenMillisecondCommandsChangeOnlyActivePendingMarkersUntilCommit()
    {
        var rows=await TimingRows();var editor=_model.ActiveEditor!;var source=editor.Document.Serialize();_model.SetSelectedEvents(rows);
        await Command("time/lead/both");Assert.Equal(new AudioClipInterval(200,1750),_model.PrimaryAudioRange);Assert.True(_model.HasPendingAudioTiming);Assert.Equal(source,editor.Document.Serialize());Assert.False(editor.Undo.CanUndo);
        await Command("time/start/decrease");await Command("time/start/increase");await Command("time/length/decrease");await Command("time/length/increase/shift");Assert.Equal(new AudioClipInterval(200,1750),_model.PrimaryAudioRange);
        var draft=_model.Draft;await Command("audio/commit/stay");Assert.Same(rows[0],_model.SelectedEvent);Assert.Same(draft,_model.Draft);
        Assert.Equal(200,rows[0].StartMilliseconds);Assert.Equal(1750,rows[0].EndMilliseconds);Assert.Equal(2000,rows[1].StartMilliseconds);Assert.Equal("0001",rows[0].MarginL);Assert.Contains("\\future(x)\\distort",rows[0].Text);
        Assert.False(_model.HasPendingAudioTiming);await Command(CommandIds.EditUndo);Assert.Equal(source,editor.Document.Serialize());await Command(CommandIds.EditRedo);Assert.Equal(1750,rows[0].EndMilliseconds);
    }
    [Theory] [InlineData("time/lead/in",200,1400)] [InlineData("time/lead/out",300,1750)]
    [InlineData("time/start/decrease",290,1400)] [InlineData("time/start/increase",310,1400)]
    [InlineData("time/length/decrease",300,1390)] [InlineData("time/length/increase",300,1410)]
    [InlineData("time/length/decrease/shift",300,1390)] [InlineData("time/length/increase/shift",300,1410)]
    public async Task TimingCommandsHaveExactDialogueDefaults(string command,long start,long end)
    {await TimingRows();await Command(command);Assert.Equal(new AudioClipInterval(start,end),_model.PrimaryAudioRange);Assert.False(_model.ActiveEditor!.Undo.CanUndo);}
    [Fact] public async Task NavigationDoesNotCommitPreservesMarkersAndHandlesFirstLastEmptyAndInvalidDraft()
    {
        var rows=await TimingRows();var source=_model.ActiveEditor!.Document.Serialize();await Command("time/prev");Assert.Same(rows[0],_model.SelectedEvent);
        await Command("time/lead/out");await Command("time/next");Assert.Same(rows[1],_model.SelectedEvent);Assert.Equal(new AudioClipInterval(2000,3000),_model.PrimaryAudioRange);
        await Command("time/next");Assert.Equal(2,_model.Events.Count);await Command("time/prev");Assert.Equal(new AudioClipInterval(300,1750),_model.PrimaryAudioRange);Assert.Equal(source,_model.ActiveEditor.Document.Serialize());Assert.False(_model.ActiveEditor.Undo.CanUndo);
        _model.Draft!.Start="unfinished";var draft=_model.Draft;await Command("time/next");Assert.Same(rows[0],_model.SelectedEvent);Assert.Same(draft,_model.Draft);Assert.False(await _model.Registry.InvokeAsync("audio/commit",new()));
        _model.Draft.Start=rows[0].Start;_model.SelectedEvent=null;_model.SetSelectedEvents([]);Assert.False(_model.Actions["audio/commit"].CanExecute(null));await Command("time/next");Assert.Same(rows[0],_model.SelectedEvent);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task DefaultNextStagesExistingNormalOrZeroLineWithoutChangingStoredTimes(bool zero)
    {
        var rows=await TimingRows(zero);await Command("time/lead/out");await Command("audio/opt/autocommit");
        await Command("audio/commit/default");Assert.Same(rows[1],_model.SelectedEvent);Assert.Equal(1750,rows[0].EndMilliseconds);
        Assert.Equal(zero?0:3000,rows[1].EndMilliseconds);Assert.Equal(new AudioClipInterval(1750,4750),_model.PrimaryAudioRange);Assert.True(_model.HasPendingAudioTiming);
        await Command("audio/commit/stay");Assert.Equal(1750,rows[1].StartMilliseconds);Assert.Equal(4750,rows[1].EndMilliseconds);
    }
    [Fact] public async Task ExplicitNextResetsZeroLineAndLastLineCreatesStoredSuccessor()
    {
        var rows=await TimingRows(true);await Command("audio/opt/autonext");Assert.False(_model.AudioAutoNext);
        await Command("audio/commit/next");Assert.Same(rows[1],_model.SelectedEvent);Assert.Equal(new AudioClipInterval(1400,4400),_model.PrimaryAudioRange);
        await Command("audio/commit/next");Assert.Equal(3,_model.Events.Count);Assert.Equal(4400,_model.SelectedEvent!.StartMilliseconds);Assert.Equal(7400,_model.SelectedEvent.EndMilliseconds);Assert.False(_model.HasPendingAudioTiming);
    }
    [Fact] public async Task AutoCommitDoesNotNavigateAndBareCommitHonorsAutoNextWhileStayOverridesIt()
    {
        var rows=await TimingRows();await Command("audio/opt/autocommit");Assert.True(_model.Actions["audio/opt/autocommit"].IsChecked);Assert.True(_model.Actions["audio/opt/autonext"].IsChecked);
        await Command("time/lead/out");Assert.Equal(1750,rows[0].EndMilliseconds);Assert.Same(rows[0],_model.SelectedEvent);Assert.False(_model.HasPendingAudioTiming);
        await Command("audio/commit/stay");Assert.Same(rows[0],_model.SelectedEvent);await Command("audio/commit");Assert.Same(rows[1],_model.SelectedEvent);
        await Command("audio/opt/autonext");await Command("audio/commit");Assert.Same(rows[1],_model.SelectedEvent);Assert.Equal(2,_model.Events.Count);
    }
    [Fact] public async Task PendingGestureRetainsDraftRollsBackToPriorMarkersAndAutoCommitIsOneUndo()
    {
        var rows=await TimingRows();await Command("time/lead/in");_model.Draft!.Text+=" draft 👩‍👩‍👧";var draft=_model.Draft;var source=_model.ActiveEditor!.Document.Serialize();
        Assert.True(_model.BeginAudioTimingGesture());_model.UpdateAudioTimingGesture(1,.2);Assert.Equal(new AudioClipInterval(200,1600),_model.PrimaryAudioRange);
        _model.CancelGesture();Assert.Same(draft,_model.Draft);Assert.Equal(new AudioClipInterval(200,1400),_model.PrimaryAudioRange);Assert.Equal(source,_model.ActiveEditor.Document.Serialize());Assert.False(_model.ActiveEditor.Undo.CanUndo);
        Assert.True(_model.BeginAudioTimingGesture());_model.UpdateAudioTimingGesture(1,.2);_model.EndAudioTimingGesture();Assert.True(_model.HasPendingAudioTiming);Assert.False(_model.ActiveEditor.Undo.CanUndo);
        await Command("audio/opt/autocommit");Assert.True(_model.BeginAudioTimingGesture());_model.UpdateAudioTimingGesture(1,.2);_model.EndAudioTimingGesture();
        Assert.Same(draft,_model.Draft);Assert.EndsWith("draft 👩‍👩‍👧",draft.Text);Assert.Equal(1800,rows[0].EndMilliseconds);Assert.Equal(source.Replace("0:00:00.30","0:00:00.20").Replace("0:00:01.40","0:00:01.80"),_model.ActiveEditor.Document.Serialize());
        Assert.True(_model.ActiveEditor.Undo.Undo());Assert.Equal(source,_model.ActiveEditor.Document.Serialize());Assert.False(_model.ActiveEditor.Undo.CanUndo);
    }
    [Fact] public async Task PendingStateIsDocumentLocalAndExternalTimingChangesInvalidateIt()
    {
        var rows=await TimingRows();var document=_model.ActiveDocumentId;var editor=_model.ActiveEditor!;await Command("time/lead/in");
        Assert.False(await _model.Registry.InvokeAsync("audio/commit",new(Guid.NewGuid())));await Command(CommandIds.SubtitleNew);Assert.False(_model.HasPendingAudioTiming);
        await Command(CommandIds.WorkspaceActivateTab,document);Assert.Same(editor,_model.ActiveEditor);Assert.Equal(new AudioClipInterval(200,1400),_model.PrimaryAudioRange);
        editor.SetField(rows[0],"End","0:00:02.00","External timing");Assert.False(_model.HasPendingAudioTiming);Assert.Equal(new AudioClipInterval(300,2000),_model.PrimaryAudioRange);
        await Command("time/lead/in");using var cancel=new CancellationTokenSource();cancel.Cancel();Assert.False(await _model.Registry.InvokeAsync("audio/commit/stay",new(),cancellationToken:cancel.Token));Assert.Equal(300,rows[0].StartMilliseconds);Assert.True(_model.HasPendingAudioTiming);
    }
    [Fact] public async Task TimingPreferencesPersistRecoverAndRemainSeparateFromGenericInsertion()
    {
        await TimingRows();await Command("audio/opt/autocommit");await Command("audio/opt/autonext");await Command("audio/timing/preferences",new AudioTimingPreferencesRequest(4000,120,400));
        var restored=new SettingsStore(Path.Combine(_root,"settings.json")).Load();Assert.True(restored.AudioAutoCommit);Assert.False(restored.AudioAutoNext);Assert.Equal(4000,restored.AudioDefaultDuration);Assert.Equal(120,restored.AudioLeadIn);Assert.Equal(400,restored.AudioLeadOut);
        var before=restored;Assert.False(await _model.Registry.InvokeAsync("audio/timing/preferences",new(),new AudioTimingPreferencesRequest(-1,0,0)));Assert.Equal(before.AudioDefaultDuration,new SettingsStore(Path.Combine(_root,"settings.json")).Load().AudioDefaultDuration);
        File.WriteAllText(Path.Combine(_root,"settings.json"),"""{"audioDefaultDuration":-1,"audioLeadIn":"broken","audioLeadOut":null,"audioAutoCommit":true,"audioAutoNext":false,"toolbarVisible":false}""");
        restored=new SettingsStore(Path.Combine(_root,"settings.json")).Load();Assert.Equal(3000,restored.AudioDefaultDuration);Assert.Equal(100,restored.AudioLeadIn);Assert.Equal(350,restored.AudioLeadOut);Assert.True(restored.AudioAutoCommit);Assert.False(restored.AudioAutoNext);Assert.False(restored.ToolbarVisible);
        var old=_model.SelectedEvent!;await Command(CommandIds.GridInsertAfter);Assert.Equal(old.EndMilliseconds+2000,_model.SelectedEvent!.EndMilliseconds);
    }
    [Fact] public async Task SourceChangeCancelsAnObsoleteGestureAndSaveDoesNotPublishMarkers()
    {
        var rows=await TimingRows();await Command("time/lead/in");var source=_model.ActiveEditor!.Document.Serialize();
        var path=Path.Combine(_root,"stored-only.ass");Assert.True(_model.SaveActiveSubtitle(path));Assert.Equal(source,AssDocument.Load(path).Serialize());Assert.True(_model.HasPendingAudioTiming);
        Assert.True(_model.BeginAudioTimingGesture());_model.UpdateAudioTimingGesture(1,.2);
        _model.ActiveEditor.SetField(rows[0],"End","0:00:02.00","External timing");Assert.False(_model.HasGesture);Assert.False(_model.HasPendingAudioTiming);
        _model.EndAudioTimingGesture();Assert.Equal(2000,rows[0].EndMilliseconds);Assert.Equal("External timing",_model.ActiveEditor.Undo.NextUndoName);
    }
    [Fact] public async Task NormalCommitNextRetainsStoredDestinationAndEditNextUsesTimingSuccessorPolicy()
    {
        var rows=await TimingRows();await Command("time/lead/out");await Command("audio/commit/next");Assert.Same(rows[1],_model.SelectedEvent);Assert.Equal(new AudioClipInterval(2000,3000),_model.PrimaryAudioRange);Assert.False(_model.HasPendingAudioTiming);
        await Command("time/lead/out");await Command(CommandIds.EditCommitNext);Assert.Equal(3350,rows[1].EndMilliseconds);Assert.Equal(3350,_model.SelectedEvent!.StartMilliseconds);Assert.Equal(6350,_model.SelectedEvent.EndMilliseconds);
    }
}
