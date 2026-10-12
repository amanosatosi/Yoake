using System.Diagnostics;
using Yoake.Core.Audio;
using Yoake.Core.Commands;
using Yoake.Core.Subtitles;
using Yoake.Native;

namespace Yoake.UI.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly object _subtitleRendererGate = new();
    private MangetsuSubtitleRenderer? _subtitleRenderer;
    private long _subtitleRevision;
    private long _subtitleRendererRevision = -1;
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
            _registry.NotifyStateChanged();
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

    private double? _activeVideoPlaybackEnd;
    public double? ActiveVideoPlaybackEndSeconds=>_activeVideoPlaybackEnd;
    private void SetVideoPlaybackEnd(double? end)
    {if(_activeVideoPlaybackEnd==end)return;_activeVideoPlaybackEnd=end;OnPropertyChanged(nameof(ActiveVideoPlaybackEndSeconds));}
    public Task StartPlaybackAsync(double? endSeconds = null)=>RunPlaybackAsync(endSeconds??(_media?.HasVideo==true?VideoDurationSeconds:null),null,CommandIds.VideoPlay);

    private async Task RunPlaybackAsync(double? endSeconds,AudioPlaybackPlan? range,string commandId)
    {
        var session = _media;
        var audio = session?.AudioProvider;
        if (session is null || (!session.HasVideo && !session.HasAudio) || _disposed)
        {
            MediaStatus = "Open playable media first.";
            return;
        }

        var playbackInfo = session.Info; var hadVideo = session.HasVideo;
        StopPlayback();
        if(range is not null)CurrentTimeSeconds=range.StartSeconds;
        else if (CurrentTimeSeconds >= (endSeconds??MediaDurationSeconds) - 0.001)
            CurrentTimeSeconds = 0;

        var start = CurrentTimeSeconds;
        var generation = Interlocked.Increment(ref _playbackGeneration);
        var cancellation = new CancellationTokenSource();
        _playbackCancellation = cancellation;
        var token = cancellation.Token;
        ActiveAudioPlayback=range;SetVideoPlaybackEnd(range is null&&hadVideo?endSeconds:null);
        IsPlaying = true;

        WindowsWaveOutStream? player = null;
        Task? audioTask = null;
        long audioClockBits = BitConverter.DoubleToInt64Bits(start);
        var stopwatch = Stopwatch.StartNew();
        double? afterAudioAnchor=null;var afterAudioElapsed=0d;

        try
        {
            // Publish the activity on the owner dispatcher before opening output.
            // An immediate Stop can cancel startup without acquiring a device.
            await Task.Yield();
            token.ThrowIfCancellationRequested();
            if (audio is not null)
            {
                player = new WindowsWaveOutStream();
                player.SetVolume(PlaybackVolume,PlaybackMuted);
                if(range is not null)player.SetEndFrame(ActiveAudioPlayback?.EndFrame??range.EndFrame);
                else if(endSeconds is {} end)player.SetEndFrame(AudioPlaybackPlan.FrameAtSeconds(end,playbackInfo.SampleRate,playbackInfo.AudioSamples));
                _audioPlayer = player;
                audioTask = Task.Run(
                    () => player.PlayAsync(
                        audio!,
                        start,
                        seconds => Interlocked.Exchange(
                            ref audioClockBits,
                            BitConverter.DoubleToInt64Bits(seconds)),
                        token,range),
                    token);
            }

            var fps = playbackInfo.FramesPerSecond > 0
                ? Math.Min(60, playbackInfo.FramesPerSecond)
                : 30;
            var delayMs = Math.Clamp((int)Math.Round(1000d / fps), 15, 50);

            while (!token.IsCancellationRequested &&
                   generation == Volatile.Read(ref _playbackGeneration) &&
                   (range is null ? ReferenceEquals(session, _media) : ReferenceEquals(audio, _media?.AudioProvider)))
            {
                if (afterAudioAnchor is null&&audioTask is { IsCompleted: true })
                {
                    await audioTask;
                    var audioEnd=BitConverter.Int64BitsToDouble(Interlocked.Read(ref audioClockBits));
                    if(range is not null||!hadVideo)
                    {
                        SetPlaybackTime(audioEnd);if(_media?.HasVideo==true)await RefreshVideoFrameAsync(CurrentTimeSeconds);break;
                    }
                    // Continue the same central clock silently after audio EOF.
                    // In particular, starting video beyond audio EOF must not
                    // stop immediately or rewind to the shorter audio endpoint.
                    afterAudioAnchor=Math.Max(start,audioEnd);afterAudioElapsed=stopwatch.Elapsed.TotalSeconds;
                }

                var seconds = afterAudioAnchor is {} anchor?anchor+stopwatch.Elapsed.TotalSeconds-afterAudioElapsed:
                    audio is not null?BitConverter.Int64BitsToDouble(Interlocked.Read(ref audioClockBits)):
                    start + stopwatch.Elapsed.TotalSeconds;

                var stopAt=ActiveAudioPlayback?.EndSeconds??endSeconds;
                if(stopAt is {} endAt&&seconds>=endAt){SetPlaybackTime(endAt);player?.Stop();break;}

                if (MediaDurationSeconds > 0 && seconds >= MediaDurationSeconds)
                {
                    seconds = MediaDurationSeconds;
                    SetPlaybackTime(seconds);
                    if (_media?.HasVideo==true)
                        await RefreshVideoFrameAsync(seconds);
                    break;
                }

                SetPlaybackTime(seconds);
                if (_media?.HasVideo==true)
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
            {MediaStatus = $"Playback failed: {exception.Message}";_registry.ReportFailure(commandId,exception);}
        }
        finally
        {
            player?.Stop();
            // Observe the decoder/output task before releasing its native state,
            // including a stop which happens before the worker opens the device.
            if(audioTask is not null)
            {
                try{await audioTask;}
                catch(OperationCanceledException){}
                catch(Exception){/* Already reported above, or belongs to canceled playback. */}
            }
            player?.Dispose();
            if (ReferenceEquals(_audioPlayer, player))
                _audioPlayer = null;

            if (ReferenceEquals(_playbackCancellation, cancellation))
            {
                _playbackCancellation = null;
                cancellation.Dispose();
            }

            if (!_disposed && generation == Volatile.Read(ref _playbackGeneration))
            {ActiveAudioPlayback=null;SetVideoPlaybackEnd(null);IsPlaying = false;}
        }
    }

    public async Task SeekPlaybackAsync(double seconds)
    {
        var resume = IsPlaying;var session=_media;var document=_activeId;
        StopPlayback();var generation=Volatile.Read(ref _playbackGeneration);

        CurrentTimeSeconds = seconds;
        if (_media?.HasVideo == true)
            await RefreshVideoFrameAsync(CurrentTimeSeconds);

        if (resume&&!_disposed&&_activeId==document&&ReferenceEquals(session,_media)&&generation==Volatile.Read(ref _playbackGeneration))
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
        ActiveAudioPlayback=null;SetVideoPlaybackEnd(null);
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

    private CancellationTokenSource? _previewDelay;
    private void InvalidateSubtitlePreview(bool refreshCurrentFrame)
    {
        Interlocked.Increment(ref _subtitleRevision);
        _previewDelay?.Cancel();_previewDelay?.Dispose();_previewDelay=null;
        if(refreshCurrentFrame&&!IsPlaying&&_media?.HasVideo==true)
        {
            var cancellation=new CancellationTokenSource();_previewDelay=cancellation;
            _=RefreshPreviewAfterDelayAsync(cancellation.Token);
        }
    }
    private async Task RefreshPreviewAfterDelayAsync(CancellationToken token)
    {
        try{await Task.Delay(30,token);await RefreshVideoFrameAsync(CurrentTimeSeconds);}catch(OperationCanceledException){}
    }

    private void DisposeSubtitleRenderer()
    {
        _=Task.Run(()=>
        {
            lock(_subtitleRendererGate)
            {
                _subtitleRenderer?.Dispose();_subtitleRenderer=null;_subtitleRendererRevision=-1;
            }
        });
    }
}
