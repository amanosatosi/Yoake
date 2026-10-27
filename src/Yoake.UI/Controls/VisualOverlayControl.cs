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
    private Point? _lastPointerPosition;
    private Guid? _pointerDocument;
    private IPointer? _capture;
    private bool _inside;
    private StandardCursorType? _cursorType;
    private Point? _panPointer;
    private (double X, double Y)? _panCheckpoint;
    private int _panProviderRevision;
    public bool IsPanning => _panPointer is not null;
    public bool HasTransientInput=>_capture is not null||Model?.HasGesture==true;
    public int RenderedHandles {get;private set;}
    public string? VectorPreviewDrawing=>(_tool as VectorClipTool)?.PreviewDrawing;
    public string? RenderedTool {get;private set;}
    public VisualOverlayControl(){Focusable=true;ClipToBounds=true;LostFocus+=(_,_)=>Cancel();}
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if(change.Property==ModelProperty)
        {
            ReleaseCapture();_tool?.Cancel();if(change.OldValue is MainWindowViewModel old){old.CancelGesture();old.PropertyChanged-=Changed;old.GestureEnded-=GestureEnded;if(old.SelectedEvents is INotifyCollectionChanged collection)collection.CollectionChanged-=SelectionChanged;}
            if(Model is {} model){model.PropertyChanged+=Changed;model.GestureEnded+=GestureEnded;if(model.SelectedEvents is INotifyCollectionChanged collection)collection.CollectionChanged+=SelectionChanged;}
            ContextMenu=Model is { } menuModel ? new ContextMenu { ItemsSource = new object[] {
                new MenuItem { Header="Save full frame…", Command=menuModel.Actions["video/frame/save"] },
                new MenuItem { Header="Copy full frame", Command=menuModel.Actions["video/frame/copy"] }, new Separator(),
                new MenuItem { Header="Save video frame…", Command=menuModel.Actions["video/frame/save/raw"] },
                new MenuItem { Header="Copy video frame", Command=menuModel.Actions["video/frame/copy/raw"] }, new Separator(),
                new MenuItem { Header="Save subtitles only…", Command=menuModel.Actions["video/frame/save/subs"] },
                new MenuItem { Header="Copy subtitles only", Command=menuModel.Actions["video/frame/copy/subs"] }, new Separator(),
                new MenuItem { Header="Copy coordinates", Command=menuModel.Actions["video/copy_coordinates"] },
                new MenuItem { Header="Reset Video Pan", Command=menuModel.Actions["video/reset_pan"] }, new Separator(),
                new MenuItem { Header="Detach / attach Video", Command=menuModel.Actions["video/detach"] }
            }} : null;
            Refresh();
        }
        if(change.Property==BoundsProperty){if(HasTransientInput)Cancel();Refresh();}
        if(change.Property==IsVisibleProperty&&!IsVisible)Cancel();
    }
    private void SelectionChanged(object? sender,NotifyCollectionChangedEventArgs e){Cancel();Refresh();}
    private void GestureEnded(object? sender,EventArgs e){ReleaseCapture();_tool?.Cancel();Refresh();}
    private void Changed(object? sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName is nameof(MainWindowViewModel.ActiveVisualTool) or nameof(MainWindowViewModel.VectorMode) or nameof(MainWindowViewModel.SelectedEvent) or nameof(MainWindowViewModel.Events) or nameof(MainWindowViewModel.VideoAspectRatio) or nameof(MainWindowViewModel.VideoSourcePath))
        {Cancel();}
        if(e.PropertyName is nameof(MainWindowViewModel.VideoPresentationRevision) or nameof(MainWindowViewModel.VideoAspectRatio) or nameof(MainWindowViewModel.PreviewRevision) or nameof(MainWindowViewModel.VideoFrame) or nameof(MainWindowViewModel.CurrentTimeSeconds) or nameof(MainWindowViewModel.ActiveVisualTool) or nameof(MainWindowViewModel.VectorMode) or nameof(MainWindowViewModel.SelectedEvent) or nameof(MainWindowViewModel.Events) or nameof(MainWindowViewModel.EditorDraft) or nameof(MainWindowViewModel.VisualBounds) or nameof(MainWindowViewModel.VisualBoundsPending))Refresh();
    }
    private void Refresh()
    {
        if(Model is not {} model||model.VideoFrame is not {} frame){_context=null;InvalidateVisual();return;}
        if (_pointerDocument != model.ActiveDocumentId) { _pointerDocument=model.ActiveDocumentId; _lastPointerPosition=null; }
        var area=VideoPresentationControl.Rect(model.VideoViewport(Bounds.Width,Bounds.Height,TopLevel.GetTopLevel(this)?.RenderScaling??1));
        if(!IsPanning&&HasTransientInput&&_context is {} previous&&previous.Video!=area){Cancel();}
        _context=new(model,area,model.VisibleVisualLines());
        if (_lastPointerPosition is { } last && _context.Video.Width>0 && _context.Video.Height>0)
        { _pointer=_pointer with { Script=_context.Script(last) }; _inside=_context.Video.Contains(last); model.ReportVideoPointer(_pointer.Script); }
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
        _lastPointerPosition=_pointer.Screen;
        Model?.ReportVideoPointer(_pointer.Script);
        if(e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed&&Model?.VideoFrame is not null)
        {
            Cancel();_panPointer=e.GetPosition(this);_panCheckpoint=(Model.VideoView!.PanX,Model.VideoView.PanY);_panProviderRevision=Model.VideoProviderRevision;
            _capture=e.Pointer;e.Pointer.Capture(this);e.Handled=true;return;
        }
        if(_context is not {} state||_tool is null||!state.Video.Contains(_pointer.Screen)||!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)return;
        try{if(_tool.Press(state,_pointer)){_capture=e.Pointer;e.Pointer.Capture(this);e.Handled=true;}InvalidateVisual();}catch(Exception ex){Fail(ex);}
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if(_panPointer is { } last)
        {
            var position=e.GetPosition(this);
            if(!e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed){FinishPan(false);return;}
            _panPointer=position;Model?.PanVideo(position.X-last.X,position.Y-last.Y);e.Handled=true;
        }
        _pointer=Pointer(e);_inside=_context?.Video.Contains(_pointer.Screen)==true;
        _lastPointerPosition=_pointer.Screen;
        if (_context is not null) Model?.ReportVideoPointer(_pointer.Script);
        try{if(!IsPanning&&_capture is not null&&_context is {} state)_tool?.Move(state,_pointer);var cursorType=_tool?.Cursor(_context,_pointer)??StandardCursorType.Cross;if(cursorType!=_cursorType){_cursorType=cursorType;Cursor=new Cursor(cursorType);}InvalidateVisual();}catch(Exception ex){Fail(ex);}
    }
    protected override void OnPointerExited(PointerEventArgs e){base.OnPointerExited(e);if(_capture is null)_inside=false;InvalidateVisual();}
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);if(IsPanning){if(e.InitialPressMouseButton==MouseButton.Middle)FinishPan(true);e.Handled=true;return;}if(_capture is null)return;
        try{if(_context is {} state)_tool?.Release(state,Pointer(e));ReleaseCapture();if(Model?.HasGesture==true)Model.EndGesture();e.Handled=true;InvalidateVisual();}catch(Exception ex){Fail(ex);}
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e){base.OnPointerCaptureLost(e);if(_capture is not null)Cancel();}
    protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.Key==Key.Escape&&HasTransientInput){Cancel();e.Handled=true;}else Modifiers(e.KeyModifiers);}
    protected override void OnKeyUp(KeyEventArgs e){base.OnKeyUp(e);Modifiers(e.KeyModifiers);}
    private void Modifiers(KeyModifiers modifiers){_pointer=_pointer with{Modifiers=modifiers};try{if(!IsPanning&&_capture is not null&&_context is {} state)_tool?.Move(state,_pointer);InvalidateVisual();}catch(Exception ex){Fail(ex);}}
    private void ReleaseCapture(){var pointer=_capture;_capture=null;pointer?.Capture(null);}
    private void FinishPan(bool commit)
    {
        var checkpoint=_panCheckpoint;_panPointer=null;_panCheckpoint=null;ReleaseCapture();
        if(!commit&&checkpoint is { } pan&&_pointerDocument==Model?.ActiveDocumentId&&_panProviderRevision==Model?.VideoProviderRevision)Model?.RestoreVideoPan(pan.X,pan.Y);
        Refresh();
    }
    public void CancelInput(){Cancel();}
    private void Cancel(){if(IsPanning)FinishPan(false);ReleaseCapture();_tool?.Cancel();Model?.CancelGesture();InvalidateVisual();}
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);if(Model?.VideoFrame is null)return;
        Cancel();var p=e.GetPosition(this);
        if(e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            var factor=1+.125*e.Delta.Y;var next=Model.VideoView!.ContentZoom*factor;
            if(next>=.125&&next<=10)Model.ZoomVideoContent(factor,p.X,p.Y);
        }
        else Model.Actions["video/presentation/zoom"].Execute((Math.Clamp(Model.VideoWindowZoom+.125*e.Delta.Y,.125,10)*100).ToString(System.Globalization.CultureInfo.InvariantCulture));
        e.Handled=true;
    }
    private void Fail(Exception exception){Model?.Registry.ReportFailure("video/visual-edit",exception);Cancel();}
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e){Cancel();base.OnDetachedFromVisualTree(e);}
}
