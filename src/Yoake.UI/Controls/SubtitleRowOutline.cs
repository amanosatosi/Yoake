using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Yoake.UI.Controls;

// Paint above the cells, inside the existing row bounds. Unlike a Border
// around the content, the outline has no layout thickness or desired size.
public sealed class SubtitleRowOutline : Control
{
    public static readonly StyledProperty<IBrush?> StrokeProperty=
        AvaloniaProperty.Register<SubtitleRowOutline,IBrush?>(nameof(Stroke));

    public IBrush? Stroke {get=>GetValue(StrokeProperty);set=>SetValue(StrokeProperty,value);}
    public SubtitleRowOutline(){IsHitTestVisible=false;}
    protected override Size MeasureOverride(Size availableSize)=>default;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if(change.Property==StrokeProperty)InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if(Stroke is not {} stroke||Bounds.Width<1||Bounds.Height<1)return;
        // Inset by half the stroke so none of it is clipped at the row edge.
        context.DrawRectangle(null,new Pen(stroke,1),new Rect(.5,.5,Bounds.Width-1,Bounds.Height-1));
    }
}
