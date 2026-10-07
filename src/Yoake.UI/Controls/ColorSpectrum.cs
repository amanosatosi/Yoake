using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Yoake.Core.Subtitles;

namespace Yoake.UI.Controls;

// HSV/H: two composited linear gradients are resolution-independent and cached
// by the drawing backend; dragging S/V never regenerates a pixel bitmap.
public sealed class ColorSpectrum : Control
{
    private double _hue,_saturation,_value;
    private double _brushHue=double.NaN;
    private bool _drag;
    private LinearGradientBrush _hueBrush=new();
    private static readonly LinearGradientBrush Black=new(){StartPoint=new(0,0,RelativeUnit.Relative),EndPoint=new(0,1,RelativeUnit.Relative),GradientStops=[new(Colors.Transparent,0),new(Colors.Black,1)]};
    public event EventHandler? ValueChanged;
    public double Hue=>_hue;
    public ColorSpectrum(){Focusable=true;MinWidth=256;MinHeight=256;SetColor(new(255,255,255,0));}
    public void SetColor(AssColor color)
    {
        var hsv=ColorSpace.Hsv(color);
        // Keep a useful hue while choosing gray or black.
        if(hsv.Saturation>0&&hsv.Component>0)_hue=hsv.Hue;
        _saturation=hsv.Saturation;_value=hsv.Component;UpdateBrush();InvalidateVisual();
    }
    public void SetHue(double hue){_hue=hue;UpdateBrush();InvalidateVisual();ValueChanged?.Invoke(this,EventArgs.Empty);}
    public AssColor Color(byte alpha)=>ColorSpace.FromHsv(_hue,_saturation,_value,alpha);
    private void UpdateBrush(){if(_brushHue==_hue)return;_brushHue=_hue;var c=ColorSpace.FromHsv(_hue,1,1);_hueBrush=new(){StartPoint=new(0,0,RelativeUnit.Relative),EndPoint=new(1,0,RelativeUnit.Relative),GradientStops=[new(Colors.White,0),new(Avalonia.Media.Color.FromRgb(c.Red,c.Green,c.Blue),1)]};}
    public override void Render(DrawingContext context)
    {
        context.FillRectangle(_hueBrush,new Rect(Bounds.Size));context.FillRectangle(Black,new Rect(Bounds.Size));
        var point=new Point(_saturation*Bounds.Width,(1-_value)*Bounds.Height);
        context.DrawEllipse(null,new Pen(Brushes.Black,3),point,5,5);context.DrawEllipse(null,new Pen(Brushes.White,1),point,5,5);
    }
    private void Move(Point p){_saturation=Math.Clamp(p.X/Math.Max(1,Bounds.Width),0,1);_value=1-Math.Clamp(p.Y/Math.Max(1,Bounds.Height),0,1);InvalidateVisual();ValueChanged?.Invoke(this,EventArgs.Empty);}
    protected override void OnPointerPressed(PointerPressedEventArgs e){base.OnPointerPressed(e);if(!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)return;Focus();_drag=true;e.Pointer.Capture(this);Move(e.GetPosition(this));e.Handled=true;}
    protected override void OnPointerMoved(PointerEventArgs e){base.OnPointerMoved(e);if(_drag)Move(e.GetPosition(this));}
    protected override void OnPointerReleased(PointerReleasedEventArgs e){base.OnPointerReleased(e);_drag=false;e.Pointer.Capture(null);}
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e){base.OnPointerCaptureLost(e);_drag=false;}
    protected override void OnKeyDown(KeyEventArgs e)
    {
        var step=e.KeyModifiers.HasFlag(KeyModifiers.Shift)?0.1:0.01;
        if(e.Key is Key.Left or Key.Right)_saturation=Math.Clamp(_saturation+(e.Key==Key.Left?-step:step),0,1);
        else if(e.Key is Key.Up or Key.Down)_value=Math.Clamp(_value+(e.Key==Key.Down?-step:step),0,1);
        else{base.OnKeyDown(e);return;}
        InvalidateVisual();ValueChanged?.Invoke(this,EventArgs.Empty);e.Handled=true;
    }
}
