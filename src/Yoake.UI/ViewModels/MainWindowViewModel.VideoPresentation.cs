using System.Globalization;
using Yoake.Core.Commands;
using Yoake.Core.Media;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed partial class MainWindowViewModel
{
    public event EventHandler? VideoWindowZoomRequested;
    public VideoViewState? VideoView => _activeId is { } id && _documents.TryGetValue(id, out var state) ? state.VideoView : null;
    public bool IsVideoDetached => VideoView?.Detached == true;
    public bool ShowEmbeddedVideo => ShowVideo && !IsVideoDetached;
    public double VideoWindowZoom => VideoView?.EffectiveWindowZoom ?? 1;
    public string VideoZoomText => (VideoWindowZoom * 100).ToString("0.###", CultureInfo.InvariantCulture) + "%";
    public int VideoPresentationRevision { get; private set; }
    public VideoPresentation VideoViewport(double width, double height, double dpi = 1) =>
        _media?.VideoProvider is { } video && VideoView is { } state
            ? state.Layout(width, height, dpi, video.SourceInfo.Height, VideoAspectRatio) : default;
    public void NotifyVideoPresentation()
    {
        VideoPresentationRevision++;
        foreach (var name in new[] { nameof(VideoPresentationRevision), nameof(IsVideoDetached), nameof(ShowEmbeddedVideo), nameof(VideoWindowZoom), nameof(VideoZoomText) }) OnPropertyChanged(name);
        _registry.NotifyStateChanged();
    }
    public void PanVideo(double x, double y) { if (_media?.HasVideo != true) return; VideoView!.Pan(x, y); NotifyVideoPresentation(); }
    public void RestoreVideoPan(double x, double y) { VideoView?.RestorePan(x, y); NotifyVideoPresentation(); }
    public void ZoomVideoContent(double factor, double x, double y) { if (_media?.HasVideo != true) return; CancelGesture(); VideoView!.ZoomContent(VideoView.ContentZoom * factor, x, y); NotifyVideoPresentation(); }
    private void RegisterVideoPresentationCommands()
    {
        bool Applies(CommandContext context) => _media?.HasVideo == true && (context.DocumentId is null || context.DocumentId == _activeId);
        void Register(string id, string label, Func<CommandInvocation, CancellationToken, ValueTask> action, bool attached = false, Func<bool>? check = null)
        {
            _registry.Register(new(new(id, label, label, "Video presentation"), (i, token) => { token.ThrowIfCancellationRequested(); return action(i, token); },
                c => Applies(c) && (!attached || !IsVideoDetached), isChecked: check is null ? null : c => Applies(c) && check()));
            Actions[id] = new RegistryCommand(_registry, id, CurrentContext, hotkeys: () => HotkeyProfile);
        }
        void Zoom(double value, bool stop)
        {
            CancelGesture(); if (stop) StopPlayback(); VideoView!.SetWindowZoom(value); VideoWindowZoomRequested?.Invoke(this, EventArgs.Empty); NotifyVideoPresentation();
        }
        foreach (var percent in new[] { 50, 100, 200 })
        {
            var scale = percent / 100d;
            Register("video/zoom/" + percent, percent + "%", (_, _) => { Zoom(scale, true); return ValueTask.CompletedTask; }, true, () => VideoWindowZoom == scale);
        }
        foreach (var step in new[] { -1, 1 })
        {
            var delta = step;
            Register(step > 0 ? "video/zoom/in" : "video/zoom/out", step > 0 ? "Zoom In" : "Zoom Out", (_, _) =>
            { Zoom(Math.Max(.125, VideoWindowZoom + delta * .125), false); return ValueTask.CompletedTask; }, true);
        }
        Register("video/reset_pan", "Reset Video Pan", (_, _) => { CancelGesture(); VideoView!.ResetContent(); NotifyVideoPresentation(); return ValueTask.CompletedTask; });
        // The editable zoom control works in both hosts, as in VideoBox. Menu
        // presets remain attached-only; this is the semantic action for its input.
        Register("video/presentation/zoom", "Video zoom percentage", (i, _) =>
        {
            var text = (i.Parameter as string ?? "").Trim().TrimEnd('%');
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent)) throw new ArgumentException("Enter a zoom percentage.");
            Zoom(VideoViewState.ClampZoom(percent / 100), false); return ValueTask.CompletedTask;
        });
        Register("video/detach", "Detach Video", async (_, token) =>
        {
            var id = _activeId; var state = VideoView!; var target = !state.Detached;
            CancelGesture();
            var previous = state.Detached; state.Detached = target;
            try { await (Dialogs ?? throw new InvalidOperationException("Video window is unavailable.")).SetVideoDetachedAsync(target, token); }
            catch { state.Detached = previous; throw; }
            if (_activeId != id || !ReferenceEquals(state, VideoView)) return;
            if (!target) state.SetWindowZoom(state.EffectiveWindowZoom);
            state.Detached = target; NotifyVideoPresentation();
        }, check: () => IsVideoDetached);
    }
}
