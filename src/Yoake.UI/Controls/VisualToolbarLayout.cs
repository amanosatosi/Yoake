using Avalonia;
using Avalonia.Controls;

namespace Yoake.UI.Controls;

/// <summary>Keep primary tools and their context on the same stable baseline.
/// Context sits immediately after primary tools; only genuine width overflow
/// needs a second row. Switching ordinary tools must not resize the viewport.</summary>
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
        var baseline=Math.Max(MinHeight,a.Height);
        return a.Width+gap+b.Width<=availableSize.Width
            ?new Size(a.Width+gap+b.Width,Math.Max(baseline,b.Height))
            :new Size(Math.Max(a.Width,b.Width),baseline+b.Height);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        if(Children.Count!=2)return base.ArrangeOverride(finalSize);
        var primary=Children[0];var context=Children[1];
        var a=primary.DesiredSize;var b=context.DesiredSize;
        var gap=b.Width>0?Gap:0;
        var separate=a.Width+gap+b.Width>finalSize.Width;
        var baseline=Math.Max(MinHeight,a.Height);
        var primaryHeight=separate?baseline:Math.Max(baseline,b.Height);
        // The primary WrapPanel has the same minimum height as every context
        // dropdown. It never follows the active tool's content size.
        primary.Arrange(new Rect(0,0,Math.Min(a.Width,finalSize.Width),primaryHeight));
        var width=Math.Min(b.Width,finalSize.Width);
        var x=separate?0:Math.Min(a.Width+gap,Math.Max(0,finalSize.Width-width));
        var y=separate?baseline:Math.Max(0,(primaryHeight-b.Height)/2);
        context.Arrange(new Rect(x,y,width,b.Height));
        return finalSize;
    }
}
