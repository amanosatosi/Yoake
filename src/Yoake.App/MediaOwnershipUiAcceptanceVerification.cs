using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using Yoake.Core.Commands;
using Yoake.Core.Audio;
using Yoake.Core.Logging;
using Yoake.Core.Settings;
using Yoake.Core.Subtitles;
using Yoake.Native;
using Yoake.UI;
using Yoake.UI.Controls;
using Yoake.UI.ViewModels;

namespace Yoake.App;

internal sealed class MediaOwnershipUiAcceptanceVerification(IClassicDesktopStyleApplicationLifetime desktop,
    MainWindow window, MainWindowViewModel model, StartupOptions options)
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private bool _ready, _ticking; private int _stage, _waiting;
    private string _video = "", _audio = "", _source = "", _undo = "";
    private object? _samples, _draft, _selected, _editor;
    private Yoake.Core.Audio.AudioClipInterval? _pending;
    private Task<bool>? _opening, _racingVideo;
    private object? _retainedFrame, _retainedTimes;
    private Task<WaveformData?>? _obsoleteWaveform;
    private Task<Avalonia.Media.Imaging.WriteableBitmap?>? _obsoleteSpectrum;
    public void Start() { window.LayoutUpdated += (_, _) => _ready = window.IsVisible && window.Bounds.Width > 0; _timer.Tick += Tick; _timer.Start(); }
    private async void Tick(object? sender, EventArgs args)
    {
        if (!_ready || _ticking) return; _ticking = true;
        try
        {
            switch (_stage++)
            {
                case 0:
                    Require(!model.Actions["video/close"].CanExecute(null), "Close requires video.");
                    Require(model.IsDefaultView && !model.ShowVideo && !model.ShowAudio &&
                        !model.Actions[CommandIds.ViewAudio].CanExecute(null) &&
                        !model.Actions[CommandIds.ViewVideo].CanExecute(null) &&
                        !model.Actions[CommandIds.ViewAudioVideo].CanExecute(null),
                        "No media means subtitles-only with disabled media views.");
                    var root = options.ProfileDirectory!;
                    _video = Path.Combine(root, "日本語 မြန်မာ video.avi"); _audio = Path.Combine(root, "独立 audio.avi");
                    var fixture = Path.Combine(options.VerificationMedia!, "av.avi"); File.Copy(fixture, _video); File.Copy(fixture, _audio);
                    VerifyNativeOwnership(fixture);
                    var subtitles = Path.Combine(root, "media-ownership.ass");
                    File.WriteAllText(subtitles, "[Script Info]\nPlayResX: 384\nPlayResY: 288\n[Events]\nFormat: " + string.Join(',', AssDocument.EventFormat) + "\nDialogue: 0,0:00:00.30,0:00:01.40,Default,A,0001,0002,0003,fx,{\\future(x)\\distort(1,2,3,4,5,6,7,8)}日本語 é\n");
                    Require(model.OpenSubtitle(subtitles), "Open lossless ownership fixture.");
                    System.ComponentModel.PropertyChangedEventHandler? published = null;
                    published = (_, change) =>
                    {
                        if(change.PropertyName != nameof(MainWindowViewModel.AudioSourcePath) || model.AudioSourcePath is null) return;
                        model.PropertyChanged -= published;
                        Require(model.WaveformSamples is null, "The video replacement starts before combined-open audio analysis finishes.");
                        _racingVideo = model.OpenVideoAsync(_video);
                    };
                    model.PropertyChanged += published;
                    _opening = model.OpenMediaAsync(_audio); return;
                case 1:
                    if (_opening?.IsCompleted != true) { _stage--; return; }
                    var combinedCompleted = await _opening;
                    Require(_racingVideo is not null, "Published combined media must trigger the overlapping Video replacement.");
                    var videoCompleted = await _racingVideo!;
                    // A newer Video replacement may supersede the original
                    // combined-open task, so its bool is not the retention
                    // contract. The *published* original Audio provider must
                    // still finish peak analysis and coexist with the new Video.
                    Require(videoCompleted && model.AudioSourcePath == _audio &&
                            model.VideoSourcePath == _video && model.HasAudio &&
                            model.WaveformSamples is not null,
                        $"Video replacement must retain in-flight Audio and its waveform; combined={combinedCompleted}, video={videoCompleted}, audio={model.AudioSourcePath}, videoPath={model.VideoSourcePath}, audioReady={model.WaveformSamples is not null}, status={model.MediaStatus}.");
                    Require(model.IsAudioVideoView && model.Actions[CommandIds.ViewAudioVideo].CanExecute(null),
                        "Independent audio and video automatically select the combined view.");
                    await Invoke(CommandIds.ViewAudioVideo); model.Draft!.Text += " stored checkpoint"; await Invoke(CommandIds.EditCommit); await Invoke("time/lead/out");
                    _editor = model.ActiveEditor; _source = model.ActiveEditor!.Document.Serialize(); _undo = model.ActiveEditor.Undo.NextUndoName ?? "";
                    model.Draft!.Layer = "unfinished"; model.Draft.Text += " retained draft";
                    _draft = model.Draft; _selected = model.SelectedEvent; _pending = model.PrimaryAudioRange; _samples = model.WaveformSamples;
                    await Invoke("video/open", _video); Require(model.VideoSourcePath == _video && model.AudioSourcePath == _audio, "Video Open preserves independent audio path.");
                    Unchanged();
                    var map = Path.Combine(options.ProfileDirectory!, "external.txt"); File.WriteAllText(map, "# timecode format v2\n" + string.Join('\n', Enumerable.Range(0, model.FrameTimes.Count).Select(i => i * 150)) + "\n");
                    await Invoke("timecode/open", map);
                    var cuts = Path.Combine(options.ProfileDirectory!, "cuts.txt"); File.WriteAllText(cuts, "# keyframe format v1\nfps 0\n0\n2\n"); await Invoke("keyframe/open", cuts);
                    Require(!await model.OpenVideoAsync(Path.Combine(options.ProfileDirectory!, "missing.mkv")), "Failed replacement does not publish.");
                    Require(model.VideoSourcePath == _video && model.AudioSourcePath == _audio, "Failed open retains both current providers."); Unchanged();
                    window.FindControl<MenuItem>("VideoMenu")!.IsSubMenuOpen = true; return;
                case 2:
                    Press(window.FindControl<MenuItem>("CloseVideoMenu")!, Key.Enter); return;
                case 3:
                    Require(model.VideoSourcePath is null && model.VideoFrame is null && model.AudioSourcePath == _audio, "Native Close releases only video.");
                    Require(model.IsAudioView && model.ShowAudio && !model.ShowVideo &&
                        !model.Actions[CommandIds.ViewVideo].CanExecute(null) &&
                        !model.Actions[CommandIds.ViewAudioVideo].CanExecute(null),
                        "Video Close automatically selects audio-only view and disables missing video choices.");
                    Require(!model.Actions["video/play"].CanExecute(null) && model.Actions["audio/play/current"].CanExecute(null), "Audio remains usable and Video-only commands disable."); Unchanged();
                    Press(window.FindControl<AudioWaveformControl>("AudioDisplay")!, Key.S); Require(model.IsPlaying && model.ActiveAudioPlayback is not null, "Actual focused Audio playback works after Video Close.");
                    Press(window.FindControl<AudioWaveformControl>("AudioDisplay")!, Key.H);
                    window.FindControl<MenuItem>("VideoMenu")!.IsSubMenuOpen = true; return;
                case 4: window.FindControl<MenuItem>("RecentVideoMenu")!.IsSubMenuOpen = true; return;
                case 5:
                    var recent = (MenuItem)window.FindControl<MenuItem>("RecentVideoMenu")!.Items[0]!;
                    Press(recent, Key.Enter); return;
                case 6:
                    if (model.VideoSourcePath != _video || model.VideoFrame is null) { Require(++_waiting < 100, "Recent Video must complete through real semantic open."); _stage--; return; }
                    Require(model.ExternalTimecodePath is not null && model.ExternalKeyframePath is not null && model.Keyframes.SequenceEqual(new[] { 0, 2 }), "External frame metadata survives close/reopen.");
                    Require(model.IsAudioVideoView && model.Actions[CommandIds.ViewAudioVideo].CanExecute(null),
                        "Reopening Video with existing Audio automatically restores their combined view.");
                    Unchanged();
                    var times = model.FrameTimes;
                    Require(await model.OpenAudioAsync(_video), "Audio replacement succeeds independently.");
                    Require(model.VideoSourcePath == _video && model.AudioSourcePath == _video && ReferenceEquals(times, model.FrameTimes), "Audio replacement retains the video/frame map.");
                    Require(!await model.OpenAudioAsync(Path.Combine(options.ProfileDirectory!, "missing-audio.wav")) && model.VideoSourcePath == _video && model.AudioSourcePath == _video && ReferenceEquals(times, model.FrameTimes), "Failed Audio replacement also retains both providers.");
                    Require(ReferenceEquals(_editor, model.ActiveEditor) && ReferenceEquals(_draft, model.Draft) && ReferenceEquals(_selected, model.SelectedEvent) && _pending == model.PrimaryAudioRange && _source == model.ActiveEditor!.Document.Serialize() && _undo == (model.ActiveEditor.Undo.NextUndoName ?? ""), "Audio replacement retains document state.");
                    var saved = new SettingsStore(Path.Combine(options.ProfileDirectory!, "settings.json")).Load();
                    Require(saved.RecentVideo!.SequenceEqual(new[] { _video, _audio }) && saved.RecentAudio!.SequenceEqual(new[] { _video, _audio }), "Independent Unicode MRUs reorder and persist.");
                    var historyFailures = 0;
                    EventHandler<CommandFailureEventArgs> historyFailed = (_, failure) => { if(failure.CommandId == "settings/media-recents") historyFailures++; };
                    model.Registry.CommandFailed += historyFailed;
                    // Lock the file being written, not the rename destination:
                    // this forces the real write boundary on every Windows filesystem.
                    using (var locked = File.Open(Path.Combine(options.ProfileDirectory!, "settings.json.tmp"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                    {
                        Require(await model.OpenVideoAsync(_video), "A profile-write failure cannot turn a successfully published Video provider into a failed replacement.");
                        Require(historyFailures == 1, $"The locked profile must fail through the real persistence boundary; failures={historyFailures}, status={model.MediaStatus}.");
                        Require(model.VideoSourcePath == _video && model.AudioSourcePath == _video && model.MediaStatus.Contains("recent history could not be saved"), $"Successful media stays usable and profile failure is visible; video={model.VideoSourcePath}, audio={model.AudioSourcePath}, expected={_video}, status={model.MediaStatus}.");
                    }
                    model.Registry.CommandFailed -= historyFailed;
                    using (var canceled = new CancellationTokenSource())
                    { canceled.Cancel(); Require(!await model.Registry.InvokeAsync("video/open", new(model.ActiveDocumentId), _audio, canceled.Token), "Canceled request cannot replace providers."); }
                    // Tiny test AVIs can finish indexing before the next call.
                    // Only require a canceled result if the older task really was
                    // pending when its replacement started; the latest source must
                    // win either way.
                    var obsolete = model.OpenVideoAsync(_audio);
                    var latest = model.OpenVideoAsync(_video);
                    var supersededWhilePending = !obsolete.IsCompleted;
                    var obsoleteResult = await obsolete;
                    var latestResult = await latest;
                    Require(latestResult && model.VideoSourcePath == _video &&
                        (!supersededWhilePending || !obsoleteResult),
                        $"Latest video must win an overlapping open; pending={supersededWhilePending}, old={obsoleteResult}, latest={latestResult}, source={model.VideoSourcePath}, status={model.MediaStatus}.");

                    // Deterministically exercise the second race: a request
                    // already published its provider but is still refreshing
                    // its frame when the next open supersedes it.
                    Task<bool>? replacementAfterPublish = null;
                    System.ComponentModel.PropertyChangedEventHandler? onPublished = null;
                    onPublished = (_, property) =>
                    {
                        if (property.PropertyName != nameof(MainWindowViewModel.VideoSourcePath) ||
                            model.VideoSourcePath != _audio) return;
                        model.PropertyChanged -= onPublished;
                        replacementAfterPublish = model.OpenVideoAsync(_video);
                    };
                    model.PropertyChanged += onPublished;
                    try
                    {
                        var publishedThenSuperseded = model.OpenVideoAsync(_audio);
                        var oldPublishedResult = await publishedThenSuperseded;
                        Require(replacementAfterPublish is not null,
                            "The previous provider must publish before the forced supersession.");
                        var newestResult = await replacementAfterPublish!;
                        Require(!oldPublishedResult && newestResult && model.VideoSourcePath == _video,
                            $"Post-publication supersession must return false for obsolete request; old={oldPublishedResult}, latest={newestResult}, source={model.VideoSourcePath}, status={model.MediaStatus}.");
                    }
                    finally { model.PropertyChanged -= onPublished; }
                    await Invoke("audio/play/current"); Require(model.IsPlaying, "Audio audition starts before Audio Close.");
                    using (var canceledClose = new CancellationTokenSource())
                    { canceledClose.Cancel(); Require(!await model.Registry.InvokeAsync("audio/close",new(model.ActiveDocumentId),cancellationToken:canceledClose.Token) && model.IsPlaying && model.AudioSourcePath == _video,"Canceled Audio Close retains active playback and both resources."); }
                    _retainedFrame = model.VideoFrame; _retainedTimes = model.FrameTimes;
                    window.FindControl<MenuItem>("AudioMenu")!.IsSubMenuOpen = true; return;
                case 7:
                    _obsoleteWaveform = model.CreateWaveformViewportAsync(0.013, 1.137, 1023, CancellationToken.None);
                    _obsoleteSpectrum = model.CreateSpectrumAsync(0.013, 1.137, CancellationToken.None);
                    Press(window.FindControl<MenuItem>("CloseAudioMenu")!, Key.Enter); return;
                case 8:
                    Require(model.AudioSourcePath is null && model.VideoSourcePath == _video && !model.HasAudio && model.WaveformSamples is null,
                        "Audio Close releases only Audio and clears its overview.");
                    Require(model.IsVideoView && model.ShowVideo && !model.ShowAudio &&
                        !model.Actions[CommandIds.ViewAudio].CanExecute(null) &&
                        !model.Actions[CommandIds.ViewAudioVideo].CanExecute(null),
                        "Audio Close automatically selects video-only view and disables absent audio choices.");
                    Require(!model.IsPlaying && model.ActiveAudioPlayback is null && !model.Actions["audio/play/current"].CanExecute(null) && model.Actions["video/play"].CanExecute(null),
                        "Audio Close stops audition and keeps Video playback enabled.");
                    Require(ReferenceEquals(_retainedFrame, model.VideoFrame) && ReferenceEquals(_retainedTimes, model.FrameTimes) &&
                        model.ExternalTimecodePath is not null && model.ExternalKeyframePath is not null,
                        "Audio Close retains exact Video frame/provider map and external metadata.");
                    Require(await RejectedAnalysis(_obsoleteWaveform!) && await RejectedAnalysis(_obsoleteSpectrum!), "Closed Audio analysis cannot publish obsolete detail or spectrum.");
                    Require(!window.FindControl<AudioWaveformControl>("AudioDisplay")!.SpectrumTileReady, "Audio Close clears the presented spectrum.");
                    DocumentUnchanged();
                    Require(!await model.Registry.InvokeAsync("audio/close", new(model.ActiveDocumentId)), "Repeated Audio Close is safely disabled.");
                    await Invoke("video/frame/next"); await Invoke("video/play"); return;
                case 9:
                    Require(model.IsPlaying && model.VideoFrame is not null, "Retained native Video seeks and plays without Audio.");
                    await Invoke("video/stop"); Require(await model.OpenAudioAsync(_audio), "Audio reopens after Close.");
                    Require(model.AudioSourcePath == _audio && model.WaveformSamples is not null && ReferenceEquals(_retainedTimes, model.FrameTimes), "Reopening Audio retains native Video and restores analysis.");
                    Require(model.IsAudioVideoView && model.ShowVideo && model.ShowAudio &&
                        model.Actions[CommandIds.ViewAudioVideo].CanExecute(null),
                        "Reopening Audio automatically selects Audio + Video.");
                    DocumentUnchanged();
                    // Video Open (not just combined Open Media) must pick up
                    // embedded audio when this document has none.
                    await Invoke("audio/close");
                    Require(model.IsVideoView && !model.HasAudio, "Closing the independently selected audio leaves Video view.");
                    return;
                case 10:
                    Require(await model.OpenVideoAsync(_video), "Open video with an embedded audio stream.");
                    Require(model.VideoSourcePath == _video && model.AudioSourcePath == _video &&
                        model.HasAudio && model.WaveformSamples is not null && model.IsAudioVideoView,
                        $"Video Open must auto-attach embedded audio and select Audio + Video; video={model.VideoSourcePath}, audio={model.AudioSourcePath}, view={model.ViewMode}.");
                    DocumentUnchanged();
                    Require(StartupDiagnostics.BindingWarningCount == 0 && StartupDiagnostics.FrameworkErrorCount == 0, "Ownership menus/layout must have no binding errors.");
                    _timer.Stop(); options.FinishVerification("PASS: separate real-window independent media ownership; native track transfer and release; Unicode Video Open; transactional failed and superseded replacement; real Close menu Enter; focused Audio S/H after Video Close; real Recent Video menu Enter through shared backend; Audio replacement retains Video; independent persisted MRUs; deterministic post-publication supersession; auto-audio on Video Open; external timecodes/keyframes and document/draft/pending/selection/undo retention. Existing Video playback/EOF and visual acceptance run separately. Audible output remains manual QA.\n"); StartupDiagnostics.Complete(); desktop.Shutdown(0); return;
            }
        }
        catch (Exception exception) { _timer.Stop(); Program.Fatal("Packaged media ownership at stage " + (_stage - 1), exception); desktop.Shutdown(1); }
        finally { _ticking = false; }
    }
    private void Unchanged() => Require(ReferenceEquals(_editor, model.ActiveEditor) && ReferenceEquals(_samples, model.WaveformSamples) && ReferenceEquals(_draft, model.Draft) && ReferenceEquals(_selected, model.SelectedEvent) && _pending == model.PrimaryAudioRange && _source == model.ActiveEditor!.Document.Serialize() && _undo == (model.ActiveEditor.Undo.NextUndoName ?? ""), "Video changes preserve exact audio/document/draft/pending/selection/history.");
    private void DocumentUnchanged() => Require(ReferenceEquals(_editor, model.ActiveEditor) && ReferenceEquals(_draft, model.Draft) && ReferenceEquals(_selected, model.SelectedEvent) && _pending == model.PrimaryAudioRange && _source == model.ActiveEditor!.Document.Serialize() && _undo == (model.ActiveEditor.Undo.NextUndoName ?? ""), "Audio Close/reopen preserves document/draft/pending/selection/undo.");
    private static async Task<bool> RejectedAnalysis<T>(Task<T?> task) where T : class
    { try { return await task is null; } catch (OperationCanceledException) { return true; } }
    private async Task Invoke(string id, object? parameter = null) => Require(await model.Registry.InvokeAsync(id, new(model.ActiveDocumentId), parameter), "Ownership preparation: " + id);
    private static void Press(Control control, Key key)
    {
        (TopLevel.GetTopLevel(control) as Window)?.Activate(); Require(control.Focus() && control.IsKeyboardFocusWithin, "Real ownership input owns focus: " + control.Name);
        control.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Source = control, Key = key });
    }
    private static void VerifyNativeOwnership(string path)
    {
        using var original = DocumentMedia.Open(path); var video = original.VideoProvider!; var audio = original.AudioProvider!;
        Require(video.HasVideo && video is FfmsMediaSession { HasAudio:false } && audio.HasAudio && !audio.HasVideo, "One index transfers native tracks into separate owners.");
        using var candidate = DocumentMedia.Open(path, tracks: MediaTracks.Video);
        var replacement = original.WithVideo(candidate); original.Dispose(); candidate.Dispose();
        Require(!video.HasVideo && ReferenceEquals(audio, replacement.AudioProvider) && audio.ReadAudioFrames(0, 32).FrameCount == 32, "Replacing video releases its native source and retains usable audio.");
        var closed = replacement.WithVideo(null); var replacedVideo = replacement.VideoProvider!; replacement.Dispose();
        Require(!replacedVideo.HasVideo && audio.ReadAudioFrames(0, 32).FrameCount == 32, "Closing video releases video and retains native audio.");
        closed.Dispose(); Require(!audio.HasAudio, "Final audio owner releases its native source.");
        using var both = DocumentMedia.Open(path);
        var retainedVideo = both.VideoProvider!; var releasedAudio = both.AudioProvider!;
        using var audioClosed = both.WithAudio(null); both.Dispose(); both.Dispose();
        Require(!releasedAudio.HasAudio && retainedVideo.GetFrameAtTime(0).Width > 0 && ReferenceEquals(retainedVideo, audioClosed.VideoProvider), "Closing Audio retains a decoding native Video provider.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
