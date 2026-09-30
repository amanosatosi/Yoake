using System.Collections.Concurrent;

namespace Yoake.Core.Jobs;

public sealed record BackgroundJobHandle(Guid Id, string Name, Task Completion, Action Cancel);

public sealed class BackgroundJobService
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _jobs = new();

    public BackgroundJobHandle Run(string name, Func<CancellationToken, Task> work, CancellationToken parent = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(work);
        var id = Guid.NewGuid();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(parent);
        if (!_jobs.TryAdd(id, cts))
            throw new InvalidOperationException("Could not register background job.");

        var completion = Task.Run(async () =>
        {
            try { await work(cts.Token).ConfigureAwait(false); }
            finally
            {
                _jobs.TryRemove(id, out _);
                cts.Dispose();
            }
        }, CancellationToken.None);

        return new BackgroundJobHandle(id, name, completion, () =>
        {
            if (_jobs.TryGetValue(id, out var active)) active.Cancel();
        });
    }

    public void CancelAll()
    {
        foreach (var cts in _jobs.Values)
        {
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { }
        }
    }
}
