using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Yoake.UI.Controls;

public sealed class AudioWaveformControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<float>?> SamplesProperty =
        AvaloniaProperty.Register<AudioWaveformControl, IReadOnlyList<float>?>(nameof(Samples));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<AudioWaveformControl, IBrush?>(nameof(Stroke));

    public IReadOnlyList<float>? Samples
    {
        get => GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SamplesProperty || change.Property == StrokeProperty)
            InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var samples = Samples;
        if (samples is null || samples.Count == 0 || Bounds.Width <= 1 || Bounds.Height <= 1)
            return;

        var brush = Stroke ?? Brushes.DodgerBlue;
        var pen = new Pen(brush, 1);
        var mid = Bounds.Height / 2;
        var width = Math.Max(1, (int)Math.Floor(Bounds.Width));
        for (var x = 0; x < width; x++)
        {
            var index = Math.Min(samples.Count - 1, (int)((long)x * samples.Count / width));
            var amplitude = Math.Clamp(samples[index], 0f, 1f) * (float)(mid - 2);
            context.DrawLine(pen, new Point(x + 0.5, mid - amplitude), new Point(x + 0.5, mid + amplitude));
        }
    }
}
