using Yoake.Core.Audio;
using Yoake.Core.Commands;
using Yoake.Core.Media;
using Yoake.Native;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed record RecentMediaItem(string Path, RegistryCommand Command);

public sealed partial class MainWindowViewModel
{
    public string? VideoSourcePath => _media?.VideoSourcePath;
    public string? AudioSourcePath => _media?.AudioSourcePath;
    public IReadOnlyList<RecentMediaItem> RecentVideoItems => RecentMedia(true);
    public IReadOnlyList<RecentMediaItem> RecentAudioItems => RecentMedia(false);
    private IReadOnlyList<RecentMediaItem> RecentMedia(bool video) =>
        ((video ? _settings.RecentVideo : _settings.RecentAudio) ?? []).Select(path => new RecentMediaItem(path,
            new RegistryCommand(_registry, video ? "recent/video" : "recent/audio", CurrentContext, path, hotkeys: () => HotkeyProfile))).ToArray();

    private void RegisterMediaOwnershipCommands()
    {
        bool Applies(CommandContext context) => _activeId is not null && (context.DocumentId is null || context.DocumentId == _activeId);
        foreach (var video in new[] { true, false })
        {
            async ValueTask Open(CommandInvocation invocation, CancellationToken token)
            {
                var id = _activeId!.Value; var state = _documents[id];
                var path = invocation.Parameter as string ?? (Dialogs is null ? null :
                    video ? await Dialogs.OpenVideoAsync() : await Dialogs.OpenAudioAsync());
                token.ThrowIfCancellationRequested();
                if (path is null || _activeId != id || !_documents.TryGetValue(id, out var live) || !ReferenceEquals(state, live)) return;
                await OpenMediaTrackAsync(path, video, token);
            }
            var name = video ? "video" : "audio";
            _registry.Register(new(new(name + "/open", "Open " + name + "…", "Replace only " + name, video ? "Video" : "Audio"), Open, Applies));
            _registry.Register(new(new("recent/" + name, "Recent " + name, "Open recent " + name, video ? "Video" : "Audio"),
                async (invocation, token) =>
                {
                    if (invocation.Parameter is not string path) return;
                    if (!await OpenMediaTrackAsync(path, video, token) &&
                        (video && path.StartsWith("?dummy:", StringComparison.Ordinal) ? !DummyVideoOptions.TrySourceName(path, out _) : !File.Exists(path))) ForgetMedia(path, video);
                }, Applies));
            foreach (var command in new[] { name + "/open", "recent/" + name }) Actions[command] = new RegistryCommand(_registry, command, CurrentContext, hotkeys: () => HotkeyProfile);
        }
        _registry.Register(new(new("video/close", "Close video", "Release video while retaining audio", "Video"),
            (_, _) => { CloseVideo(); return ValueTask.CompletedTask; }, context => Applies(context) &&
                (_media?.HasVideo == true || _documents[_activeId!.Value].VideoLoading is not null)));
        Actions["video/close"] = new RegistryCommand(_registry, "video/close", CurrentContext, hotkeys: () => HotkeyProfile);
        _registry.Register(new(new("audio/close", "Close audio", "Release audio while retaining video", "Audio"),
            (_, token) => { token.ThrowIfCancellationRequested(); CloseAudio(); return ValueTask.CompletedTask; }, context => Applies(context) &&
                (_media?.HasAudio == true || _documents[_activeId!.Value].AudioLoading is not null)));
        Actions["audio/close"] = new RegistryCommand(_registry, "audio/close", CurrentContext, hotkeys: () => HotkeyProfile);
    }

