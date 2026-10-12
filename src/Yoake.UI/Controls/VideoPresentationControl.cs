using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System.ComponentModel;
using Yoake.Core.Media;
using Yoake.UI.ViewModels;

namespace Yoake.UI.Controls;

/// <summary>Presentation-only image and view aids. Source frame pixels remain immutable here.</summary>
public sealed class VideoPresentationControl : Control
{
    public static readonly StyledProperty<MainWindowViewModel?> ModelProperty = AvaloniaProperty.Register<VideoPresentationControl, MainWindowViewModel?>(nameof(Model));
    public MainWindowViewModel? Model { get => GetValue(ModelProperty); set => SetValue(ModelProperty, value); }
    public Rect VideoArea => Model is { } model ? Rect(model.VideoViewport(Bounds.Width, Bounds.Height)) : default;
    internal static Rect Rect(VideoPresentation viewport) => new(viewport.X, viewport.Y, viewport.Width, viewport.Height);
    public VideoPresentationControl() { ClipToBounds = true; IsHitTestVisible = false; }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ModelProperty)
        {
            if (change.OldValue is MainWindowViewModel old) { old.PropertyChanged -= Changed; old.FrameReady -= FrameReady; }
            if (Model is { } model) { model.PropertyChanged += Changed; model.FrameReady += FrameReady; }
        }
        InvalidateVisual();
    }
    private void FrameReady(object? sender, EventArgs args) => InvalidateVisual();
    private void Changed(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName is nameof(MainWindowViewModel.VideoFrame) or nameof(MainWindowViewModel.VideoAspectRatio) or nameof(MainWindowViewModel.ShowVideoOverscan)) InvalidateVisual(); }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Model is not { VideoFrame: { } frame } model) return;
        var area = VideoArea; if (area.Width <= 0 || area.Height <= 0) return;
        context.DrawImage(frame, new Rect(frame.Size), area);
        if (!model.ShowVideoOverscan) return;
        using (context.PushClip(area))
        foreach (var (horizontal, vertical) in VideoPresentation.Overscan(model.VideoAspectRatio))
        {
            var safe = new Rect(area.X + area.Width * horizontal / 2, area.Y + area.Height * vertical / 2,
                area.Width * (1 - horizontal), area.Height * (1 - vertical));
            var curve = RoundedSafeArea(safe);
            context.DrawGeometry(new SolidColorBrush(Color.FromArgb(128, 30, 70, 200)), null,
                new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(area), curve));
        }
    }
    private static StreamGeometry RoundedSafeArea(Rect r)
    {
        var dx = r.Width / 5; var dy = r.Height / 5;
        var path = new StreamGeometry();
        using var p = path.Open(); p.BeginFigure(new(r.Left, r.Top + dy), true);
        p.CubicBezierTo(r.TopLeft, r.TopLeft, new(r.Left + dx, r.Top)); p.LineTo(new(r.Right - dx, r.Top));
        p.CubicBezierTo(r.TopRight, r.TopRight, new(r.Right, r.Top + dy)); p.LineTo(new(r.Right, r.Bottom - dy));
        p.CubicBezierTo(r.BottomRight, r.BottomRight, new(r.Right - dx, r.Bottom)); p.LineTo(new(r.Left + dx, r.Bottom));
        p.CubicBezierTo(r.BottomLeft, r.BottomLeft, new(r.Left, r.Bottom - dy)); p.EndFigure(true); return path;
    }
}
