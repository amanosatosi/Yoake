using Yoake.Core.Commands;
using Yoake.Core.Media;
using Yoake.Core.Subtitles;
using Yoake.Native;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed record VideoDetails(string Source, int Width, int Height, int FrameCount, double FramesPerSecond,
    double DurationSeconds, double LastFrameSeconds, string Decoder, string Timing, string PixelFormat);

public sealed partial class MainWindowViewModel
{
    public VideoAspect VideoAspect => _activeId is { } id && _documents.TryGetValue(id, out var state) ? state.VideoAspect : new();
    public double VideoAspectRatio => VideoAspect.Ratio(_media?.VideoProvider is { } video ? video.SourceInfo.Width / (double)video.SourceInfo.Height : 1);
    public bool ShowVideoOverscan => _settings.VideoOverscan;
    public DummyVideoOptions LastDummyVideo
    {
        get { var options = _settings.LastDummyVideo ?? new(); try { options.Validate(); return options; } catch (ArgumentException) { return new(); } }
    }
    private string? RememberDummyVideo(DummyVideoOptions options)
    {
        // Project::DoLoadVideo -> SetPath(...,"Video",path) records dummy
        // sources in 3.2.2. These typed entries reopen without filesystem probes.
        _settings = _settings with { LastDummyVideo = options, RecentVideo = new[] { options.SourceName }
            .Concat((_settings.RecentVideo ?? []).Where(p => p != options.SourceName)).Take(12).ToArray() };
        try { _settingsStore.Save(_settings); OnPropertyChanged(nameof(RecentVideoItems)); return null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { OnPropertyChanged(nameof(RecentVideoItems)); _registry.ReportFailure("settings/dummy-video", e); return "Dummy video retained; last options could not be saved: " + e.Message; }
    }
    public VideoDetails? CurrentVideoDetails
    {
        get
        {
            if (_media?.VideoProvider is not { } video) return null;
            var info = video.SourceInfo;
            return new(video.SourcePath, info.Width, info.Height, info.FrameCount, info.FramesPerSecond,
                video.VideoDurationSeconds, FrameTimes.Count == 0 ? 0 : FrameTimes[^1], video.DecoderName,
                ExternalTimecodePath is not null ? "External timecodes: " + ExternalTimecodePath : video.IsDummy ? "Constant frame rate" : "Native provider timestamps (supports VFR)", "BGRA8888 decoded output");
        }
    }
    private AssPoint? _videoPointer;
    public void ReportVideoPointer(AssPoint? point) { if (point is null || double.IsFinite(point.Value.X) && double.IsFinite(point.Value.Y)) _videoPointer = point; }
    private static VideoAspect InitialVideoAspect(IVideoProvider? video) => video is { IsDummy:false, DisplayAspectRatio:>0 }
        ? new(VideoAspectKind.Custom, Math.Clamp(video.DisplayAspectRatio,0.5,5)) : new();
    public VideoPresentation VideoViewport(double width, double height) => VideoPresentation.Fit(width, height, VideoAspectRatio);
    private void RegisterVideoUtilityCommands()
    {
        bool Applies(CommandContext context) => _activeId is not null && (context.DocumentId is null || context.DocumentId == _activeId);
        void Register(string id, string label, Func<CommandInvocation, CancellationToken, ValueTask> action, bool requiresVideo = true, Func<bool>? check = null)
        {
            _registry.Register(new(new(id, label, label, "Video"), (invocation, token) => { token.ThrowIfCancellationRequested(); return action(invocation, token); }, c => Applies(c) && (!requiresVideo || _media?.HasVideo == true),
                isChecked: check is null ? null : c => Applies(c) && check()));
            Actions[id] = new RegistryCommand(_registry, id, CurrentContext, hotkeys: () => HotkeyProfile);
        }
        Register("video/open/dummy", "Use dummy video…", async (invocation, token) =>
        {
            var id = _activeId; var state = _documents[id!.Value];
            var options = invocation.Parameter as DummyVideoOptions ?? (Dialogs is null ? null : await Dialogs.ShowDummyVideoAsync(LastDummyVideo));
            token.ThrowIfCancellationRequested();
            if (options is not null && _activeId == id && _documents.TryGetValue(id.Value, out var live) && ReferenceEquals(state, live))
                await OpenDummyVideoAsync(options, token);
        }, requiresVideo: false);
        foreach (var (suffix, kind, label) in new[]
        {
            ("default", VideoAspectKind.Default, "Default"), ("full", VideoAspectKind.Full, "Fullscreen (4:3)"),
            ("wide", VideoAspectKind.Wide, "Widescreen (16:9)"), ("cinematic", VideoAspectKind.Cinematic, "Cinematic (2.35)"),
            ("custom", VideoAspectKind.Custom, "Custom…")
        })
        {
            Register("video/aspect/" + suffix, label, async (invocation, token) =>
            {
                var id = _activeId!.Value; var state = _documents[id]; var provider = _media!.VideoProvider;
                StopPlayback();
                var aspect = new VideoAspect(kind);
                if (kind == VideoAspectKind.Custom)
                {
                    var text = invocation.Parameter as string ?? (Dialogs is null ? null : await Dialogs.ShowCustomAspectAsync(VideoAspectRatio));
                    if (text is null) return;
                    if (!Yoake.Core.Media.VideoAspect.TryCustom(text, out aspect)) throw new ArgumentException("Aspect ratio must be between 0.5 and 5.0 (decimal, 16:9, 16/9 or 853x480).");
                }
                token.ThrowIfCancellationRequested();
                if (_activeId != id || !_documents.TryGetValue(id, out var live) || !ReferenceEquals(state, live) || !ReferenceEquals(provider, _media?.VideoProvider)) return;
                CancelGesture(); state.VideoAspect = aspect; _videoPointer = null;
                OnPropertyChanged(nameof(VideoAspectRatio)); _registry.NotifyStateChanged();
            }, check: () => VideoAspect.Kind == kind);
        }
        Register("video/copy_coordinates", "Copy coordinates to clipboard", async (_, token) =>
        {
            token.ThrowIfCancellationRequested();
            // 3.2.2 retains the last in-video script position after the pointer
            // leaves. The unused Vector2D sentinel serializes as 0,0 at 3 decimals.
            await (Dialogs ?? throw new InvalidOperationException("Clipboard is unavailable.")).WriteClipboardAsync(VideoPresentation.Coordinates(_videoPointer ?? new AssPoint(0, 0)));
        });
        Register("video/details", "Show video details", async (_, token) =>
        {
            StopPlayback(); token.ThrowIfCancellationRequested();
            await (Dialogs ?? throw new InvalidOperationException("Video details are unavailable.")).ShowVideoDetailsAsync(CurrentVideoDetails!);
        });
        Register("video/show_overscan", "Show overscan mask", (_, token) =>
        {
            token.ThrowIfCancellationRequested(); _settings = _settings with { VideoOverscan = !ShowVideoOverscan };
            OnPropertyChanged(nameof(ShowVideoOverscan)); _registry.NotifyStateChanged(); _settingsStore.Save(_settings);
            return ValueTask.CompletedTask;
        }, check: () => ShowVideoOverscan);
    }
}
