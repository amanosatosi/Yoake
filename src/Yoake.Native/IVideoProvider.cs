using Yoake.Core.Media;

namespace Yoake.Native;

/// <summary>Document-owned source frames and metadata. No playback clock or presentation state.</summary>
public interface IVideoProvider : IDisposable
{
    string SourcePath { get; }
    string DecoderName { get; }
    bool IsDummy { get; }
    double DisplayAspectRatio { get; }
    bool HasVideo { get; }
    MediaInfo Info { get; }
    MediaInfo SourceInfo { get; }
    double VideoDurationSeconds { get; }
    IReadOnlyList<double> FrameTimes { get; }
    IReadOnlyList<int> Keyframes { get; }
    void SetExternalTimecodes(VideoTimecodeMap? map);
    void SetExternalKeyframes(IReadOnlyList<int>? frames);
    DecodedVideoFrame GetFrame(int frame, byte[]? pixels = null);
    DecodedVideoFrame GetFrameAtTime(double seconds, byte[]? pixels = null);
    double AdjacentFrameTime(double seconds, int direction);
}
