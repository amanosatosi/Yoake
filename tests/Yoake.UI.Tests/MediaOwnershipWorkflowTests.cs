using Xunit;
using Yoake.Core.Commands;
using Yoake.Core.Settings;

namespace Yoake.UI.Tests;

public sealed partial class EditorWorkflowTests
{
    [Fact] public async Task FailedSaveRollsBackMediaPropertiesAndRetainsProviderForRetry()
    {
        Assert.True(await _model.OpenDummyVideoAsync(new(){Width=320,Height=180,FrameCount=12}));
        var editor=_model.ActiveEditor!;var before=editor.Document.Serialize();var undo=editor.Undo.NextUndoName;
        Assert.False(_model.SaveActiveSubtitle(Path.Combine(_root,"missing-directory","project.ass")));
        Assert.Equal(before,editor.Document.Serialize());Assert.Equal(undo,editor.Undo.NextUndoName);
        Assert.Equal(12,_model.FrameTimes.Count);
        var path=Path.Combine(_root,"retry.ass");Assert.True(_model.SaveActiveSubtitle(path));
        Assert.StartsWith("?dummy:",editor.Document.GetSectionValue("[Aegisub Project Garbage]","Video File"));
        Assert.Equal(editor.Document.Serialize(),Yoake.Core.Subtitles.AssDocument.Load(path).Serialize());
    }

    [Fact] public async Task SavedMediaAssociationsRestoreDummyVideoAndRespectExplicitClosure()
    {
        Assert.True(await _model.OpenDummyVideoAsync(new(){Width=320,Height=180,FrameCount=12}));
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="日本語 မြန်မာ";await Command(CommandIds.EditCommit);
        var timecodes=Path.Combine(_root,"日本語 timecodes.txt");
        File.WriteAllText(timecodes,"# timecode format v2\n"+string.Join('\n',Enumerable.Range(0,12).Select(i=>i*150))+"\n");
        var keyframes=Path.Combine(_root,"မြန်မာ keyframes.txt");File.WriteAllText(keyframes,"# keyframe format v1\nfps 0\n0\n2\n");
        await Command("timecode/open",timecodes);await Command("keyframe/open",keyframes);
        var originalTimecodes=File.ReadAllBytes(timecodes);var originalKeyframes=File.ReadAllBytes(keyframes);
        Assert.False(_model.SaveActiveSubtitle(timecodes));Assert.False(_model.SaveActiveSubtitle(keyframes));
        Assert.Equal(originalTimecodes,File.ReadAllBytes(timecodes));Assert.Equal(originalKeyframes,File.ReadAllBytes(keyframes));
        var path=Path.Combine(_root,"associated.ass");Assert.True(_model.SaveActiveSubtitle(path));
        var source=_model.ActiveEditor!.Document.Serialize();var id=_model.ActiveDocumentId!.Value;
        Assert.StartsWith("?dummy:",_model.ActiveEditor.Document.GetSectionValue("[Aegisub Project Garbage]","Video File"));
        Assert.True(await _model.CloseDocumentAsync(id));
        await Command(CommandIds.SubtitleOpen,path);
        Assert.Equal(12,_model.FrameTimes.Count);Assert.True(_model.IsVideoView);Assert.False(_model.HasAudio);
        Assert.Equal(source,_model.ActiveEditor!.Document.Serialize());
        Assert.Equal(timecodes,_model.ExternalTimecodePath);Assert.Equal(keyframes,_model.ExternalKeyframePath);
        Assert.Equal(.15,_model.FrameTimes[1],8);Assert.Equal(new[]{0,2},_model.Keyframes);
        await Command("video/close");await Command("timecode/close");await Command("keyframe/close");Assert.True(_model.SaveActiveSubtitle(path));
        Assert.Equal("",_model.ActiveEditor.Document.GetSectionValue("[Aegisub Project Garbage]","Video File"));
        Assert.True(await _model.CloseDocumentAsync(_model.ActiveDocumentId!.Value));
        await Command(CommandIds.SubtitleOpen,path);Assert.Null(_model.VideoSourcePath);Assert.False(_model.HasAudio);
        Assert.Null(_model.ExternalTimecodePath);Assert.Null(_model.ExternalKeyframePath);
    }

