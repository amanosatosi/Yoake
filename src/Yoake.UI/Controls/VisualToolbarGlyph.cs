using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Yoake.UI.Controls;

// Original 18-DIP line art for the dense video toolbar. Unlike a flattened
// PathIcon, the outline, main geometry and detail/handle geometry are painted
// independently, with theme-aware semantic brushes.
public sealed class VisualToolbarGlyph : Control
{
    public static readonly StyledProperty<string> GlyphProperty = AvaloniaProperty.Register<VisualToolbarGlyph, string>(nameof(Glyph), "");
    public static readonly StyledProperty<IBrush?> AccentProperty = AvaloniaProperty.Register<VisualToolbarGlyph, IBrush?>(nameof(Accent));
    public static readonly StyledProperty<IBrush?> DetailProperty = AvaloniaProperty.Register<VisualToolbarGlyph, IBrush?>(nameof(Detail));
    public static readonly StyledProperty<IBrush?> OutlineProperty = AvaloniaProperty.Register<VisualToolbarGlyph, IBrush?>(nameof(Outline));
    public string Glyph { get=>GetValue(GlyphProperty); set=>SetValue(GlyphProperty,value); }
    public IBrush? Accent { get=>GetValue(AccentProperty); set=>SetValue(AccentProperty,value); }
    public IBrush? Detail { get=>GetValue(DetailProperty); set=>SetValue(DetailProperty,value); }
    public IBrush? Outline { get=>GetValue(OutlineProperty); set=>SetValue(OutlineProperty,value); }

