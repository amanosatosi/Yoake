using Avalonia;
using Avalonia.Controls;

namespace Yoake.UI.Controls;

/// <summary>Primary tools stay left; contextual tools sit against the right
/// edge. When space is insufficient, the contextual group gets its own row.</summary>
public sealed class VisualToolbarLayout : Panel
{
    private const double Gap=4;
    protected override Size MeasureOverride(Size availableSize)
    {
        if(Children.Count!=2)return base.MeasureOverride(availableSize);
        var primary=Children[0];var context=Children[1];
        var limit=new Size(availableSize.Width,double.PositiveInfinity);
        primary.Measure(limit);context.Measure(limit);
        var a=primary.DesiredSize;var b=context.DesiredSize;
        var gap=b.Width>0?Gap:0;
        return a.Width+gap+b.Width<=availableSize.Width
            ?new Size(a.Width+gap+b.Width,Math.Max(a.Height,b.Height))
            :new Size(Math.Max(a.Width,b.Width),a.Height+b.Height);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        if(Children.Count!=2)return base.ArrangeOverride(finalSize);
        var primary=Children[0];var context=Children[1];
        var a=primary.DesiredSize;var b=context.DesiredSize;
        var separate=a.Width+(b.Width>0?Gap:0)+b.Width>finalSize.Width;
        primary.Arrange(new Rect(0,0,Math.Min(a.Width,finalSize.Width),a.Height));
        var width=Math.Min(b.Width,finalSize.Width);
        context.Arrange(new Rect(Math.Max(0,finalSize.Width-width),separate?a.Height:0,width,b.Height));
        return finalSize;
    }
}
