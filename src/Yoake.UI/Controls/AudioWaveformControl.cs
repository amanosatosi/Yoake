using Yoake.Core.Audio;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System.ComponentModel;
using Yoake.Core.Commands;
using Yoake.Core.Subtitles;
using Yoake.UI.ViewModels;

namespace Yoake.UI.Controls;

public sealed class AudioWaveformControl : Control
{
    public static readonly StyledProperty<WaveformData?> SamplesProperty=AvaloniaProperty.Register<AudioWaveformControl,WaveformData?>(nameof(Samples));
    public static readonly StyledProperty<IBrush?> StrokeProperty=AvaloniaProperty.Register<AudioWaveformControl,IBrush?>(nameof(Stroke));
    public static readonly StyledProperty<MainWindowViewModel?> ModelProperty=AvaloniaProperty.Register<AudioWaveformControl,MainWindowViewModel?>(nameof(Model));
    public WaveformData? Samples { get=>GetValue(SamplesProperty);set=>SetValue(SamplesProperty,value); }
    public IBrush? Stroke { get=>GetValue(StrokeProperty);set=>SetValue(StrokeProperty,value); }
    public MainWindowViewModel? Model { get=>GetValue(ModelProperty);set=>SetValue(ModelProperty,value); }
    public static readonly StyledProperty<double> ViewportStartProperty=AvaloniaProperty.Register<AudioWaveformControl,double>(nameof(ViewportStart),0,defaultBindingMode:Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<double> VisibleSecondsProperty=AvaloniaProperty.Register<AudioWaveformControl,double>(nameof(VisibleSeconds),20);
    public static readonly StyledProperty<double> MaximumPanProperty=AvaloniaProperty.Register<AudioWaveformControl,double>(nameof(MaximumPan));
    public double ViewportStart{get=>GetValue(ViewportStartProperty);set=>SetCurrentValue(ViewportStartProperty,value);}
    public double VisibleSeconds{get=>GetValue(VisibleSecondsProperty);set=>SetCurrentValue(VisibleSecondsProperty,value);}
    public double MaximumPan{get=>GetValue(MaximumPanProperty);private set=>SetValue(MaximumPanProperty,value);}
    private WaveformData? _detail;
    private readonly HashSet<int> _boundaryPixels=[];
    private double _pointerTime;
    private int _part;
    private bool _drag;
    private AssEvent? _lastLine;
    private WriteableBitmap? _spectrum;
    private CancellationTokenSource? _analysis;
    private bool _spectrogram;
    public bool Spectrogram { get=>_spectrogram;set { _spectrogram=value; RequestSpectrum();InvalidateVisual(); } }
    public AudioWaveformControl() { Focusable=true;ClipToBounds=true; }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if(change.Property==ModelProperty)
        {
            if(change.OldValue is MainWindowViewModel old){old.PropertyChanged-=ModelChanged;old.AudioZoomChanged-=ZoomChanged;}
            if(Model is {} model){model.PropertyChanged+=ModelChanged;model.AudioZoomChanged+=ZoomChanged;}
        }
        if(change.Property==SamplesProperty||change.Property==ViewportStartProperty||change.Property==VisibleSecondsProperty||change.Property==BoundsProperty)
        {MaximumPan=Math.Max(0,(Model?.MediaDurationSeconds??0)-VisibleSeconds);if(ViewportStart>MaximumPan)ViewportStart=MaximumPan;RequestSpectrum();}
        if(change.Property==ViewportStartProperty||change.Property==VisibleSecondsProperty)Model?.RememberAudioViewport(ViewportStart,VisibleSeconds);
        InvalidateVisual();
    }
    private void ModelChanged(object? sender,PropertyChangedEventArgs e)
    {
        if(Model is not {} model)return;
        if(e.PropertyName==nameof(MainWindowViewModel.AudioIntensity))RequestSpectrum();
        if(e.PropertyName==nameof(MainWindowViewModel.CanPlayMedia)){var viewport=model.AudioViewport;_spectrum?.Dispose();_spectrum=null;VisibleSeconds=viewport.Span;ViewportStart=viewport.Start;_lastLine=model.SelectedEvent;RequestSpectrum();}
        if(_lastLine!=model.SelectedEvent)
        {
            _lastLine=model.SelectedEvent;
            if(_lastLine?.StartMilliseconds is {} ms){ViewportStart=Math.Max(0,ms/1000d-VisibleSeconds/5);RequestSpectrum();}
        }
        if(e.PropertyName==nameof(MainWindowViewModel.CurrentTimeSeconds) && model.IsPlaying && (model.CurrentTimeSeconds<ViewportStart||model.CurrentTimeSeconds>ViewportStart+VisibleSeconds)){ViewportStart=Math.Max(0,model.CurrentTimeSeconds-VisibleSeconds/5);RequestSpectrum();}
        InvalidateVisual();
    }
    private double? _zoomAnchor;
    private void ZoomChanged(object? sender,EventArgs e)
    {
        if(Model is not {} model)return;
        var oldSpan=VisibleSeconds;var oldStart=ViewportStart;
        var anchor=_zoomAnchor??(model.CurrentTimeSeconds>=oldStart&&model.CurrentTimeSeconds<=oldStart+oldSpan?model.CurrentTimeSeconds:oldStart+oldSpan/2);
        var span=model.AudioWindowSeconds;var start=AudioViewportMath.AnchoredStart(oldStart,oldSpan,span,anchor,model.MediaDurationSeconds);
        VisibleSeconds=span;ViewportStart=start;RequestSpectrum();InvalidateVisual();
    }
    private double X(double seconds)=>(seconds-ViewportStart)/VisibleSeconds*Bounds.Width;
    private double Time(double x)=>Math.Max(0,ViewportStart+x/Math.Max(1,Bounds.Width)*VisibleSeconds);
    public override void Render(DrawingContext context)
    {
        base.Render(context);var model=Model;if(model is null||Bounds.Width<=1||Bounds.Height<=1)return;
        var area=new Rect(0,0,Bounds.Width,Bounds.Height);context.FillRectangle(new SolidColorBrush(Color.FromRgb(20,23,29)),area);
        if(_spectrogram&&_spectrum is not null)context.DrawImage(_spectrum,new Rect(_spectrum.Size),area);
        else if((_detail??Samples) is {Count:>0} samples && model.MediaDurationSeconds>0)
        {
            var pen=new Pen(Stroke??Brushes.DodgerBlue,1);var mid=Bounds.Height/2;
            for(var x=0;x<(int)Bounds.Width;x++)
            {
                var envelope=samples.Range(Time(x),Time(x+1));var gain=model.AudioIntensity;
                context.DrawLine(pen,new(x+0.5,mid-Math.Clamp(envelope.Maximum*gain,-1,1)*(mid-3)),new(x+0.5,mid-Math.Clamp(envelope.Minimum*gain,-1,1)*(mid-3)));
            }
        }
        var neighbor=new Pen(new SolidColorBrush(Color.FromArgb(90,180,180,180)),1);
        _boundaryPixels.Clear();
        foreach(var line in model.Events)
        {
            if(line==model.SelectedEvent)continue;
            void Boundary(long? boundary){if(boundary is {} ms){var x=X(ms/1000d);if(x>=0&&x<=Bounds.Width&&_boundaryPixels.Add((int)x))context.DrawLine(neighbor,new(x,0),new(x,Bounds.Height));}}
            Boundary(line.StartMilliseconds);Boundary(line.EndMilliseconds);
        }
        if(model.SelectedEvent is {} selected)
        {
            var left=X((selected.StartMilliseconds??0)/1000d);var right=X((selected.EndMilliseconds??0)/1000d);
            if(right>left)context.FillRectangle(new SolidColorBrush(Color.FromArgb(45,80,180,255)),new Rect(left,0,right-left,Bounds.Height));
            context.DrawLine(new Pen(Brushes.LimeGreen,2),new(left,0),new(left,Bounds.Height));context.DrawLine(new Pen(Brushes.OrangeRed,2),new(right,0),new(right,Bounds.Height));
        }
        var cursor=X(model.CurrentTimeSeconds);context.DrawLine(new Pen(Brushes.White,1),new(cursor,0),new(cursor,Bounds.Height));
        // Keep complete time labels readable as the audio pane is resized.
        // Prefer familiar 1/2/5 intervals and avoid drawing overlapping labels.
        var target=Math.Max(0.1,VisibleSeconds*64/Bounds.Width);
        var magnitude=Math.Pow(10,Math.Floor(Math.Log10(target)));
        var fraction=target/magnitude;
        var step=magnitude*(fraction<=1?1:fraction<=2?2:fraction<=5?5:10);
        for(var t=Math.Ceiling(ViewportStart/step)*step;t<ViewportStart+VisibleSeconds;t+=step)
        {
            var x=X(t);context.DrawLine(neighbor,new(x,Bounds.Height-5),new(x,Bounds.Height));
            var label=new FormattedText(AssTime.Format((long)(t*1000)),System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,Typeface.Default,9,Brushes.LightGray);
            if(x+2+label.Width<=Bounds.Width)context.DrawText(label,new Point(x+2,Bounds.Height-14));
        }
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);Focus();if(Model is not {} model)return;
        var point=e.GetCurrentPoint(this);if(!point.Properties.IsLeftButtonPressed)return;
        _pointerTime=Time(point.Position.X);
        var line=model.SelectedEvent;var start=(line?.StartMilliseconds??0)/1000d;var end=(line?.EndMilliseconds??0)/1000d;
        var nearStart=Math.Abs(X(start)-point.Position.X)<=7;var nearEnd=Math.Abs(X(end)-point.Position.X)<=7;
        if(line is not null && (nearStart||nearEnd||e.KeyModifiers.HasFlag(KeyModifiers.Shift)&&_pointerTime>=start&&_pointerTime<=end))
        {
            _part=nearStart?0:nearEnd?1:2;
            if(model.BeginGesture("Drag line timing")){model.StopPlayback();_drag=true;e.Pointer.Capture(this);}
        }
        else _=model.Registry.InvokeAsync(CommandIds.VideoSeek,new(),AssTime.Format((long)(_pointerTime*1000)));
        e.Handled=true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);if(!_drag)return;
        try{Model?.UpdateTimingGesture(_part,Time(e.GetPosition(this).X)-_pointerTime);InvalidateVisual();}
        catch(Exception exception){Model?.Registry.ReportFailure("timing/drag",exception);Cancel();}
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e){base.OnPointerReleased(e);if(_drag){_drag=false;Model?.EndGesture();e.Pointer.Capture(null);e.Handled=true;}}
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e){base.OnPointerCaptureLost(e);Cancel();}
    private void Cancel(){if(_drag){_drag=false;Model?.CancelGesture();InvalidateVisual();}}
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if(_drag){e.Handled=true;return;}
        if(e.KeyModifiers.HasFlag(KeyModifiers.Shift)&&Model is {} amplitudeModel)amplitudeModel.AudioAmplitude+=e.Delta.Y*3;
        else if(e.KeyModifiers.HasFlag(KeyModifiers.Control)&&Model is {} model)
        {
            _zoomAnchor=Time(e.GetPosition(this).X);try{model.AudioWindowSeconds=Math.Clamp(VisibleSeconds*Math.Pow(1.25,-e.Delta.Y),0.02,3600);}finally{_zoomAnchor=null;}
        }
        else ViewportStart=Math.Clamp(ViewportStart+(e.Delta.X-e.Delta.Y)*VisibleSeconds/10,0,Math.Max(0,(Model?.MediaDurationSeconds??0)-VisibleSeconds));
        RequestSpectrum();InvalidateVisual();e.Handled=true;
    }
    private async void RequestSpectrum()
    {
        _analysis?.Cancel();_analysis?.Dispose();_analysis=null;
        _detail=null;if(Model is not {} model)return;
        // Supply a visible-range tile while the long-file overview is still
        // building. Once available, use the overview unless finer detail is needed.
        if(!_spectrogram&&Samples is not null&&VisibleSeconds/Math.Max(1,Bounds.Width)>=Samples.StepSeconds)return;
        var cancellation=new CancellationTokenSource();_analysis=cancellation;
        try {
            await Task.Delay(25,cancellation.Token);
            if(!_spectrogram){var detail=await model.CreateWaveformViewportAsync(ViewportStart,VisibleSeconds,(int)Math.Clamp(Bounds.Width,1,4096),cancellation.Token);if(!cancellation.IsCancellationRequested){_detail=detail;InvalidateVisual();}return;}
            var bitmap=await model.CreateSpectrumAsync(ViewportStart,VisibleSeconds,cancellation.Token);if(cancellation.IsCancellationRequested){bitmap?.Dispose();return;}var old=_spectrum;_spectrum=bitmap;old?.Dispose();InvalidateVisual(); }
        catch(OperationCanceledException) { }
        catch(Exception e){if(!cancellation.IsCancellationRequested)model.Registry.ReportFailure("audio/spectrogram",e);}
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e){Cancel();_analysis?.Cancel();_spectrum?.Dispose();_spectrum=null;base.OnDetachedFromVisualTree(e);}
}
