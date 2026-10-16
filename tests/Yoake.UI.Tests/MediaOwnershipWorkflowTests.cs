using Xunit;
using Yoake.Core.Commands;
using Yoake.Core.Settings;

namespace Yoake.UI.Tests;

public sealed partial class EditorWorkflowTests
{
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
