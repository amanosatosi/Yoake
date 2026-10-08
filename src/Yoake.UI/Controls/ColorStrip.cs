using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Yoake.UI.Controls;

// Transparent input/marker over the gradient. No templated Slider or accent thumb.
public sealed class ColorStrip : Control
{
    public static readonly StyledProperty<double> ValueProperty=AvaloniaProperty.Register<ColorStrip,double>(nameof(Value));
    public static readonly StyledProperty<double> MaximumProperty=AvaloniaProperty.Register<ColorStrip,double>(nameof(Maximum),255);
    public double Value {get=>GetValue(ValueProperty);set=>SetValue(ValueProperty,Math.Clamp(value,0,Maximum));}
    public double Maximum {get=>GetValue(MaximumProperty);set=>SetValue(MaximumProperty,value);}
    private bool _drag;
    private double _original;
    private IPointer? _pointer;
    public ColorStrip(){Focusable=true;Width=24;MinHeight=40;Cursor=new Cursor(StandardCursorType.Cross);}
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e){base.OnPropertyChanged(e);InvalidateVisual();}
    public override void Render(DrawingContext context)
    {
        var y=Math.Clamp(Value/Math.Max(1,Maximum)*Bounds.Height,1,Math.Max(1,Bounds.Height-1));
        context.DrawLine(new Pen(Brushes.Black,3),new(0,y),new(Bounds.Width,y));context.DrawLine(new Pen(Brushes.White,1),new(0,y),new(Bounds.Width,y));
        if(IsKeyboardFocusWithin)context.DrawRectangle(null,new Pen(Brushes.White,1,DashStyle.Dot),new Rect(1,1,Math.Max(0,Bounds.Width-2),Math.Max(0,Bounds.Height-2)));
    }
    private void Move(Point point)=>Value=point.Y/Math.Max(1,Bounds.Height)*Maximum;
    protected override void OnPointerPressed(PointerPressedEventArgs e){base.OnPointerPressed(e);if(!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)return;Focus();_original=Value;_drag=true;_pointer=e.Pointer;e.Pointer.Capture(this);Move(e.GetPosition(this));e.Handled=true;}
    protected override void OnPointerMoved(PointerEventArgs e){base.OnPointerMoved(e);if(_drag)Move(e.GetPosition(this));}
    protected override void OnPointerReleased(PointerReleasedEventArgs e){base.OnPointerReleased(e);if(!_drag)return;Move(e.GetPosition(this));_drag=false;_pointer=null;e.Pointer.Capture(null);e.Handled=true;}
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e){base.OnPointerCaptureLost(e);if(_drag){_drag=false;_pointer=null;Value=_original;}}
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if(e.Key==Key.Escape&&_drag){_drag=false;Value=_original;_pointer?.Capture(null);_pointer=null;e.Handled=true;return;}
        var step=e.KeyModifiers.HasFlag(KeyModifiers.Shift)?10:1;
        switch(e.Key){case Key.Up:case Key.Left:Value-=step;break;case Key.Down:case Key.Right:Value+=step;break;case Key.Home:Value=0;break;case Key.End:Value=Maximum;break;default:base.OnKeyDown(e);return;}e.Handled=true;
    }
}
