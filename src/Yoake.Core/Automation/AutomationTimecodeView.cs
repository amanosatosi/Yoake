namespace Yoake.Core.Automation;

// Source START rounding over a snapshot of the central media timeline. No clock.
public sealed class AutomationTimecodeView
{
    private readonly int[] _times;
    private readonly double _fps;
    public AutomationTimecodeView(IReadOnlyList<double> seconds, double framesPerSecond)
    {
        if (!double.IsFinite(framesPerSecond) || framesPerSecond <= 0) throw new ArgumentException("Frame rate must be positive.");
        _fps = framesPerSecond;
        _times = seconds.Select(s => checked((int)Math.Round(s * 1000, MidpointRounding.AwayFromZero))).ToArray();
        if (_times.Length == 0 || _times.Where((t, i) => i > 0 && t < _times[i - 1]).Any()) throw new ArgumentException("Timecodes must be a nonempty ordered timeline.");
    }
    public int FrameFromMilliseconds(int milliseconds)
    {
        var time = (long)milliseconds - 1;
        if (time < 0) return checked((int)Math.Floor(time * _fps / 1000) + 1);
        if (time > _times[^1]) return checked(_times.Length + (int)Math.Floor((time - _times[^1]) * _fps / 1000));
        var lo = 0; var hi = _times.Length;
        while (lo < hi) { var mid = lo + (hi - lo) / 2; if (_times[mid] <= time) lo = mid + 1; else hi = mid; }
        return lo;
    }
    public int MillisecondsFromFrame(int frame)
    {
        var previous = ExactTime((long)frame - 1); var current = ExactTime(frame);
        return checked((int)(previous + (current - previous + 1) / 2));
    }
    private long ExactTime(long frame) => frame < 0 ? (long)(frame * 1000 / _fps)
        : frame >= _times.Length ? _times[^1] + (long)Math.Round((frame - _times.Length + 1) * 1000 / _fps, MidpointRounding.AwayFromZero)
        : _times[(int)frame];
}
