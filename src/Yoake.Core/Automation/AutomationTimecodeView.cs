namespace Yoake.Core.Automation;

// Source START rounding over a snapshot of the central media timeline. No clock.
public sealed class AutomationTimecodeView
{
    private readonly int[] _times;
    private const long Denominator = 1_000_000_000;
    private readonly Int128 _numerator, _last;
    // Input integration only: external files supply the release's V1 assumed
    // extrapolation rate/anchor. The frozen START conversion below is unchanged.
    public AutomationTimecodeView(Yoake.Core.Media.ExternalTimecodes external)
    {
        ArgumentNullException.ThrowIfNull(external);
        _times=external.Milliseconds.Select(value=>checked((int)value)).ToArray();
        _numerator=external.Numerator;_last=external.Last;
    }
    public AutomationTimecodeView(IReadOnlyList<double> seconds, double framesPerSecond)
    {
        if (!double.IsFinite(framesPerSecond) || framesPerSecond <= 0) throw new ArgumentException("Frame rate must be positive.");
        _times = seconds.Select(s => checked((int)Math.Round(s * 1000, MidpointRounding.AwayFromZero))).ToArray();
        if (_times.Length == 0 || _times.Where((t, i) => i > 0 && t < _times[i - 1]).Any()) throw new ArgumentException("Timecodes must be a nonempty ordered timeline.");
        var first = _times[0];
        for (var i = 0; i < _times.Length; i++) _times[i] = checked(_times[i] - first);
        if (_times.Length > 1)
        {
            if (_times[^1] == 0) throw new ArgumentException("Timecodes must not all be identical.");
            // The release FFMS2 provider constructs Framerate(timecodes), whose
            // extrapolation uses the normalized timeline's average rate.
            _last = (Int128)(_times.Length - 1) * Denominator * 1000;
            _numerator = _last / _times[^1];
        }
        else _numerator = checked((long)(framesPerSecond * Denominator));
        if (_numerator <= 0) throw new ArgumentException("Frame rate is too small.");
    }
    public int FrameFromMilliseconds(int milliseconds)
    {
        var time = (long)milliseconds - 1;
        if (time < 0) return checked((int)((time * _numerator / Denominator - 999) / 1000) + 1);
        if (time > _times[^1]) return checked(_times.Length + (int)((time * _numerator - _last + Denominator - 1) / Denominator / 1000));
        var lo = 0; var hi = _times.Length;
        while (lo < hi) { var mid = lo + (hi - lo) / 2; if (_times[mid] <= time) lo = mid + 1; else hi = mid; }
        return lo;
    }
    public int MillisecondsFromFrame(int frame)
    {
        var previous = ExactTime((long)frame - 1); var current = ExactTime(frame);
        return checked((int)(previous + (current - previous + 1) / 2));
    }
    private long ExactTime(long frame) => frame < 0 ? checked((long)(frame * Denominator * (Int128)1000 / _numerator))
        : frame >= _times.Length ? checked((long)(((frame - _times.Length + 1) * Denominator * (Int128)1000 + _last + _numerator / 2) / _numerator))
        : _times[(int)frame];
}
