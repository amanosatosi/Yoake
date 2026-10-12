using System.Globalization;
using Yoake.Core.Media;
using Yoake.Core.Subtitles;

namespace Yoake.Native;

public sealed class DummyVideoProvider : IVideoProvider
{
    private readonly object _gate = new();
    private readonly byte[] _pixels;
    private readonly IReadOnlyList<double> _nativeTimes;
    private VideoTimecodeMap? _external;
    private IReadOnlyList<int> _keyframes = [];
    private bool _disposed;
    public DummyVideoOptions Options { get; }
    public DummyVideoProvider(DummyVideoOptions options, CancellationToken token = default)
    {
        options.Validate(); Options = options;
        SourcePath = string.Create(CultureInfo.InvariantCulture, $"?dummy:{options.FramesPerSecond:F6}:{options.FrameCount}:{options.Width}:{options.Height}:{options.Color.Red}:{options.Color.Green}:{options.Color.Blue}:{(options.Checkerboard ? "c" : "")}");
        SourceInfo = new(SourcePath, options.Width, options.Height, options.FrameCount, options.FramesPerSecond, options.DurationSeconds, 0, 0, 0);
        _nativeTimes = new ConstantFrameTimes(options.FrameCount, options.FramesPerSecond);
        _pixels = new byte[checked(options.Width * options.Height * 4)];
        var hsl = ColorSpace.Hsl(options.Color);
        var luminance = (int)(hsl.Component * 255);
        luminance = luminance > 231 ? luminance - 24 : luminance + 24;
        var alternate = ColorSpace.FromHsl(hsl.Hue, hsl.Saturation, luminance / 255d);
        for (var y = 0; y < options.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < options.Width; x++)
            {
                var color = options.Checkerboard && ((x / 8 & 1) != (y / 8 & 1)) ? alternate : options.Color;
                var i = (y * options.Width + x) * 4;
                _pixels[i] = color.Blue; _pixels[i + 1] = color.Green; _pixels[i + 2] = color.Red; _pixels[i + 3] = 255;
            }
        }
    }
    public string SourcePath { get; }
    public string DecoderName => "Yoake internal dummy video";
    public bool IsDummy => true;
    public double DisplayAspectRatio => Options.Width / (double)Options.Height;
    public bool HasVideo { get { lock (_gate) return !_disposed; } }
    public MediaInfo SourceInfo { get; }
    public MediaInfo Info => SourceInfo with { DurationSeconds = VideoDurationSeconds };
    public double VideoDurationSeconds => Volatile.Read(ref _external)?.DurationSeconds ?? Options.DurationSeconds;
    public IReadOnlyList<double> FrameTimes => Volatile.Read(ref _external)?.FrameTimes ?? _nativeTimes;
    public IReadOnlyList<int> Keyframes => _keyframes;
    public void SetExternalTimecodes(VideoTimecodeMap? map)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (map is not null && map.FrameTimes.Count != Options.FrameCount) throw new ArgumentException("Timecodes must match the dummy frame count.");
            Volatile.Write(ref _external, map);
        }
    }
    public void SetExternalKeyframes(IReadOnlyList<int>? frames)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (frames is not null && frames.Where((f, i) => f < 0 || i > 0 && f <= frames[i - 1]).Any()) throw new ArgumentException("Keyframes must be ordered and unique.");
            _keyframes = frames is null ? [] : Array.AsReadOnly(frames.Where(f => f < Options.FrameCount).ToArray());
        }
    }
    public DecodedVideoFrame GetFrame(int frame, byte[]? pixels = null)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (frame < 0 || frame >= Options.FrameCount) throw new ArgumentOutOfRangeException(nameof(frame));
            var destination = pixels is not null && pixels.Length == _pixels.Length ? pixels : new byte[_pixels.Length];
            _pixels.CopyTo(destination, 0);
            return new(Options.Width, Options.Height, Options.Width * 4, destination);
        }
    }
    public DecodedVideoFrame GetFrameAtTime(double seconds, byte[]? pixels = null) => GetFrame(FrameNavigation.AtTime(FrameTimes, seconds), pixels);
    public double AdjacentFrameTime(double seconds, int direction) => FrameTimes[Math.Clamp(FrameNavigation.AtTime(FrameTimes, seconds) + Math.Sign(direction), 0, Options.FrameCount - 1)];
    public void Dispose() { lock (_gate) _disposed = true; }
}
