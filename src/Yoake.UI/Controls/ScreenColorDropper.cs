using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Yoake.Core.Subtitles;
using Yoake.Native;

namespace Yoake.UI.Controls;

public sealed class ScreenColorDropper : Control
{
    private WindowsScreenColor.Sample? _sample;
    private IPointer? _pointer;
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMilliseconds(32)};
    private bool _latched;
    private PixelPoint _anchor;
    private bool _moved;
    public byte Transparency {get;set;}
    public bool IsSampling=>_pointer is not null;
    public event Action<AssColor>? ColorPicked;
    public event Action<Exception>? SamplingFailed;
    public ScreenColorDropper()
    {
        Name="ScreenMagnifier";Width=58;Height=58;Focusable=true;ClipToBounds=true;
        ToolTip.SetTip(this,"Drag the eyedropper over the desktop, or click it then click a pixel. Click a magnified neighbor to choose it. Esc cancels.");
        _timer.Tick+=(_,_)=>Sample();
    }
    public void Begin(IPointer pointer)
    {
        Cancel();_pointer=pointer;_latched=false;_moved=false;Cursor=new Cursor(StandardCursorType.Cross);pointer.Capture(this);Focus();Sample();
        if(_sample is {} s)_anchor=new(s.X,s.Y);_timer.Start();
    }
    private void Sample()
    {
        if(_pointer is null)return;
        try{_sample=WindowsScreenColor.SampleAtCursor(Transparency);if(Math.Abs(_sample.X-_anchor.X)+Math.Abs(_sample.Y-_anchor.Y)>7)_moved=true;InvalidateVisual();}
        catch(Exception e){Cancel();SamplingFailed?.Invoke(e);}
    }
    public void Cancel(){var pointer=_pointer;_pointer=null;_timer.Stop();pointer?.Capture(null);Cursor=Avalonia.Input.Cursor.Default;InvalidateVisual();}
    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Black,new Rect(Bounds.Size));
        if(_sample is {} sample)
        {
            for(var y=0;y<7;y++)for(var x=0;x<7;x++){var c=sample.Pixels[y*7+x];context.FillRectangle(new SolidColorBrush(Color.FromRgb(c.Red,c.Green,c.Blue)),new Rect(1+x*8,1+y*8,8,8));}
            context.DrawRectangle(null,new Pen(Brushes.Black,3),new Rect(25,25,8,8));context.DrawRectangle(null,new Pen(Brushes.White,1),new Rect(25,25,8,8));
        }
        else{var hint=new FormattedText("7 × 7",System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,Typeface.Default,11,Brushes.White);context.DrawText(hint,new Point(12,20));}
        context.DrawRectangle(null,new Pen(IsSampling?Brushes.Yellow:Brushes.Gray,1),new Rect(0.5,0.5,57,57));
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if(e.GetCurrentPoint(this).Properties.IsRightButtonPressed){Cancel();e.Handled=true;return;}
        if(!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)return;
        if(IsSampling){_latched=true;e.Handled=true;return;}
        var p=e.GetPosition(this);if(_sample is {} sample&&p.X>=1&&p.Y>=1&&p.X<57&&p.Y<57)ColorPicked?.Invoke(sample.Pixels[Math.Clamp((int)((p.Y-1)/8),0,6)*7+Math.Clamp((int)((p.X-1)/8),0,6)] with{Transparency=Transparency});e.Handled=true;
    }
    protected override void OnPointerMoved(PointerEventArgs e){base.OnPointerMoved(e);if(IsSampling)Sample();}
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);if(!IsSampling)return;Sample();
        if(_latched||_moved){var color=_sample?.Center;Cancel();if(color is {} c)ColorPicked?.Invoke(c with{Transparency=Transparency});}
        else _latched=true;e.Handled=true;
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e){base.OnPointerCaptureLost(e);if(IsSampling)Cancel();}
    protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.Key==Key.Escape){Cancel();e.Handled=true;}}
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e){Cancel();base.OnDetachedFromVisualTree(e);}
}
