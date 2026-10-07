using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Yoake.Core.Jobs;
using Yoake.Native;

namespace Yoake.UI.Controls;

// Actual Mangetsu output over a checkerboard destination; rendering stays off UI.
public sealed class StylePreviewControl : UserControl, IDisposable
{
    private const int WidthPixels=640,HeightPixels=180;
    private readonly BackgroundJobService _jobs=new();
    private readonly SemaphoreSlim _gate=new(1,1);
    private readonly Image _image=new(){Stretch=Stretch.Uniform};
    private readonly TextBlock _status=new(){FontSize=10,TextWrapping=TextWrapping.Wrap,IsVisible=false};
    private readonly byte[] _pixels=new byte[WidthPixels*HeightPixels*4];
    private BackgroundJobHandle? _job;
    private MangetsuSubtitleRenderer? _renderer;
    private WriteableBitmap? _bitmap;
    private long _revision;
    private bool _disposed;
    public bool HasFrame {get;private set;}
    public long RequestedRevision=>Interlocked.Read(ref _revision);
    public long DisplayedRevision {get;private set;}
    public bool HasCurrentFrame=>HasFrame&&DisplayedRevision==RequestedRevision;
    public string? LastError {get;private set;}
    public StylePreviewControl()
    {
        var grid=new Grid{RowDefinitions=new("*,Auto"),MinHeight=150};grid.Children.Add(new CheckerboardControl());grid.Children.Add(_image);Grid.SetRow(_status,1);grid.Children.Add(_status);Content=grid;
    }
    public void Update(string assSource)
    {
        if(_disposed)return;_job?.Cancel();var revision=Interlocked.Increment(ref _revision);LastError=null;
        _job=_jobs.Run("Mangetsu style preview",async token=>
        {
            try
            {
                await Task.Delay(100,token);await _gate.WaitAsync(token);
                try
                {
                    token.ThrowIfCancellationRequested();if(_disposed)return;
                    if(_renderer is null)_renderer=new MangetsuSubtitleRenderer(assSource,WidthPixels,HeightPixels);else _renderer.UpdateTrack(assSource);
                    for(var y=0;y<HeightPixels;y++)for(var x=0;x<WidthPixels;x++)
                    {
                        var i=(y*WidthPixels+x)*4;var value=(byte)((x/10+y/10)%2==0?184:132);
                        _pixels[i]=_pixels[i+1]=_pixels[i+2]=value;_pixels[i+3]=255;
                    }
                    _renderer.Composite(new(WidthPixels,HeightPixels,WidthPixels*4,_pixels),0.5);
                    token.ThrowIfCancellationRequested();
                    await Dispatcher.UIThread.InvokeAsync(()=>
                    {
                        if(_disposed||revision!=_revision)return;
                        _bitmap??=new WriteableBitmap(new PixelSize(WidthPixels,HeightPixels),new Vector(96,96),PixelFormat.Bgra8888,AlphaFormat.Unpremul);
                        using(var framebuffer=_bitmap.Lock())for(var y=0;y<HeightPixels;y++)Marshal.Copy(_pixels,y*WidthPixels*4,framebuffer.Address+y*framebuffer.RowBytes,WidthPixels*4);
                        _image.Source=_bitmap;_image.InvalidateVisual();HasFrame=true;DisplayedRevision=revision;_status.IsVisible=false;
                    });
                }
                finally{_gate.Release();}
            }
            catch(OperationCanceledException){}
            catch(Exception exception)
            {
                await Dispatcher.UIThread.InvokeAsync(()=>{if(!_disposed&&revision==_revision){LastError=exception.Message;_status.Text="Preview: "+exception.Message;_status.IsVisible=true;}});
            }
        });
    }
    public void Dispose()
    {
        if(_disposed)return;_disposed=true;Interlocked.Increment(ref _revision);_jobs.CancelAll();_image.Source=null;_bitmap?.Dispose();
        _=Task.Run(async()=>{await _gate.WaitAsync();try{_renderer?.Dispose();_renderer=null;}finally{_gate.Release();}});
    }
}
