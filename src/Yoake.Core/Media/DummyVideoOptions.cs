using Yoake.Core.Subtitles;

namespace Yoake.Core.Media;

public sealed record DummyVideoOptions
{
    public int Width { get; init; } = 640;
    public int Height { get; init; } = 480;
    public double FramesPerSecond { get; init; } = 23.976;
    public int FrameCount { get; init; } = 40000;
    public AssColor Color { get; init; } = new(47, 163, 254, 0);
    public bool Checkerboard { get; init; }
    public double DurationSeconds => FrameCount / FramesPerSecond;
    public void Validate()
    {
        if (Width is < 1 or > 10000 || Height is < 1 or > 10000) throw new ArgumentException("Resolution must be between 1 and 10000 pixels per dimension.");
        if (!double.IsFinite(FramesPerSecond) || FramesPerSecond is < 0.1 or > 1000) throw new ArgumentException("Frame rate must be between 0.1 and 1000 fps.");
        if (FrameCount is < 2 or > 36000000) throw new ArgumentException("Duration must be between 2 and 36000000 frames.");
    }
}

public sealed class ConstantFrameTimes(int count, double fps) : IReadOnlyList<double>
{
    public int Count { get; } = count;
    public double this[int index] => index >= 0 && index < Count ? index / fps : throw new ArgumentOutOfRangeException(nameof(index));
    public IEnumerator<double> GetEnumerator() { for (var i = 0; i < Count; i++) yield return this[i]; }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
