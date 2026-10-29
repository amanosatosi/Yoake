using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Yoake.Core.Media;
using Yoake.Native;

namespace Yoake.UI.ViewModels;

// A modal authoring session, holding a lease on the existing video resource.
// Working points are transient; only Apply can mutate a subtitle document.
public sealed class MotionTrackingSession : INotifyPropertyChanged
{
    private readonly DocumentMedia _lease;
    private readonly IVideoProvider _provider;
    private readonly CancellationTokenSource _lifetime;
    private CancellationTokenSource? _work;
    private Task _pending = Task.CompletedTask;
    private readonly SortedDictionary<int, TrackingPoint> _points = [];
    private bool _closed;
    public IReadOnlyList<double> FrameTimes { get; }
    public int FirstFrame { get; }
    public int LastFrame { get; }
    public int CurrentFrame { get; private set; }
    public int ReferenceFrame { get; private set; }
    public int Width => _provider.SourceInfo.Width;
    public int Height => _provider.SourceInfo.Height;
    public double AspectRatio => _provider.DisplayAspectRatio > 0 ? _provider.DisplayAspectRatio : Width / (double)Height;
    public TrackingRegion Region { get; private set; }
    public bool Viewable { get; set; }
    public MotionModel Model { get; set; } = MotionModel.Position;
    public bool TrackClip { get; set; }
    public bool ApplyX { get; set; } = true;
    public bool ApplyY { get; set; } = true;
    public bool ApplyScale { get; set; } = true;
    public bool ApplyRotation { get; set; } = true;
    public bool Optimize { get; set; }
    public bool Busy { get; private set; }
    public string Status { get; private set; } = "Draw the pattern around a textured object. Orange is the search area.";
    public WriteableBitmap? Preview { get; private set; }
    public IReadOnlyList<TrackingPoint> Points => _points.Values.ToArray();
    public TrackingPoint? CurrentPoint => _points.TryGetValue(CurrentFrame, out var point) ? point : null;
    public bool CanApply => !Busy && _points.Count >= 2 && Enumerable.Range(FirstFrame, LastFrame - FirstFrame + 1).All(f => _points.TryGetValue(f, out var p) && p.Confidence >= .65);
    public event PropertyChangedEventHandler? PropertyChanged;
    public MotionTrackingSession(DocumentMedia media, IReadOnlyList<double> times, int current, long start, long end, CancellationToken parent)
    {
        _lease = media.WithAudio(null); _provider = _lease.VideoProvider!;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(parent); FrameTimes = times.ToArray();
        FirstFrame = Math.Clamp(FrameNavigation.AtTime(FrameTimes, start / 1000d), 0, FrameTimes.Count - 1);
        LastFrame = Math.Clamp(FrameNavigation.AtTime(FrameTimes, end / 1000d) + 1, FirstFrame, FrameTimes.Count - 1);
        CurrentFrame = ReferenceFrame = Math.Clamp(current, FirstFrame, LastFrame);
        var w = Math.Min(32, Width); var h = Math.Min(32, Height);
        Region = new(Width / 2d, Height / 2d, w, h, Math.Min(96, Math.Max(w + 4, Width)), Math.Min(96, Math.Max(h + 4, Height)));
        _points[ReferenceFrame] = new(ReferenceFrame, Region.X, Region.Y);
    }
    public Task NavigateAsync(int frame) => RunAsync(async token =>
    {
        CurrentFrame = Math.Clamp(frame, FirstFrame, LastFrame);
        var image = await Task.Run(() => { token.ThrowIfCancellationRequested(); return _provider.GetFrame(CurrentFrame); }, token);
        token.ThrowIfCancellationRequested(); Publish(image); Notify();
    });
    public void SetRegion(TrackingRegion region)
    {
        if (Busy) return; region.Validate(Width, Height); Region = region;
        _points[CurrentFrame] = new(CurrentFrame, region.X, region.Y);
        Status = "Pattern corrected at this frame. Track either direction to replace that section."; Notify();
    }
    public void Reset()
    {
        if (Busy) return;
        ReferenceFrame = CurrentFrame;
        var point = CurrentPoint;
        if (point is {} p) Region = Region with { X = p.X, Y = p.Y };
        _points.Clear(); _points[CurrentFrame] = new(CurrentFrame, Region.X, Region.Y);
        Status = "Reference set. Draw or adjust the pattern, then track forward and backward."; Notify();
    }
    public Task TrackAsync(int direction, bool singleFrame = false) => RunAsync(async token =>
    {
        direction = Math.Sign(direction); if (direction == 0) return;
        var first = CurrentFrame;
        var region = CurrentPoint is {} point ? Region with { X = point.X, Y = point.Y } : Region;
        region.Validate(Width, Height);
        var viewable = Viewable;
        // Retain the opposite section; replace stale samples in this direction.
        foreach (var frame in _points.Keys.Where(f => direction > 0 ? f > first : f < first).ToArray()) _points.Remove(frame);
        var initial = CurrentPoint ?? new TrackingPoint(first, region.X, region.Y);
        _points[first] = initial;
        await Task.Run(async () =>
        {
            token.ThrowIfCancellationRequested();
            var image = _provider.GetFrame(first);
            var previous = initial;
            var model = Model;
            var elapsed = Stopwatch.StartNew();
            for (var frame = first + direction; frame >= FirstFrame && frame <= LastFrame; frame += direction)
            {
                token.ThrowIfCancellationRequested(); var nextImage = _provider.GetFrame(frame); token.ThrowIfCancellationRequested();
                var tracked = OpenCvMotionTracker.Track(image, nextImage, region, previous, frame, model, .75, token);
                image = nextImage;
                var show = singleFrame || viewable || elapsed.ElapsedMilliseconds >= 120 || tracked.Confidence < .65 || frame == FirstFrame || frame == LastFrame;
                var captured = image;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (token.IsCancellationRequested || _closed) return;
                    _points[tracked.Frame] = tracked;
                    if (show)
                    {
                        CurrentFrame = tracked.Frame; Publish(captured);
                        Status = tracked.Confidence < .65 ? $"Tracking lost at frame {tracked.Frame} ({tracked.Confidence:P0}). Correct the pattern here and restart." : $"Frame {tracked.Frame} · confidence {tracked.Confidence:P0} · {_points.Count} points";
                        Notify();
                    }
                });
                if (tracked.Confidence < .65) break;
                previous = tracked;
                if (show) elapsed.Restart();
                if (singleFrame) break;
                if (viewable) await Task.Delay(40, token);
            }
        }, token);
        Notify();
    });
    private Task RunAsync(Func<CancellationToken, Task> operation)
    {
        if (Busy || _closed) return Task.CompletedTask;
        _pending = RunCoreAsync(operation); return _pending;
    }
    private async Task RunCoreAsync(Func<CancellationToken, Task> operation)
    {
        using var work = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); _work = work; Busy = true; Notify();
        try { await operation(work.Token); }
        catch (OperationCanceledException) when (work.IsCancellationRequested) { Status = "Stopped. Retained points can be inspected; correct a frame and restart to continue."; }
        catch (Exception error) { Status = error.Message; throw; }
        finally { _work = null; Busy = false; Notify(); }
    }
    public void Stop() => _work?.Cancel();
    public Task ApplyAsync(Func<CancellationToken, Task> prepareAndPublish) => RunAsync(prepareAndPublish);
    public async Task CloseAsync()
    {
        if (_closed) return; _closed = true; _lifetime.Cancel();
        try { await _pending; }
        catch (Exception) { /* The command registry reports the original operation failure. */ }
        finally { Preview?.Dispose(); Preview = null; await Task.Run(_lease.Dispose); _lifetime.Dispose(); }
    }
    private void Publish(DecodedVideoFrame frame)
    {
        if (_closed) return;
        var old = Preview; Preview = MainWindowViewModel.CreateBitmap(frame); old?.Dispose();
    }
    private void Notify() => PropertyChanged?.Invoke(this, new(null));
}