    [Fact] public async Task AudioStreamSelectionWithoutAudioPreservesIndependentVideoAndDraft()
    {
        Assert.True(await _model.OpenDummyVideoAsync(new(){Width=320,Height=180,FrameCount=12}));
        await Command(CommandIds.GridInsertAfter);
        _model.Draft!.Text = "日本語 မြန်မာ retained";
        _model.Draft.Start = "unfinished";
        var editor = _model.ActiveEditor!; var source = editor.Document.Serialize();
        var draft = _model.Draft; var undo = editor.Undo.NextUndoName; var video = _model.VideoSourcePath;
        Assert.Empty(_model.AudioTracks); Assert.Null(_model.AudioTrackIndex);
        Assert.False(_model.Actions["audio/stream"].CanExecute(null));
        Assert.False(_model.Actions["audio/open/video"].CanExecute(null));
        Assert.False(await _model.Registry.InvokeAsync("audio/open/video", new()));
        Assert.False(await _model.Registry.InvokeAsync("audio/stream", new(), 1));
        Assert.False(await _model.Registry.InvokeAsync("audio/stream", new(Guid.NewGuid()), 1));
        Assert.False(await _model.SelectAudioTrackAsync(1));
        Assert.Same(draft, _model.Draft); Assert.Equal(source, editor.Document.Serialize());
        Assert.Equal(undo, editor.Undo.NextUndoName); Assert.Equal(video, _model.VideoSourcePath);
        Assert.True(_model.IsVideoView);
    }

    [Fact] public async Task ReopeningVideoAfterCommittedPublicationReportsSuccess()
    {
        var first = new Yoake.Core.Media.DummyVideoOptions { Width=320, Height=180, FrameCount=12 };
        Assert.True(await _model.OpenDummyVideoAsync(first));
        var firstRevision = _model.VideoProviderRevision;
        Assert.True(await _model.OpenDummyVideoAsync(new(){Width=640,Height=360,FrameCount=12}));
        Assert.True(_model.VideoProviderRevision > firstRevision);
        Assert.Equal(640,_model.CurrentVideoDetails!.Width);
        Assert.Equal(Yoake.Core.Settings.WorkspaceViewMode.Video,_model.ViewMode);
    }

    [Fact] public async Task ReentrantVideoOpenSupersedesPublishedRequestAndLatestWins()
    {
        Task<bool>? latest = null;
        System.ComponentModel.PropertyChangedEventHandler? published = null;
        published = (_, e) =>
        {
            if (e.PropertyName != nameof(Yoake.UI.ViewModels.MainWindowViewModel.VideoSourcePath) ||
                _model.VideoSourcePath is null) return;
            _model.PropertyChanged -= published;
            latest = _model.OpenDummyVideoAsync(new(){Width=640,Height=360,FrameCount=12});
        };
        _model.PropertyChanged += published;
        try
        {
            var superseded = _model.OpenDummyVideoAsync(new(){Width=320,Height=180,FrameCount=12});
            Assert.False(await superseded);
            Assert.NotNull(latest);
            Assert.True(await latest!);
            Assert.Equal(640,_model.CurrentVideoDetails!.Width);
            Assert.Equal(Yoake.Core.Settings.WorkspaceViewMode.Video,_model.ViewMode);
        }
        finally { _model.PropertyChanged -= published; }
    }

