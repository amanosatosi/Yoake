using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Yoake.Native;
namespace Yoake.UI.ViewModels;
public sealed partial class MainWindowViewModel
{
    public async Task<T> RunAnalysisAsync<T>(string name,Func<CancellationToken,T> work,CancellationToken token)
    {
        T result=default!;var job=_jobs.Run(name,ct=>{result=work(ct);return Task.CompletedTask;},token);await job.Completion;return result;
    }
    private readonly Dictionary<(FfmsMediaSession Media,double Start,double Duration),AudioSpectrumTile> _spectrumCache=[];
    public async Task<WriteableBitmap?> CreateSpectrumAsync(double start,double duration,CancellationToken token)
    {
        var media=_media;if(media?.HasAudio!=true)return null;
        var key=(media,Math.Round(start,2),Math.Round(duration,2));
        if(!_spectrumCache.TryGetValue(key,out var tile))
        {
            AudioSpectrumTile? result=null;
            var job=_jobs.Run("Spectrogram viewport",ct=>{result=media.BuildSpectrum(start,duration,256,ct);return Task.CompletedTask;},token);
            await job.Completion;token.ThrowIfCancellationRequested();if(!ReferenceEquals(media,_media))return null;
            tile=result!;if(_spectrumCache.Count>=4)_spectrumCache.Remove(_spectrumCache.Keys.First());_spectrumCache[key]=tile;
        }
        var bitmap=new WriteableBitmap(new PixelSize(tile.Width,tile.Height),new Vector(96,96),PixelFormat.Bgra8888,AlphaFormat.Opaque);
        using var buffer=bitmap.Lock();for(var y=0;y<tile.Height;y++)Marshal.Copy(tile.Bgra,y*tile.Width*4,buffer.Address+y*buffer.RowBytes,tile.Width*4);return bitmap;
    }
}
