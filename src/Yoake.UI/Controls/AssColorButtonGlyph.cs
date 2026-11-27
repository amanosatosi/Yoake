using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Yoake.UI.Icons;

namespace Yoake.UI.Controls;

// Both imported Aegisub layers share the same 24x24 coordinate system.
// Separate PathIcons independently fit their geometry bounds, distorting
// A/B placement, outlines and shadow offsets. Draw the original layers in
// their source coordinates and source paint order instead.
public sealed class AssColorButtonGlyph : Control
{
    public static readonly StyledProperty<string> ChannelProperty=AvaloniaProperty.Register<AssColorButtonGlyph,string>(nameof(Channel),"");
    public static readonly StyledProperty<IBrush?> AccentProperty=AvaloniaProperty.Register<AssColorButtonGlyph,IBrush?>(nameof(Accent));
    public static readonly StyledProperty<IBrush?> InkProperty=AvaloniaProperty.Register<AssColorButtonGlyph,IBrush?>(nameof(Ink));
    public string Channel {get=>GetValue(ChannelProperty);set=>SetValue(ChannelProperty,value);}
    public IBrush? Accent {get=>GetValue(AccentProperty);set=>SetValue(AccentProperty,value);}
    public IBrush? Ink {get=>GetValue(InkProperty);set=>SetValue(InkProperty,value);}
    public AssColorButtonGlyph(){IsHitTestVisible=false;}
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if(change.Property==ChannelProperty||change.Property==AccentProperty||change.Property==InkProperty)InvalidateVisual();
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        (Geometry? accent,Geometry? ink)=Channel switch
        {
            "1"=>(IconGeometries.Color1Accent,IconGeometries.Color1Foreground),
            "2"=>(IconGeometries.Color2Accent,IconGeometries.Color2Foreground),
            "3"=>(IconGeometries.Color3Accent,IconGeometries.Color3Foreground),
            "4"=>(IconGeometries.Color4Accent,IconGeometries.Color4Foreground),
            _=>(null,null)
        };
        // Accent first: the foreground letters must cover the outline/shadow,
        // never the other way around. No per-layer stretching or translation.
        if(accent is not null&&Accent is {} accentBrush)context.DrawGeometry(accentBrush,null,accent);
        if(ink is not null&&Ink is {} inkBrush)context.DrawGeometry(inkBrush,null,ink);
    }
}
