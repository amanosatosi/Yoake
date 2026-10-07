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
    public static readonly StyledProperty<IReadOnlyList<float>?> SamplesProperty=AvaloniaProperty.Register<AudioWaveformControl,IReadOnlyList<float>?>(nameof(Samples));
    public static readonly StyledProperty<IBrush?> StrokeProperty=AvaloniaProperty.Register<AudioWaveformControl,IBrush?>(nameof(Stroke));
    public static readonly StyledProperty<MainWindowViewModel?> ModelProperty=AvaloniaProperty.Register<AudioWaveformControl,MainWindowViewModel?>(nameof(Model));
    public IReadOnlyList<float>? Samples { get=>GetValue(SamplesProperty);set=>SetValue(SamplesProperty,value); }
    public IBrush? Stroke { get=>GetValue(StrokeProperty);set=>SetValue(StrokeProperty,value); }
    public MainWindowViewModel? Model { get=>GetValue(ModelProperty);set=>SetValue(ModelProperty,value); }
    private double _start, _span=20, _pointerTime;
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
        if(change.Property==SamplesProperty)RequestSpectrum();
        InvalidateVisual();
    }
    private void ModelChanged(object? sender,PropertyChangedEventArgs e)
    {
        if(Model is not {} model)return;
        if(_lastLine!=model.SelectedEvent)
        {
            _lastLine=model.SelectedEvent;
            if(_lastLine?.StartMilliseconds is {} ms){_start=Math.Max(0,ms/1000d-_span/5);RequestSpectrum();}
        }
        if(e.PropertyName==nameof(MainWindowViewModel.CurrentTimeSeconds) && model.IsPlaying && (model.CurrentTimeSeconds<_start||model.CurrentTimeSeconds>_start+_span)){_start=Math.Max(0,model.CurrentTimeSeconds-_span/5);RequestSpectrum();}
        InvalidateVisual();
    }
    private void ZoomChanged(object? sender,EventArgs e){if(Model is {} model){_span=model.AudioWindowSeconds;_start=Math.Max(0,model.CurrentTimeSeconds-_span/5);RequestSpectrum();InvalidateVisual();}}
    private double X(double seconds)=>(seconds-_start)/_span*Bounds.Width;
    private double Time(double x)=>Math.Max(0,_start+x/Math.Max(1,Bounds.Width)*_span);
    public override void Render(DrawingContext context)
    {
        base.Render(context);var model=Model;if(model is null||Bounds.Width<=1||Bounds.Height<=1)return;
        var area=new Rect(0,0,Bounds.Width,Bounds.Height);context.FillRectangle(new SolidColorBrush(Color.FromRgb(20,23,29)),area);
        if(_spectrogram&&_spectrum is not null)context.DrawImage(_spectrum,new Rect(_spectrum.Size),area);
        else if(Samples is {Count:>0} samples && model.MediaDurationSeconds>0)
        {
            var pen=new Pen(Stroke??Brushes.DodgerBlue,1);var mid=Bounds.Height/2;
            for(var x=0;x<(int)Bounds.Width;x++)
            {
                var first=(int)Math.Clamp(Time(x)/model.MediaDurationSeconds*samples.Count,0,samples.Count-1);
                var last=(int)Math.Clamp(Time(x+1)/model.MediaDurationSeconds*samples.Count,first,samples.Count-1);
                var peak=0f;for(var i=first;i<=last;i++)peak=Math.Max(peak,samples[i]);
                context.DrawLine(pen,new(x+0.5,mid-peak*(mid-3)),new(x+0.5,mid+peak*(mid-3)));
            }
        }
        var neighbor=new Pen(new SolidColorBrush(Color.FromArgb(90,180,180,180)),1);
        foreach(var line in model.Events)
        {
            if(line==model.SelectedEvent)continue;
            foreach(var boundary in new[]{line.StartMilliseconds,line.EndMilliseconds})if(boundary is {} ms){var x=X(ms/1000d);if(x>=0&&x<=Bounds.Width)context.DrawLine(neighbor,new(x,0),new(x,Bounds.Height));}
        }
        if(model.SelectedEvent is {} selected)
        {
            var left=X((selected.StartMilliseconds??0)/1000d);var right=X((selected.EndMilliseconds??0)/1000d);
            if(right>left)context.FillRectangle(new SolidColorBrush(Color.FromArgb(45,80,180,255)),new Rect(left,0,right-left,Bounds.Height));
            context.DrawLine(new Pen(Brushes.LimeGreen,2),new(left,0),new(left,Bounds.Height));context.DrawLine(new Pen(Brushes.OrangeRed,2),new(right,0),new(right,Bounds.Height));
        }
        var cursor=X(model.CurrentTimeSeconds);context.DrawLine(new Pen(Brushes.White,1),new(cursor,0),new(cursor,Bounds.Height));
        // Second grid, useful even with no audio loaded.
        var step=Math.Max(0.1,Math.Pow(10,Math.Floor(Math.Log10(_span/8))));
        for(var t=Math.Ceiling(_start/step)*step;t<_start+_span;t+=step){var x=X(t);context.DrawLine(neighbor,new(x,Bounds.Height-5),new(x,Bounds.Height));}
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
        if(e.KeyModifiers.HasFlag(KeyModifiers.Control)) { _span=Math.Clamp(_span*Math.Pow(1.25,-e.Delta.Y),0.5,3600); }
        else _start=Math.Clamp(_start-(e.Delta.X+e.Delta.Y)*_span/10,0,Math.Max(0,(Model?.MediaDurationSeconds??0)-_span));
        RequestSpectrum();InvalidateVisual();e.Handled=true;
    }
    private async void RequestSpectrum()
    {
        _analysis?.Cancel();_analysis?.Dispose();_analysis=null;
        if(!_spectrogram||Model is not {} model)return;
        var cancellation=new CancellationTokenSource();_analysis=cancellation;
        try { var bitmap=await model.CreateSpectrumAsync(_start,_span,cancellation.Token);if(cancellation.IsCancellationRequested){bitmap?.Dispose();return;}var old=_spectrum;_spectrum=bitmap;old?.Dispose();InvalidateVisual(); }
        catch(OperationCanceledException) { }
        catch(Exception e){model.Registry.ReportFailure("audio/spectrogram",e);}
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e){Cancel();_analysis?.Cancel();_spectrum?.Dispose();_spectrum=null;base.OnDetachedFromVisualTree(e);}
}
