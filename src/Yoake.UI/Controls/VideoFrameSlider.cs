using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Yoake.Core.Media;
using Yoake.UI.ViewModels;

namespace Yoake.UI.Controls;

public sealed class VideoFrameSlider : Control
{
    public static readonly StyledProperty<MainWindowViewModel?> ModelProperty=AvaloniaProperty.Register<VideoFrameSlider,MainWindowViewModel?>(nameof(Model));
    public MainWindowViewModel? Model{get=>GetValue(ModelProperty);set=>SetValue(ModelProperty,value);}
    private bool _drag,_resume;
    public int RenderedKeyframeMarks {get;private set;}
    public int KeyframeCount=>Model?.Keyframes.Count??0;
    public VideoFrameSlider(){Focusable=true;MinHeight=24;ToolTip.SetTip(this,"Seek by frame · Shift+click: nearest keyframe · Wheel: frames · Shift+wheel: keyframes");}
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);if(e.Property==ModelProperty){if(e.OldValue is MainWindowViewModel old)old.PropertyChanged-=Changed;if(Model is {} model)model.PropertyChanged+=Changed;}InvalidateVisual();
    }
    private void Changed(object? sender,PropertyChangedEventArgs e)=>InvalidateVisual();
    private double X(int frame)=>5+frame/(double)Math.Max(1,(Model?.FrameTimes.Count??1)-1)*Math.Max(1,Bounds.Width-10);
    public int FrameAt(double x,bool snap)
    {
        var frame=(int)Math.Round(Math.Clamp((x-5)/Math.Max(1,Bounds.Width-10),0,1)*Math.Max(0,(Model?.FrameTimes.Count??1)-1));
        return snap?FrameNavigation.NearestKeyframe(Model?.Keyframes??Array.Empty<int>(),frame):frame;
    }
    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent,new Rect(Bounds.Size));
        var accent=this.TryFindResource("IconAccentBrush",ActualThemeVariant,out var value)&&value is IBrush brush?brush:Brushes.DodgerBlue;
        var track=new Pen(Brushes.Gray,1);context.DrawLine(track,new(5,Bounds.Height/2),new(Math.Max(5,Bounds.Width-5),Bounds.Height/2));
        RenderedKeyframeMarks=0;
        if(Model is not {} model)return;var last=-1;
        foreach(var key in model.Keyframes){var pixel=(int)X(key);if(pixel==last)continue;last=pixel;RenderedKeyframeMarks++;context.DrawLine(new Pen(accent,1),new(pixel,2),new(pixel,8));}
        context.DrawRectangle(accent,null,new Rect(X(model.CurrentFrame)-3,7,6,Math.Max(4,Bounds.Height-10)));
        if(IsFocused)context.DrawRectangle(null,new Pen(accent,1),new Rect(Bounds.Size).Deflate(1));
    }
    private void Seek(double x,KeyModifiers modifiers){if(Model is {} model)_=model.Registry.InvokeAsync("video/seek/frame",new(),FrameAt(x,modifiers.HasFlag(KeyModifiers.Shift)));}
    protected override void OnPointerPressed(PointerPressedEventArgs e){base.OnPointerPressed(e);if(!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)return;Focus();_resume=Model?.IsPlaying==true;Model?.StopPlayback();_drag=true;e.Pointer.Capture(this);Seek(e.GetPosition(this).X,e.KeyModifiers);e.Handled=true;}
    protected override void OnPointerMoved(PointerEventArgs e){base.OnPointerMoved(e);if(_drag)Seek(e.GetPosition(this).X,e.KeyModifiers);}
    protected override void OnPointerReleased(PointerReleasedEventArgs e){base.OnPointerReleased(e);if(!_drag)return;var resume=_resume;_drag=false;_resume=false;e.Pointer.Capture(null);if(resume&&Model is {} model)_=model.Registry.InvokeAsync("video/play",new());e.Handled=true;}
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e){base.OnPointerCaptureLost(e);_drag=false;_resume=false;}
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e){base.OnPointerWheelChanged(e);if(Model is not {} model)return;var next=e.Delta.Y<0;_=model.Registry.InvokeAsync(e.KeyModifiers.HasFlag(KeyModifiers.Shift)?next?"video/frame/next-keyframe":"video/frame/previous-keyframe":next?"video/frame/next":"video/frame/previous",new());e.Handled=true;}
}