    public Task<bool> OpenVideoAsync(string path, CancellationToken token = default) => OpenMediaTrackAsync(path, true, token);
    public Task<bool> OpenAudioAsync(string path, CancellationToken token = default) => OpenMediaTrackAsync(path, false, token);
    public Task<bool> OpenDummyVideoAsync(DummyVideoOptions options, CancellationToken token = default) => OpenMediaTrackAsync("", true, token, options);
    private async Task<bool> OpenMediaTrackAsync(string path, bool video, CancellationToken parent, DummyVideoOptions? dummy = null)
    {
        if (_disposed || _activeId is not { } id || !_documents.TryGetValue(id, out var state)) return false;
        parent.ThrowIfCancellationRequested();
        var draft = Draft; state.MediaDraftPauses++;
        _editBurstDelay?.Cancel(); _editBurstDelay?.Dispose(); _editBurstDelay = null;
        state.Loading?.Cancel();
        (video ? state.VideoLoading : state.AudioLoading)?.Cancel();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(parent);
        if (video) state.VideoLoading = cancellation; else state.AudioLoading = cancellation;
        var token = cancellation.Token;
        var external = state.ExternalTimecodes; var keyframes = state.ExternalKeyframes;
        DocumentMedia? candidate = null, prepared = null; FrameTiming? nativeTiming = null; WaveformData? peaks = null;
        MediaStatus = dummy is null ? "FFMS2 indexing " + Path.GetFileName(path) : "Preparing dummy video";
        try
        {
            if (video && dummy is null && path.StartsWith("?dummy:", StringComparison.Ordinal))
            {
                if (!DummyVideoOptions.TrySourceName(path, out dummy)) throw new ArgumentException("Invalid dummy video source.");
            }
            var full = dummy is null ? Path.GetFullPath(path) : "";
            var job = _jobs.Run(video ? "Open video" : "Open audio", ct =>
            {
                candidate = dummy is null ? DocumentMedia.Open(full, ct, video ? MediaTracks.Video : MediaTracks.Audio)
                    : DocumentMedia.FromVideo(new DummyVideoProvider(dummy, ct));
                if (video)
                {
                    nativeTiming = new(candidate.FrameTimes, candidate.Info.FramesPerSecond, ct);
                    if (external is not null) candidate.SetExternalTimecodes(external.ForVideoMap(candidate.Info.FrameCount, ct));
                    candidate.SetExternalKeyframes(keyframes);
                }
                else peaks = candidate.BuildWaveform(ct);
                return Task.CompletedTask;
            }, token);
            await job.Completion; token.ThrowIfCancellationRequested();
            if (_disposed || !_documents.TryGetValue(id, out var live) || !ReferenceEquals(state, live) ||
                !ReferenceEquals(video ? state.VideoLoading : state.AudioLoading, cancellation)) return false;
            // A timecode/keyframe operation may finish while indexing. Never
            // publish a candidate prepared against obsolete external metadata.
            if (video && (!ReferenceEquals(external, state.ExternalTimecodes) || !ReferenceEquals(keyframes, state.ExternalKeyframes))) return false;
            var old = state.Media;
            var replacement = prepared = old is null ? candidate : video ? old.WithVideo(candidate) : old.WithAudio(candidate);
            if (_activeId == id)
            {
                CancelGesture(); CancelVisualBounds();
                if (!video || ActiveAudioPlayback is null) StopPlayback();
                Interlocked.Increment(ref _seekGeneration);
            }
            if (!video) { state.AudioExport?.Cancel(); ResetAudioAnalysis(state); }
            state.Editing?.Cancel();
            token.ThrowIfCancellationRequested();
            state.Media = replacement;
            prepared = null;
            if (ReferenceEquals(replacement, candidate)) candidate = null;
            if (video) { state.FrameExtraction?.Cancel(); state.VideoProviderRevision++; state.VideoView.ResetContent(); state.VideoAspect = InitialVideoAspect(replacement.VideoProvider);
                _videoPointer=null; state.ProviderFrameTiming = nativeTiming; state.FrameTiming = external?.Timing ?? nativeTiming; }
            else state.Waveform = peaks;
            if (_activeId == id) PublishMediaPresentation(state, video, old is null);
            if (old is not null) _ = Task.Run(old.Dispose);
            var historyWarning = dummy is null ? RememberMedia(full, video) : RememberDummyVideo(dummy);
            if (_activeId == id && state.Media?.HasVideo == true) await RefreshVideoFrameAsync(CurrentTimeSeconds);
            if (historyWarning is not null && _activeId == id && ReferenceEquals(state.Media,replacement) &&
                ReferenceEquals(video ? state.VideoLoading : state.AudioLoading,cancellation))
                MediaStatus = MediaStatus == historyWarning ? historyWarning : historyWarning + " Preview: " + MediaStatus;
            ScheduleDocumentAutomationSync(id);
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return false; }
        catch (Exception exception)
        {
            _registry.ReportFailure(video ? "video/open" : "audio/open", exception);
            if (_activeId == id) MediaStatus = "Open failed: " + exception.Message;
            return false;
        }
        finally
        {
            try { prepared?.Dispose(); } finally { candidate?.Dispose(); }
            if (video && ReferenceEquals(state.VideoLoading, cancellation)) state.VideoLoading = null;
            if (!video && ReferenceEquals(state.AudioLoading, cancellation)) state.AudioLoading = null;
            cancellation.Dispose(); _registry.NotifyStateChanged();
            state.MediaDraftPauses--;
            if (state.MediaDraftPauses == 0 && !_disposed && _activeId == id && ReferenceEquals(Draft, draft) && Draft?.IsChanged == true && _editBurstDelay is null)
            { var delay = new CancellationTokenSource(); _editBurstDelay = delay; _ = FinalizeBurstAsync(delay.Token); }
        }
    }
    private void CloseVideo()
    {
        var id = _activeId!.Value; var state = _documents[id];
        state.VideoLoading?.Cancel(); state.Loading?.Cancel();
        state.FrameExtraction?.Cancel();
        var old = state.Media; if (old?.HasVideo != true) return;
        CancelGesture(); CancelVisualBounds();
        if (ActiveAudioPlayback is null) StopPlayback();
        Interlocked.Increment(ref _seekGeneration);
        var replacement = old.WithVideo(null);
        state.Media = replacement.HasAudio ? replacement : null;
        if (state.Media is null) replacement.Dispose();
        state.VideoProviderRevision++; state.VideoView.ResetContent(); state.ProviderFrameTiming = null; state.FrameTiming = state.ExternalTimecodes?.Timing;
        state.Editing?.Cancel();
        PublishMediaPresentation(state, true, state.Media is null);
        _ = Task.Run(old.Dispose); ScheduleDocumentAutomationSync(id);
    }
    private void CloseAudio()
    {
        var id = _activeId!.Value; var state = _documents[id];
        state.AudioLoading?.Cancel(); state.Loading?.Cancel(); state.AudioExport?.Cancel();
        // Cancel an uncommitted audio drag without discarding previously pending
        // markers or the text draft. Video gestures and frame state stay intact.
        FinishAudioTimingGesture(false);
        StopPlayback(); ResetAudioAnalysis(state);
        var old = state.Media;
        if (old?.HasAudio != true) { _registry.NotifyStateChanged(); return; }
        var replacement = old.WithAudio(null);
        state.Media = replacement.HasVideo ? replacement : null;
        if (state.Media is null) replacement.Dispose();
        state.Waveform = null;
        PublishMediaPresentation(state, false, state.Media is null);
        _ = Task.Run(old.Dispose); ScheduleDocumentAutomationSync(id);
    }
    private void ResetAudioAnalysis(DocumentState state)
    {
        state.AudioAnalysis.Cancel(); state.AudioAnalysis.Dispose(); state.AudioAnalysis = new();
        _waveformCache.Clear(); _spectrumCache.Clear();
    }
    private void PublishMediaPresentation(DocumentState state, bool videoChanged, bool playableChanged)
    {
        _media = state.Media; MediaDurationSeconds = _media?.Info.DurationSeconds ?? 0;
        // Retain the central position where possible; an explicit Audio audition
        // retains its sample clock even when its companion video disappears.
        if (!IsPlaying && MediaDurationSeconds > 0 && CurrentTimeSeconds > MediaDurationSeconds) CurrentTimeSeconds = MediaDurationSeconds;
        if (videoChanged) { VideoFrame = null; NotifyVideoPresentation(); OnPropertyChanged(nameof(VideoAspectRatio)); }
        else { WaveformSamples = state.Waveform; OnPropertyChanged(nameof(HasAudio)); EnsureKaraokeSafe(); NotifyKaraoke(); }
        foreach (var name in new[] { nameof(VideoSourcePath), nameof(AudioSourcePath), nameof(FrameTimes), nameof(Keyframes),
            nameof(FramePositionDisplay), nameof(RelativeTimingDisplay), nameof(CanShiftByFrames) }) OnPropertyChanged(name);
        if (playableChanged) OnPropertyChanged(nameof(CanPlayMedia));
        MediaStatus = _media?.SourcePath ?? "Open video/audio";
        _registry.NotifyStateChanged();
    }
    private string? RememberMedia(string path, bool video)
    {
        var full = path.StartsWith("?dummy:", StringComparison.Ordinal) ? path : Path.GetFullPath(path);
        var items = new[] { full }.Concat(((video ? _settings.RecentVideo : _settings.RecentAudio) ?? [])
            .Where(item => !SameMediaPath(item, full))).Take(12).ToArray();
        _settings = video ? _settings with { RecentVideo = items } : _settings with { RecentAudio = items };
        return PersistMediaRecents(video);
    }
    private void ForgetMedia(string path, bool video)
    {
        var full = path.StartsWith("?dummy:", StringComparison.Ordinal) ? path : Path.GetFullPath(path);
        var items = ((video ? _settings.RecentVideo : _settings.RecentAudio) ?? []).Where(item => !SameMediaPath(item, full)).ToArray();
        _settings = video ? _settings with { RecentVideo = items } : _settings with { RecentAudio = items };
        PersistMediaRecents(video);
    }
    private string? PersistMediaRecents(bool video)
    {
        string? warning = null;
        try { _settingsStore.Save(_settings); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Provider publication already succeeded. A history-write failure
            // must not masquerade as a failed transactional provider replacement.
            _registry.ReportFailure("settings/media-recents", exception);
            MediaStatus = warning = "Media state retained; recent history could not be saved: " + exception.Message;
        }
        OnPropertyChanged(video ? nameof(RecentVideoItems) : nameof(RecentAudioItems));
        return warning;
    }
    private static bool SameMediaPath(string left, string right)
    {
        if (left.StartsWith("?dummy:", StringComparison.Ordinal) || right.StartsWith("?dummy:", StringComparison.Ordinal)) return string.Equals(left, right, StringComparison.Ordinal);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        try { return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        { return string.Equals(left, right, comparison); }
    }
}
