namespace Yoake.Core.Media;

/// <summary>Independent resource lifetimes, without any timeline or editor state.</summary>
public sealed class MediaProviderOwnership<T> : IDisposable where T : class, IDisposable
{
    private sealed class Resource(T value)
    {
        private int _references = 1;
        public T Value { get; } = value;
        public void Retain()
        {
            var count = Volatile.Read(ref _references);
            while (count > 0)
            {
                var previous = Interlocked.CompareExchange(ref _references, checked(count + 1), count);
                if (previous == count) return;
                count = previous;
            }
            throw new ObjectDisposedException(nameof(MediaProviderOwnership<T>));
        }
        public void Release() { if (Interlocked.Decrement(ref _references) == 0) Value.Dispose(); }
    }

    private readonly object _gate = new();
    private Resource? _video, _audio;
    private bool _disposed;
    private MediaProviderOwnership(Resource? video, Resource? audio) { _video = video; _audio = audio; }
    // Ownership transfers only on successful construction. A combined native
    // session must first separate its handles; sharing one object in both slots
    // would prevent Video Close from releasing video while audio remains open.
    public static MediaProviderOwnership<T> Own(T? video, T? audio)
    {
        if (video is not null && ReferenceEquals(video, audio))
            throw new ArgumentException("Audio and video require separate resource ownership.");
        return new(video is null ? null : new(video), audio is null ? null : new(audio));
    }
    public T? Video { get { lock (_gate) { ThrowIfDisposed(); return _video?.Value; } } }
    public T? Audio { get { lock (_gate) { ThrowIfDisposed(); return _audio?.Value; } } }
    private Resource? Capture(bool video)
    {
        lock (_gate) { ThrowIfDisposed(); var value = video ? _video : _audio; value?.Retain(); return value; }
    }
    public MediaProviderOwnership<T> ReplaceVideo(MediaProviderOwnership<T>? candidate) => Replace(candidate, true);
    public MediaProviderOwnership<T> ReplaceAudio(MediaProviderOwnership<T>? candidate) => Replace(candidate, false);
    private MediaProviderOwnership<T> Replace(MediaProviderOwnership<T>? candidate, bool replaceVideo)
    {
        Resource? first = null;
        try
        {
            first = (replaceVideo ? candidate : this)?.Capture(true);
            var second = (replaceVideo ? this : candidate)?.Capture(false);
            return new(first, second);
        }
        catch { first?.Release(); throw; }
    }
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    public void Dispose()
    {
        Resource? video, audio;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true; video = _video; audio = _audio; _video = _audio = null;
        }
        try { video?.Release(); } finally { audio?.Release(); }
    }
}
