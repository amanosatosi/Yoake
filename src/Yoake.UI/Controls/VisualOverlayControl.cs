using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System.ComponentModel;
using Yoake.Core.Subtitles;
using Yoake.UI.ViewModels;

namespace Yoake.UI.Controls;

public sealed class VisualOverlayControl : Control
{
    public static readonly StyledProperty<MainWindowViewModel?> ModelProperty=AvaloniaProperty.Register<VisualOverlayControl,MainWindowViewModel?>(nameof(Model));
    public MainWindowViewModel? Model {get=>GetValue(ModelProperty);set=>SetValue(ModelProperty,value);}
    private bool _drag;private int _handle;private AssPoint _anchor;
    public VisualOverlayControl(){Focusable=true;ClipToBounds=true;}
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if(change.Property==ModelProperty){if(change.OldValue is MainWindowViewModel old)old.PropertyChanged-=Changed;if(Model is {} model)model.PropertyChanged+=Changed;}
        InvalidateVisual();
    }
    private void Changed(object? sender,PropertyChangedEventArgs e)=>InvalidateVisual();
    private Rect VideoRect()
    {
        if(Model?.VideoFrame is not {} frame)return default;
        var scale=Math.Min(Bounds.Width/frame.PixelSize.Width,Bounds.Height/frame.PixelSize.Height);
        var width=frame.PixelSize.Width*scale;var height=frame.PixelSize.Height*scale;return new((Bounds.Width-width)/2,(Bounds.Height-height)/2,width,height);
    }
    private Point Screen(AssPoint point){var rect=VideoRect();var size=Model!.ScriptSize;return new(rect.X+point.X/size.Width*rect.Width,rect.Y+point.Y/size.Height*rect.Height);}
    private AssPoint Script(Point point){var rect=VideoRect();var size=Model!.ScriptSize;return new((point.X-rect.X)/rect.Width*size.Width,(point.Y-rect.Y)/rect.Height*size.Height);}
    private AssPoint DefaultPosition()
    {
        var model=Model!;var line=model.SelectedEvent!;var size=model.ScriptSize;var style=model.ActiveEditor?.Document.Styles.FirstOrDefault(s=>s.Name==model.EditorDraft.Style);
        var alignment=AssVisualTags.Alignment(model.VisualText,int.TryParse(style?.Get("Alignment"),out var a)?Math.Clamp(a,1,9):2);
        double Margin(string name){var v=model.EditorDraft.Values.GetValueOrDefault(name,line.Get(name));return int.TryParse(v,out var m)&&m>0?m:int.TryParse(style?.Get(name),out m)?m:20;}
        var col=(alignment-1)%3;var row=(alignment-1)/3;
        return new(col==0?Margin("MarginL"):col==2?size.Width-Margin("MarginR"):(size.Width+Margin("MarginL")-Margin("MarginR"))/2,row==0?size.Height-Margin("MarginV"):row==2?Margin("MarginV"):size.Height/2);
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);var model=Model;if(model?.SelectedEvent is not {} line||model.VideoFrame is null)return;
        var pen=new Pen(Brushes.Cyan,1);
        if(model.ActiveVisualTool=="Position")
        {
            var point=Screen(AssVisualTags.PositionAtTime(model.VisualText,(long)(model.CurrentTimeSeconds*1000)-(line.StartMilliseconds??0),(line.EndMilliseconds??0)-(line.StartMilliseconds??0))??DefaultPosition());context.DrawLine(pen,point-new Vector(10,0),point+new Vector(10,0));context.DrawLine(pen,point-new Vector(0,10),point+new Vector(0,10));context.DrawEllipse(null,pen,point,5,5);
        }
        else if(AssVisualTags.Clip(model.VisualText) is {} clip)
        {
            var points=clip.Points.Select(Screen).ToArray();
            if(clip.Rectangular)context.DrawRectangle(null,pen,new Rect(points[0],points[1]));
            else for(var i=1;i<points.Length;i++)context.DrawLine(pen,points[i-1],points[i]);
            foreach(var point in points)context.DrawRectangle(Brushes.Black,pen,new Rect(point-new Vector(3,3),new Size(6,6)));
        }
        else
        {
            var ready=new FormattedText("Drag to create clip",System.Globalization.CultureInfo.CurrentCulture,FlowDirection.LeftToRight,Typeface.Default,11,Brushes.Cyan);
            context.DrawText(ready,VideoRect().TopLeft+new Vector(8,8));Cursor=new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Cross);
        }
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);Focus();var model=Model;var point=e.GetPosition(this);
        if(model?.SelectedEvent is not {} line||!VideoRect().Contains(point)||!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)return;
        _anchor=Script(point);_handle=-1;
        if(model.ActiveVisualTool=="Clip"&&AssVisualTags.Clip(model.VisualText) is {} clip)
        {
            if(e.KeyModifiers.HasFlag(KeyModifiers.Shift))_handle=-2;
            for(var i=0;_handle!=-2&&i<clip.Points.Count;i++)if(Math.Sqrt(Math.Pow(Screen(clip.Points[i]).X-point.X,2)+Math.Pow(Screen(clip.Points[i]).Y-point.Y,2))<=10){_handle=i;break;}
            // Existing vector clips are edited through their handles, never
            // silently converted to rectangular clips by a miss-click.
            if(!clip.Rectangular&&_handle==-1)return;
        }
        if(model.BeginGesture(model.ActiveVisualTool=="Position"?"Position subtitle":"Edit subtitle clip")){_drag=true;model.StopPlayback();e.Pointer.Capture(this);Update(point);e.Handled=true;}
    }
    private void Update(Point point)
    {
        try{var p=Script(point);if(Model?.ActiveVisualTool=="Position")Model.UpdatePositionGesture(p.X,p.Y);else Model?.UpdateClipGesture(_handle,p,_anchor);InvalidateVisual();}
        catch(Exception e){Model?.Registry.ReportFailure("video/visual-edit",e);Cancel();}
    }
    protected override void OnPointerMoved(PointerEventArgs e){base.OnPointerMoved(e);if(_drag)Update(e.GetPosition(this));}
    protected override void OnPointerReleased(PointerReleasedEventArgs e){base.OnPointerReleased(e);if(_drag){_drag=false;Model?.EndGesture();e.Pointer.Capture(null);e.Handled=true;}}
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e){base.OnPointerCaptureLost(e);Cancel();}
    private void Cancel(){if(_drag){_drag=false;Model?.CancelGesture();InvalidateVisual();}}
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e){Cancel();base.OnDetachedFromVisualTree(e);}
}
