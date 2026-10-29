using Xunit;
using Yoake.Core.Media;
using Yoake.Core.Commands;

namespace Yoake.UI.Tests;

public sealed partial class EditorWorkflowTests
{
    [Fact] public async Task VideoTimeInsertionUsesFrameBoundaryAndOneUndoWithoutLeakingMetadata()
    {
        const string before="subtitle/insert/before/videotime", after="subtitle/insert/after/videotime";
        var original=_model.SelectedEvent!;
        _model.Draft!.Text="日本語 {\\future(keep)}";_model.Draft.Actor="Existing actor";await Command(CommandIds.EditCommit);
        var editor=_model.ActiveEditor!;var source=editor.Document.Serialize();
        Assert.False(_model.Actions[before].CanExecute(null));Assert.False(_model.Actions[after].CanExecute(null));
        await _model.OpenDummyVideoAsync(new(){Width=320,Height=180,FrameCount=40,FramesPerSecond=25});
        _model.CurrentTimeSeconds=.2;
        Assert.False(await _model.Registry.InvokeAsync(after,new(Guid.NewGuid())));
        await Command(after);var inserted=_model.SelectedEvent!;
        Assert.Equal(new[]{original,inserted},_model.Events);Assert.Equal(original.Style,inserted.Style);
        Assert.Equal(180,inserted.StartMilliseconds);Assert.Equal(3180,inserted.EndMilliseconds);
        Assert.Equal("",inserted.Text);Assert.Equal("",inserted.Get("Name"));
        await Command(CommandIds.EditUndo);Assert.Equal(source,editor.Document.Serialize());
        await Command(CommandIds.EditRedo);Assert.Equal(2,_model.Events.Count);
        await Command(CommandIds.EditUndo);_model.SelectedEvent=original;_model.CurrentTimeSeconds=.2;
        Assert.Equal(5,_model.CurrentFrame);
        await Command(before);Assert.Same(original,_model.Events[1]);Assert.Equal(180,_model.Events[0].StartMilliseconds);
        await Command(CommandIds.EditUndo);Assert.Equal(source,editor.Document.Serialize());
    }
    [Fact] public async Task ZoomPanDetachUseCommandsAndPreserveDocumentAndExports()
    {
        await TimingRows();await _model.OpenDummyVideoAsync(new(){Width=640,Height=480,FrameCount=20});
        await Command("time/lead/out");_model.Draft!.Layer="unfinished";
        var source=_model.ActiveEditor!.Document.Serialize();var draft=_model.Draft;var selected=_model.SelectedEvent;var pending=_model.PrimaryAudioRange;var undo=_model.ActiveEditor.Undo.NextUndoName;
        var pixels=(await _model.ExtractCurrentVideoFrameAsync(FrameImageKind.Raw)).Image.Pixels;
        foreach(var percent in new[]{50,100,200})
        {
            await Command("video/zoom/"+percent);var area=_model.VideoViewport(320,240);
            Assert.Equal(480*percent/100d,area.Height);Assert.True(_model.Actions["video/zoom/"+percent].IsChecked);
            Assert.Single(new[]{50,100,200}.Where(p=>_model.Actions["video/zoom/"+p].IsChecked));
        }
        await Command("video/zoom/in");Assert.Equal(2.125,_model.VideoWindowZoom);await Command("video/zoom/out");Assert.Equal(2,_model.VideoWindowZoom);
        _model.PanVideo(31,17);_model.ZoomVideoContent(1.5,130,80);Assert.True(_model.VideoView!.ContentActive);
        await Command("video/reset_pan");Assert.False(_model.VideoView.ContentActive);Assert.Equal(2,_model.VideoWindowZoom);
        await Command("video/aspect/wide");Assert.Equal(16d/9,_model.VideoViewport(320,240).Width/_model.VideoViewport(320,240).Height,8);
        await Command("video/detach");Assert.True(_model.IsVideoDetached);Assert.True(_dialogs.Detached);Assert.True(_model.Actions["video/detach"].IsChecked);
        foreach(var id in new[]{"video/zoom/50","video/zoom/100","video/zoom/200","video/zoom/in","video/zoom/out"})Assert.False(_model.Actions[id].CanExecute(null));
        await Command("video/presentation/zoom","75%");Assert.Equal(.75,_model.VideoWindowZoom);
        await Command("video/detach");Assert.False(_model.IsVideoDetached);Assert.False(_dialogs.Detached);
        Assert.Equal(source,_model.ActiveEditor.Document.Serialize());Assert.Same(draft,_model.Draft);Assert.Same(selected,_model.SelectedEvent);Assert.Equal(pending,_model.PrimaryAudioRange);Assert.Equal(undo,_model.ActiveEditor.Undo.NextUndoName);
        Assert.Equal(pixels,(await _model.ExtractCurrentVideoFrameAsync(FrameImageKind.Raw)).Image.Pixels);
        using var canceled=new CancellationTokenSource();canceled.Cancel();Assert.False(await _model.Registry.InvokeAsync("video/detach",new(),null,canceled.Token));Assert.False(_model.IsVideoDetached);
        Assert.False(await _model.Registry.InvokeAsync("video/reset_pan",new(Guid.NewGuid())));
        var revision=_model.VideoProviderRevision;await Command("video/close");Assert.True(_model.VideoProviderRevision>revision);foreach(var id in new[]{"video/zoom/100","video/reset_pan","video/detach"})Assert.False(_model.Actions[id].CanExecute(null));
    }
    [Fact] public async Task PresentationBelongsToTabsAndCloseRetainsDetachedPreference()
    {
        await _model.OpenDummyVideoAsync(new(){Width=160,Height=90,FrameCount=20});var first=_model.ActiveDocumentId;
        await Command("video/zoom/200");_model.VideoViewport(400,300);_model.PanVideo(21,13);await Command("video/detach");var state=_model.VideoView;
        await Command(CommandIds.SubtitleNew);await _model.OpenDummyVideoAsync(new(){Width=320,Height=240,FrameCount=20});Assert.False(_model.IsVideoDetached);Assert.NotSame(state,_model.VideoView);Assert.Equal(0,_model.VideoView!.PanX);
        await Command(CommandIds.WorkspaceActivateTab,first);Assert.Same(state,_model.VideoView);Assert.True(_model.IsVideoDetached);Assert.Equal(21,_model.VideoView!.PanX);
        await Command("video/close");Assert.True(_model.IsVideoDetached);Assert.False(_model.Actions["video/detach"].CanExecute(null));
        await Command("recent/video",_model.RecentVideoItems[0].Path);Assert.True(_model.IsVideoDetached);Assert.False(_model.VideoView.ContentActive);
    }
}