    private static readonly Dictionary<string, (Geometry Main, Geometry Detail)> Shapes = new()
    {
        ["Crosshair"] = (Geometry.Parse("M9 1 V17 M1 9 H17"), Geometry.Parse("M7 7 H11 V11 H7 Z")),
        ["Position"] = (Geometry.Parse("M9 1 V17 M1 9 H17 M6 4 L9 1 L12 4 M6 14 L9 17 L12 14 M4 6 L1 9 L4 12 M14 6 L17 9 L14 12"), Geometry.Parse("M7 7 H11 V11 H7 Z")),
        ["Rotate"] = (Geometry.Parse("M9 2 A7 7 0 1 1 3 5 M9 2 L5 2 M9 2 L9 6"), Geometry.Parse("M7 7 H11 V11 H7 Z")),
        ["RotateXY"] = (Geometry.Parse("M2 9 C2 3 16 3 16 9 C16 15 2 15 2 9 M13 5 L16 9 L12 11"), Geometry.Parse("M9 2 C4 2 4 16 9 16 C14 16 14 2 9 2 M9 2 L6 5")),
        ["Scale"] = (Geometry.Parse("M3 3 H15 V15 H3 Z M6 12 L12 6"), Geometry.Parse("M1 1 H5 V5 H1 Z M13 13 H17 V17 H13 Z")),
        ["Clip"] = (Geometry.Parse("M3 3 H15 V15 H3 Z"), Geometry.Parse("M1 1 H5 V5 H1 Z M13 13 H17 V17 H13 Z M6 9 H12")),
        ["Vector"] = (Geometry.Parse("M3 13 C0 4 12 0 15 5 L15 14 Z"), Geometry.Parse("M1 11 H5 V15 H1 Z M13 3 H17 V7 H13 Z")),
        ["Track"] = (Geometry.Parse("M3 3 H7 M3 3 V7 M15 3 H11 M15 3 V7 M3 15 V11 M3 15 H7 M15 15 H11 M15 15 V11"), Geometry.Parse("M5 12 L9 7 L13 10 M9 5 V9")),
        ["Distort"] = (Geometry.Parse("M5 2 L15 5 L17 15 L2 16 Z"), Geometry.Parse("M3 1 H7 V5 H3 Z M13 3 H17 V7 H13 Z M0.8 14 H4.8 V17 H0.8 Z M14 13 H17 V17 H14 Z")),
        ["Drawing"] = (Geometry.Parse("M2 14 L6 3 C11 1 15 3 16 7 L13 15 Z"), Geometry.Parse("M1 12 H5 V16 H1 Z M4 1 H8 V5 H4 Z M12 13 H16 V17 H12 Z")),
        ["Shear"] = (Geometry.Parse("M6 3 H16 L12 15 H2 Z M4 9 H14"), Geometry.Parse("M6 1 H16 M13 0.8 L16 1 L13 3")),
        ["Shift"] = (Geometry.Parse("M3 5 H13 V15 H3 Z M6 8 H16 V2"), Geometry.Parse("M16 2 L12 2 M16 2 L16 6")),
        ["Tags"] = (Geometry.Parse("M2 4 H16 M2 9 H16 M2 14 H16"), Geometry.Parse("M6 2 V6 M12 7 V11 M8 12 V16")),
        ["MoveToggle"] = (Geometry.Parse("M3 4 H15 M3 14 H15 M12 1 L15 4 L12 7 M6 11 L3 14 L6 17"), Geometry.Parse("M3 2 V6 M15 12 V16")),
        ["Select"] = (Geometry.Parse("M2 2 L6 15 L9 10 L14 8 Z"), Geometry.Parse("M10 11 L15 16")),
        ["Line"] = (Geometry.Parse("M3 15 L15 3"), Geometry.Parse("M1 13 H5 V17 H1 Z M13 1 H17 V5 H13 Z")),
        ["Bicubic"] = (Geometry.Parse("M3 15 C15 15 3 3 15 3"), Geometry.Parse("M3 15 H13 M5 3 H15 M1 13 H5 V17 H1 Z M13 1 H17 V5 H13 Z")),
        ["Spline"] = (Geometry.Parse("M1 12 C5 2 7 2 10 9 S14 16 17 5"), Geometry.Parse("M3 7 V10 M9 6 V10 M14 10 V14")),
        ["Contour"] = (Geometry.Parse("M4 15 L4 4 L14 4 L14 11"), Geometry.Parse("M2 13 H6 V17 H2 Z M12 2 H16 V6 H12 Z M10 12 H17 M13.5 8.5 V15.5")),
        ["Convert"] = (Geometry.Parse("M2 3 V15 M11 3 C17 5 7 12 15 15"), Geometry.Parse("M5 9 H11 M9 7 L11 9 L9 11")),
        ["Insert"] = (Geometry.Parse("M2 15 L8 8 L16 3 M1 13 H5 V17 H1 Z M13 1 H17 V5 H13 Z"), Geometry.Parse("M6 8 H12 M9 5 V11")),
        ["Remove"] = (Geometry.Parse("M2 15 L8 8 L16 3 M1 13 H5 V17 H1 Z M13 1 H17 V5 H13 Z"), Geometry.Parse("M6 5 L12 11 M6 11 L12 5")),
        ["Freehand"] = (Geometry.Parse("M2 14 L5 10 L8 13 L11 6 L15 9"), Geometry.Parse("M12 3 L15 1 L17 3 L14 5 Z")),
        ["Smooth"] = (Geometry.Parse("M1 14 C5 2 8 16 12 5 L15 2"), Geometry.Parse("M12 3 L15 1 L17 3 L14 5 Z")),
        ["Inverse"] = (Geometry.Parse("M2 2 H16 V16 H2 Z M6 6 H12 V12 H6 Z"), Geometry.Parse("M2 6 L6 2 M12 16 L16 12 M2 12 L6 16 M12 2 L16 6")),
        ["Invert"] = (Geometry.Parse("M3 3 H15 V15 H3 Z M6 6 H12 V12 H6 Z"), Geometry.Parse("M4 10 L8 6 M10 12 L14 8")),
        ["NewDrawing"] = (Geometry.Parse("M2 3 H10 L14 7 V16 H2 Z M10 3 V7 H14"), Geometry.Parse("M11 12 H17 M14 9 V15")),
    };

    public VisualToolbarGlyph() { IsHitTestVisible=false; }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if(change.Property==GlyphProperty||change.Property==AccentProperty||change.Property==DetailProperty||change.Property==OutlineProperty)InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if(!Shapes.TryGetValue(Glyph,out var shape))return;
        var outline=Outline??Brushes.Black;
        void Stroke(Geometry geometry,IBrush color,double width)
        {
            context.DrawGeometry(null,new Pen(outline,width+1.1,lineCap:PenLineCap.Round,lineJoin:PenLineJoin.Round),geometry);
            context.DrawGeometry(null,new Pen(color,width,lineCap:PenLineCap.Round,lineJoin:PenLineJoin.Round),geometry);
        }
        Stroke(shape.Main,Accent??Brushes.Orange,1.45);
        Stroke(shape.Detail,Detail??Brushes.DeepSkyBlue,1.25);
    }
}
