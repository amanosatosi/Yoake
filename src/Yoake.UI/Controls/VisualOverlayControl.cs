using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System.ComponentModel;
using System.Collections.Specialized;
using Yoake.Core.Subtitles;
using Yoake.UI.ViewModels;
using Yoake.UI.VisualTools;

namespace Yoake.UI.Controls;

// Input/capture and coordinate host. Tools own their features and interpretation.
public sealed class VisualOverlayControl : Control
{
    public static readonly StyledProperty<MainWindowViewModel?> ModelProperty=AvaloniaProperty.Register<VisualOverlayControl,MainWindowViewModel?>(nameof(Model));
    public MainWindowViewModel? Model {get=>GetValue(ModelProperty);set=>SetValue(ModelProperty,value);}
    private readonly Dictionary<string,IVisualTool> _tools=new()
    {
        ["Crosshair"]=new CrosshairTool(),["Position"]=new PositionTool(),["RotateZ"]=new RotateZTool(),["RotateXY"]=new RotateXYTool(),
        ["Scale"]=new ScaleTool(),["Clip"]=new RectangleClipTool(),["VectorClip"]=new VectorClipTool(),["Distort"]=new DistortTool()
    };
    private IVisualTool? _tool;
    private VisualToolContext? _context;
    private VisualPointer _pointer;
    private IPointer? _capture;
    private bool _inside;
    private StandardCursorType? _cursorType;
    public int RenderedHandles {get;private set;}
    public string? VectorPreviewDrawing=>(_tool as VectorClipTool)?.PreviewDrawing;
    public string? RenderedTool {get;private set;}
    public VisualOverlayControl(){Focusable=true;ClipToBounds=true;}
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if(change.Property==ModelProperty)
        {
            ReleaseCapture();_tool?.Cancel();if(change.OldValue is MainWindowViewModel old){old.CancelGesture();old.PropertyChanged-=Changed;old.GestureEnded-=GestureEnded;if(old.SelectedEvents is INotifyCollectionChanged collection)collection.CollectionChanged-=SelectionChanged;}
            if(Model is {} model){model.PropertyChanged+=Changed;model.GestureEnded+=GestureEnded;if(model.SelectedEvents is INotifyCollectionChanged collection)collection.CollectionChanged+=SelectionChanged;}
            Refresh();
        }
        if(change.Property==BoundsProperty)Refresh();
    }
    private void SelectionChanged(object? sender,NotifyCollectionChangedEventArgs e){Cancel();Refresh();}
    private void GestureEnded(object? sender,EventArgs e){ReleaseCapture();_tool?.Cancel();Refresh();}
    private void Changed(object? sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName is nameof(MainWindowViewModel.ActiveVisualTool) or nameof(MainWindowViewModel.VectorMode) or nameof(MainWindowViewModel.SelectedEvent) or nameof(MainWindowViewModel.Events))
        {ReleaseCapture();_tool?.Cancel();}
        if(e.PropertyName is nameof(MainWindowViewModel.PreviewRevision) or nameof(MainWindowViewModel.VideoFrame) or nameof(MainWindowViewModel.CurrentTimeSeconds) or nameof(MainWindowViewModel.ActiveVisualTool) or nameof(MainWindowViewModel.VectorMode) or nameof(MainWindowViewModel.SelectedEvent) or nameof(MainWindowViewModel.Events) or nameof(MainWindowViewModel.EditorDraft) or nameof(MainWindowViewModel.VisualBounds) or nameof(MainWindowViewModel.VisualBoundsPending))Refresh();
    }
    private void Refresh()
    {
        if(Model is not {} model||model.VideoFrame is not {} frame){_context=null;InvalidateVisual();return;}
        var scale=Math.Min(Bounds.Width/frame.PixelSize.Width,Bounds.Height/frame.PixelSize.Height);
        var width=frame.PixelSize.Width*scale;var height=frame.PixelSize.Height*scale;
        _context=new(model,new((Bounds.Width-width)/2,(Bounds.Height-height)/2,width,height),model.VisibleVisualLines());
        _tools.TryGetValue(model.ActiveVisualTool,out _tool);model.RequestVisualBounds();InvalidateVisual();
    }
    public override void Render(DrawingContext context)
    {
        // Keep the entire video an input surface even before any clip/handles exist.
        context.FillRectangle(Brushes.Transparent,new Rect(Bounds.Size));
        base.Render(context);RenderedHandles=0;RenderedTool=null;
        if(_context is not {} state||_tool is null||state.Video.Width<=0||state.Video.Height<=0)return;
        var canvas=new VisualCanvas(context,state,_pointer,_inside);using(context.PushClip(state.Video))_tool.Render(canvas);
        RenderedHandles=canvas.HandleCount;RenderedTool=Model?.ActiveVisualTool;
    }
    private VisualPointer Pointer(PointerEventArgs e)=>new(e.GetPosition(this),_context?.Script(e.GetPosition(this))??default,e.KeyModifiers,e is PointerPressedEventArgs press?press.ClickCount:0);
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);Focus();_pointer=Pointer(e);_inside=true;
        if(_context is not {} state||_tool is null||!state.Video.Contains(_pointer.Screen)||!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)return;
        try{if(_tool.Press(state,_pointer)){_capture=e.Pointer;e.Pointer.Capture(this);e.Handled=true;}InvalidateVisual();}catch(Exception ex){Fail(ex);}
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);_pointer=Pointer(e);_inside=_context?.Video.Contains(_pointer.Screen)==true;
        try{if(_capture is not null&&_context is {} state)_tool?.Move(state,_pointer);var cursorType=_tool?.Cursor(_context,_pointer)??StandardCursorType.Cross;if(cursorType!=_cursorType){_cursorType=cursorType;Cursor=new Cursor(cursorType);}InvalidateVisual();}catch(Exception ex){Fail(ex);}
    }
    protected override void OnPointerExited(PointerEventArgs e){base.OnPointerExited(e);if(_capture is null)_inside=false;InvalidateVisual();}
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);if(_capture is null)return;
        try{if(_context is {} state)_tool?.Release(state,Pointer(e));ReleaseCapture();if(Model?.HasGesture==true)Model.EndGesture();e.Handled=true;InvalidateVisual();}catch(Exception ex){Fail(ex);}
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e){base.OnPointerCaptureLost(e);if(_capture is not null)Cancel();}
    protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.Key==Key.Escape){Cancel();e.Handled=true;}else Modifiers(e.KeyModifiers);}
    protected override void OnKeyUp(KeyEventArgs e){base.OnKeyUp(e);Modifiers(e.KeyModifiers);}
    private void Modifiers(KeyModifiers modifiers){_pointer=_pointer with{Modifiers=modifiers};try{if(_capture is not null&&_context is {} state)_tool?.Move(state,_pointer);InvalidateVisual();}catch(Exception ex){Fail(ex);}}
    private void ReleaseCapture(){var pointer=_capture;_capture=null;pointer?.Capture(null);}
    private void Cancel(){ReleaseCapture();_tool?.Cancel();Model?.CancelGesture();InvalidateVisual();}
    private void Fail(Exception exception){Model?.Registry.ReportFailure("video/visual-edit",exception);Cancel();}
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e){Cancel();base.OnDetachedFromVisualTree(e);}
}
