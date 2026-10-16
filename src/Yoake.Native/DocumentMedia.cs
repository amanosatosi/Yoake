using Yoake.Core.Audio;
using Yoake.Core.Media;

namespace Yoake.Native;

/// <summary>A document's resource snapshot. Playback remains owned by the UI's central timeline.</summary>
public sealed class DocumentMedia : IDisposable
{
    private readonly MediaProviderOwnership<IDisposable> _ownership;
    private DocumentMedia(MediaProviderOwnership<IDisposable> ownership) => _ownership = ownership;
    public IVideoProvider? VideoProvider => _ownership.Video as IVideoProvider;
    public FfmsMediaSession? AudioProvider => _ownership.Audio as FfmsMediaSession;
    public bool HasVideo => VideoProvider is not null;
    public bool HasAudio => AudioProvider is not null;
    public string? VideoSourcePath => VideoProvider?.SourcePath;
    public string? AudioSourcePath => AudioProvider?.SourcePath;
    public string SourcePath => VideoSourcePath ?? AudioSourcePath ?? "";
    public double VideoDurationSeconds => VideoProvider?.VideoDurationSeconds ?? 0;
    public MediaInfo Info
    {
        get
        {
            var video = VideoProvider?.Info; var audio = AudioProvider?.Info;
            return new(SourcePath, video?.Width ?? 0, video?.Height ?? 0, video?.FrameCount ?? 0,
                video?.FramesPerSecond ?? 0, Math.Max(video?.DurationSeconds ?? 0, audio?.DurationSeconds ?? 0),
                audio?.SampleRate ?? 0, audio?.Channels ?? 0, audio?.AudioSamples ?? 0);
        }
    }
    public IReadOnlyList<double> FrameTimes => VideoProvider?.FrameTimes ?? Array.Empty<double>();
    public IReadOnlyList<int> Keyframes => VideoProvider?.Keyframes ?? Array.Empty<int>();
    public static DocumentMedia Open(string path, CancellationToken token = default, MediaTracks tracks = MediaTracks.Both)
    {
        using var opened = FfmsMediaSession.Open(path, token, tracks);
        var (video, audio) = opened.SeparateResources();
        try { return new(MediaProviderOwnership<IDisposable>.Own(video, audio)); }
        catch { try { video?.Dispose(); } finally { audio?.Dispose(); } throw; }
    }
    public static DocumentMedia FromVideo(IVideoProvider video) => new(MediaProviderOwnership<IDisposable>.Own(video, null));
    public DocumentMedia WithVideo(DocumentMedia? candidate) => new(_ownership.ReplaceVideo(candidate?._ownership));
    public DocumentMedia WithAudio(DocumentMedia? candidate) => new(_ownership.ReplaceAudio(candidate?._ownership));
    public void SetExternalTimecodes(VideoTimecodeMap? map) => VideoProvider?.SetExternalTimecodes(map);
    public void SetExternalKeyframes(IReadOnlyList<int>? frames) => VideoProvider?.SetExternalKeyframes(frames);
    public DecodedVideoFrame GetFrameAtTime(double seconds, byte[]? pixels = null) =>
        (VideoProvider ?? throw new InvalidOperationException("Open video first.")).GetFrameAtTime(seconds, pixels);
    public double AdjacentFrameTime(double seconds, int direction) =>
        VideoProvider?.AdjacentFrameTime(seconds, direction) ?? seconds;
    public WaveformData BuildWaveform(CancellationToken cancellationToken = default) =>
        (AudioProvider ?? throw new InvalidOperationException("Open audio first.")).BuildWaveform(cancellationToken);
    public WaveformData BuildWaveformViewport(double start, double duration, int columns, CancellationToken token) =>
        (AudioProvider ?? throw new InvalidOperationException("Open audio first.")).BuildWaveformViewport(start, duration, columns, token);
    public long TimeToAudioFrame(double seconds) => AudioProvider?.TimeToAudioFrame(seconds) ?? 0;
    public void Dispose() => _ownership.Dispose();
}