    [Fact] public async Task MissingVideoAndAudioOpensLeaveDocumentDraftPendingTimingSelectionAndUndoIntact()
    {
        await TimingRows(); await Command("time/lead/out"); _model.Draft!.Text += " retained 日本語 မြန်မာ";
        var editor = _model.ActiveEditor!; var source = editor.Document.Serialize(); var undo = editor.Undo.NextUndoName;
        var selection = _model.SelectedEvent; var draft = _model.Draft; var range = _model.PrimaryAudioRange;
        var missing = Path.Combine(_root, "存在しない.mkv");
        Assert.False(await _model.OpenVideoAsync(missing)); Assert.False(await _model.OpenAudioAsync(missing));
        await Command("recent/video", missing);
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            Assert.False(await _model.Registry.InvokeAsync("video/open", new(), missing, canceled.Token));
        }
        foreach (var command in new[] { "video/open", "recent/video", "audio/open", "recent/audio" })
            Assert.False(await _model.Registry.InvokeAsync(command, new(Guid.NewGuid()), missing));
        Assert.Same(editor, _model.ActiveEditor); Assert.Same(selection, _model.SelectedEvent); Assert.Same(draft, _model.Draft);
        Assert.Equal(range, _model.PrimaryAudioRange); Assert.Equal(source, editor.Document.Serialize()); Assert.Equal(undo, editor.Undo.NextUndoName);
        Assert.Null(_model.VideoSourcePath); Assert.Null(_model.AudioSourcePath); Assert.Empty(_model.RecentVideoItems); Assert.Empty(_model.RecentAudioItems);
        Assert.True(_model.IsDefaultView); Assert.False(_model.ShowAudio); Assert.False(_model.ShowVideo);
        Assert.False(_model.Actions[CommandIds.ViewAudioVideo].CanExecute(null));
        Assert.False(_model.Actions["video/close"].CanExecute(null));
        Assert.False(_model.Actions["audio/close"].CanExecute(null));
        Assert.False(await _model.Registry.InvokeAsync("audio/close", new(Guid.NewGuid())));
    }
    [Fact] public async Task RestoredMediaRecentsDoNotReopenProvidersAndMissingVideoRemovesOnlyItsOwnEntry()
    {
        using var model = new Yoake.UI.ViewModels.MainWindowViewModel(new(), new(), new(), new Yoake.UI.Services.ThemeService(),
            new SettingsStore(Path.Combine(_root, "media-settings.json")), new AppSettings { RecentVideo = ["C:/日本語.mkv"], RecentAudio = ["C:/音声.wav"] });
        Assert.Single(model.RecentVideoItems); Assert.Single(model.RecentAudioItems);
        Assert.Null(model.VideoSourcePath); Assert.Null(model.AudioSourcePath); Assert.False(model.CanPlayMedia);
        Assert.Equal("recent/video", model.RecentVideoItems[0].Command.CommandId);
        Assert.True(await model.Registry.InvokeAsync("recent/video", new(), "C:/日本語.mkv"));
        Assert.Empty(model.RecentVideoItems); Assert.Single(model.RecentAudioItems);
        var saved = new SettingsStore(Path.Combine(_root, "media-settings.json")).Load();
        Assert.Empty(saved.RecentVideo!); Assert.Single(saved.RecentAudio!);
    }
    [Fact] public async Task RecentPersistenceFailureReportsAnErrorWithoutUndoingTheLiveRecentStateOrEditingTheDocument()
    {
        var blocked = Path.Combine(_root, "blocked"); File.WriteAllText(blocked, "not a directory");
        var missing = Path.Combine(_root, "存在しない.mkv");
        using var model = new Yoake.UI.ViewModels.MainWindowViewModel(new(), new(), new(), new Yoake.UI.Services.ThemeService(),
            new SettingsStore(Path.Combine(blocked, "settings.json")), new AppSettings { RecentVideo = [missing] });
        var source = model.ActiveEditor!.Document.Serialize(); var draft = model.Draft;
        var failures = new List<string>(); model.Registry.CommandFailed += (_, failure) => failures.Add(failure.CommandId);
        Assert.True(await model.Registry.InvokeAsync("recent/video", new(), missing));
        Assert.Contains("settings/media-recents", failures); Assert.Empty(model.RecentVideoItems);
        Assert.Equal(source, model.ActiveEditor.Document.Serialize()); Assert.Same(draft, model.Draft); Assert.False(model.ActiveEditor.Undo.CanUndo);
        Assert.Contains("recent history could not be saved", model.MediaStatus);
    }
}
