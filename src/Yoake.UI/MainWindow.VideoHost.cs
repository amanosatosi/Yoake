using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Yoake.UI.Controls;
using Yoake.UI.ViewModels;

namespace Yoake.UI;

public partial class MainWindow
{
    public Window? DetachedVideoWindow { get; private set; }
    private bool _movingVideo;
    private Guid? _hostDocument;
    private readonly Dictionary<Guid, (PixelPoint Position, double Width, double Height)> _videoWindows = [];
    public Task SetVideoDetachedAsync(bool detached, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); MoveVideoHost(detached); return Task.CompletedTask;
    }
    private void SynchronizeVideoHost()
    {
        if (_movingVideo || _model is null) return;
        var desired = _model.IsVideoDetached && _model.CurrentVideoDetails is not null;
        if (DetachedVideoWindow is not null && _hostDocument != _model.ActiveDocumentId) MoveVideoHost(false);
        if (desired != (DetachedVideoWindow is not null)) MoveVideoHost(desired);
        if (DetachedVideoWindow is { } detached) detached.Title = "Video: " + (_model.CurrentVideoDetails?.Source ?? _model.VideoSourcePath);
        UpdateWorkspaceView();
    }
    private void MoveVideoHost(bool detached)
    {
        if (_movingVideo || detached == (DetachedVideoWindow is not null)) return;
        _movingVideo = true;
        try
        {
            VisualOverlay.CancelInput();
            if (detached)
            {
                var host = new Window { Name = "DetachedVideoWindow", Title = "Detached Video", Width = Math.Max(440, VideoRegion.Bounds.Width + 16), Height = Math.Max(320, VideoRegion.Bounds.Height + 40), MinWidth = 240, MinHeight = 180, CanResize = true, DataContext = _model, WindowStartupLocation = WindowStartupLocation.CenterOwner };
                host.AddHandler(KeyDownEvent, HandleKey, RoutingStrategies.Tunnel);
                host.Deactivated += VideoHostDeactivated; host.Closing += DetachedClosing; host.Closed += DetachedClosed;
                host.SizeChanged += DetachedResized;
                _hostDocument = _model?.ActiveDocumentId;
                if (_hostDocument is { } id && _videoWindows.TryGetValue(id, out var saved)) { host.Width = saved.Width; host.Height = saved.Height; host.WindowStartupLocation = WindowStartupLocation.Manual; host.Position = saved.Position; }
                VideoDock.Child = null; host.Content = VideoRegion; DetachedVideoWindow = host;
                try { host.Show(this); }
                catch { host.Content = null; VideoDock.Child = VideoRegion; DetachedVideoWindow = null; RemoveVideoHandlers(host); host.Close(); throw; }
            }
            else if (DetachedVideoWindow is { } host)
            {
                if (_hostDocument is { } id) _videoWindows[id] = (host.Position, host.Width, host.Height);
                host.Content = null; VideoDock.Child = VideoRegion; DetachedVideoWindow = null; _hostDocument = null;
                RemoveVideoHandlers(host); host.Close();
            }
        }
        finally { _movingVideo = false; }
    }
    private void RemoveVideoHandlers(Window host)
    {
        host.RemoveHandler(KeyDownEvent, HandleKey); host.Deactivated -= VideoHostDeactivated;
        host.Closing -= DetachedClosing; host.Closed -= DetachedClosed; host.SizeChanged -= DetachedResized;
        host.DataContext = null;
    }
    private void VideoHostDeactivated(object? sender, EventArgs e) => VisualOverlay.CancelInput();
    private void DetachedResized(object? sender, SizeChangedEventArgs e)
    {
        if (_movingVideo || _model is null) return;
        Dispatcher.UIThread.Post(() => { if (DetachedVideoWindow == sender) { _ = VideoImage.VideoArea; _model?.NotifyVideoPresentation(); } });
    }
    private void ResizeDetachedVideo(object? sender, EventArgs e)
    {
        if (DetachedVideoWindow is not { } host || _model?.CurrentVideoDetails is not { } details) return;
        var height = details.Height * _model.VideoWindowZoom / host.RenderScaling;
        var width = height * _model.VideoAspectRatio;
        host.Width = Math.Max(host.MinWidth, host.Width + width - VideoImage.Bounds.Width);
        host.Height = Math.Max(host.MinHeight, host.Height + height - VideoImage.Bounds.Height);
    }
    private async void DetachedClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_movingVideo || _model is null || !_model.IsVideoDetached) return;
        e.Cancel = true;
        await _model.Registry.InvokeAsync("video/detach", new(_model.ActiveDocumentId));
    }
    private void DetachedClosed(object? sender, EventArgs e)
    { if (!_movingVideo && ReferenceEquals(sender, DetachedVideoWindow)) MoveVideoHost(false); }
}
