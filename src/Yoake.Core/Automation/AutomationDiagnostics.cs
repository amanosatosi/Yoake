using System.Diagnostics;

namespace Yoake.Core.Automation;

// Opt-in, invocation-owned measurements. No clocks, locks or counters on the
// disabled path. Nested timings overlap; they must not be added together.
public sealed class AutomationDiagnostics
{
    private readonly Dictionary<string, (long Count, double Milliseconds)> _samples = new(StringComparer.Ordinal);
    public long Start() => Stopwatch.GetTimestamp();
    public Scope Measure(string name) => new(this, name, Start());
    public readonly struct Scope(AutomationDiagnostics owner, string name, long started) : IDisposable
    {
        public void Dispose() => owner.Finish(name, started);
    }
    public void Finish(string name, long start, long count = 1) =>
        Record(name, Stopwatch.GetElapsedTime(start).TotalMilliseconds, count);
    public void Record(string name, double milliseconds, long count = 1)
    {
        var prior = _samples.GetValueOrDefault(name);
        _samples[name] = (prior.Count + count, prior.Milliseconds + milliseconds);
    }
    public string Report() => string.Join('\n', _samples.OrderBy(p => p.Key, StringComparer.Ordinal)
        .Select(p => FormattableString.Invariant($"{p.Key}: count={p.Value.Count}, ms={p.Value.Milliseconds:F3}")));
}
