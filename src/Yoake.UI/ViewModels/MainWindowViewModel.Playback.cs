using System.Diagnostics;
using Yoake.Core.Subtitles;
using Yoake.Native;

namespace Yoake.UI.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly object _subtitleRendererGate = new();
    private MangetsuSubtitleRenderer? _subtitleRenderer;
    private long _subtitleRevision;
    private long _subtitleRendererRevision = -1;
    private bool _selectionFromPlayback;
    private bool _clockUpdateFromPlayback;

    private CancellationTokenSource? _playbackCancellation;
    private WindowsWaveOutStream? _audioPlayer;
    private long _playbackGeneration;
    private bool _isPlaying;

    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            if (_isPlaying == value)
                return;
            _isPlaying = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PlayPauseText));
        }
    }

    public string PlayPauseText => IsPlaying ? "Pause" : "Play";
    public bool CanPlayMedia => _media is { HasVideo: true } or { HasAudio: true };

    public async Task TogglePlaybackAsync()
    {
        if (IsPlaying)
        {
            StopPlayback();
            return;
        }

        await StartPlaybackAsync();
    }

    public async Task StartPlaybackAsync()
    {
        var session = _media;
        if (session is null || (!session.HasVideo && !session.HasAudio) || _disposed)
        {
            MediaStatus = "Open playable media first.";
            return;
        }

        StopPlayback();

        if (CurrentTimeSeconds >= MediaDurationSeconds - 0.001)
            CurrentTimeSeconds = 0;

        var start = CurrentTimeSeconds;
        var generation = Interlocked.Increment(ref _playbackGeneration);
        var cancellation = new CancellationTokenSource();
        _playbackCancellation = cancellation;
        var token = cancellation.Token;
        IsPlaying = true;

        WindowsWaveOutStream? player = null;
        Task? audioTask = null;
        long audioClockBits = BitConverter.DoubleToInt64Bits(start);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (session.HasAudio)
            {
                player = new WindowsWaveOutStream();
                _audioPlayer = player;
                audioTask = Task.Run(
                    () => player.PlayAsync(
                        session,
                        start,
                        seconds => Interlocked.Exchange(
                            ref audioClockBits,
                            BitConverter.DoubleToInt64Bits(seconds)),
                        token),
                    token);
            }

            var fps = session.Info.FramesPerSecond > 0
                ? Math.Min(60, session.Info.FramesPerSecond)
                : 30;
            var delayMs = Math.Clamp((int)Math.Round(1000d / fps), 15, 50);

            while (!token.IsCancellationRequested &&
                   generation == Volatile.Read(ref _playbackGeneration) &&
                   ReferenceEquals(session, _media))
            {
                if (audioTask is { IsCompleted: true })
                {
                    await audioTask;
                    break;
                }

                var seconds = session.HasAudio
                    ? BitConverter.Int64BitsToDouble(Interlocked.Read(ref audioClockBits))
                    : start + stopwatch.Elapsed.TotalSeconds;

                if (MediaDurationSeconds > 0 && seconds >= MediaDurationSeconds)
                {
                    seconds = MediaDurationSeconds;
                    SetPlaybackTime(seconds);
                    if (session.HasVideo)
                        await RefreshVideoFrameAsync(seconds);
                    break;
                }

                SetPlaybackTime(seconds);
                if (session.HasVideo)
                    await RefreshVideoFrameAsync(seconds);

                await Task.Delay(delayMs, token);
            }

            if (audioTask is not null && !token.IsCancellationRequested)
                await audioTask;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (!_disposed && generation == Volatile.Read(ref _playbackGeneration))
                MediaStatus = $"Playback failed: {exception.Message}";
        }
        finally
        {
            player?.Dispose();
            if (ReferenceEquals(_audioPlayer, player))
                _audioPlayer = null;

            if (ReferenceEquals(_playbackCancellation, cancellation))
            {
                _playbackCancellation = null;
                cancellation.Dispose();
            }

            if (!_disposed && generation == Volatile.Read(ref _playbackGeneration))
                IsPlaying = false;
        }
    }

    public async Task SeekPlaybackAsync(double seconds)
    {
        var resume = IsPlaying;
        StopPlayback();

        CurrentTimeSeconds = seconds;
        if (_media?.HasVideo == true)
            await RefreshVideoFrameAsync(CurrentTimeSeconds);

        if (resume)
            await StartPlaybackAsync();
    }

    public void StopPlayback()
    {
        Interlocked.Increment(ref _playbackGeneration);
        var cancellation = Interlocked.Exchange(ref _playbackCancellation, null);
        try
        {
            cancellation?.Cancel();
        }
        finally
        {
            cancellation?.Dispose();
        }

        var player = Interlocked.Exchange(ref _audioPlayer, null);
        player?.Stop();
        IsPlaying = false;
    }

    private void SetPlaybackTime(double seconds)
    {
        _clockUpdateFromPlayback = true;
        try
        {
            CurrentTimeSeconds = seconds;
        }
        finally
        {
            _clockUpdateFromPlayback = false;
        }

        UpdatePlaybackSelection(seconds);
    }

    private void UpdatePlaybackSelection(double seconds)
    {
        var milliseconds = (long)Math.Round(seconds * 1000);
        AssEvent? active = null;
        foreach (var candidate in Events)
        {
            if (candidate.StartMilliseconds is not { } start ||
                candidate.EndMilliseconds is not { } end)
            {
                continue;
            }

            if (milliseconds >= start && milliseconds < end)
            {
                active = candidate;
                break;
            }
        }

        if (active is null || ReferenceEquals(active, SelectedEvent))
            return;

        _selectionFromPlayback = true;
        try
        {
            SelectedEvent = active;
        }
        finally
        {
            _selectionFromPlayback = false;
        }
    }

    private string? CompositeSubtitles(
        DecodedVideoFrame frame,
        double seconds,
        string? assText,
        long revision)
    {
        if (string.IsNullOrEmpty(assText))
            return null;

        try
        {
            lock (_subtitleRendererGate)
            {
                if (_subtitleRenderer is null)
                {
                    _subtitleRenderer = new MangetsuSubtitleRenderer(
                        assText,
                        frame.Width,
                        frame.Height);
                    _subtitleRendererRevision = revision;
                }
                else if (_subtitleRendererRevision != revision)
                {
                    _subtitleRenderer.UpdateTrack(assText);
                    _subtitleRendererRevision = revision;
                }

                _subtitleRenderer.Composite(frame, seconds);
            }
            return null;
        }
        catch (Exception exception)
        {
            return exception.Message;
        }
    }

    private void InvalidateSubtitlePreview(bool refreshCurrentFrame)
    {
        Interlocked.Increment(ref _subtitleRevision);
        if (refreshCurrentFrame && !IsPlaying && _media?.HasVideo == true)
            _ = RefreshVideoFrameAsync(CurrentTimeSeconds);
    }

    private void DisposeSubtitleRenderer()
    {
        lock (_subtitleRendererGate)
        {
            _subtitleRenderer?.Dispose();
            _subtitleRenderer = null;
            _subtitleRendererRevision = -1;
        }
    }
}
