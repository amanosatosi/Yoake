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
    private readonly Dictionary<(FfmsMediaSession Media,double Start,double Duration,double Intensity,int Quality,int Frequency),AudioSpectrumTile> _spectrumCache=[];
    public async Task<WriteableBitmap?> CreateSpectrumAsync(double start,double duration,CancellationToken token,bool primary=false)
    {
        var media=_media?.AudioProvider;if(media is null)return null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _documents[_activeId!.Value].AudioAnalysis.Token);
        token = linked.Token;
        var intensity=AudioIntensity;var quality=(int)SpectrumQuality;var frequency=(int)SpectrumFrequency;var key=(media,start,duration,intensity,quality,frequency);
        if(!_spectrumCache.TryGetValue(key,out var tile))
        {
            AudioSpectrumTile? result=null;
            var job=_jobs.Run("Spectrogram viewport",ct=>{result=media.BuildSpectrum(start,duration,128<<quality,ct,intensity,quality,frequency);return Task.CompletedTask;},token);
            await job.Completion;token.ThrowIfCancellationRequested();if(!ReferenceEquals(media,_media?.AudioProvider))return null;
            tile=result!;_spectrumCache[key]=tile;
        }
        var budget=SpectrumCacheMiB*1024L*1024L;
        long Bytes(AudioSpectrumTile t)=>(long)t.Bgra.Length+(t.PrimaryBgra?.Length??0);
        var retained=_spectrumCache.Values.Sum(Bytes);
        foreach(var old in _spectrumCache.Keys.Where(k=>k!=key).ToArray())
        {
            if(retained<=budget)break;
            retained-=Bytes(_spectrumCache[old]);_spectrumCache.Remove(old);
        }
        if(retained>budget)_spectrumCache.Remove(key);
        var bitmap=new WriteableBitmap(new PixelSize(tile.Width,tile.Height),new Vector(96,96),PixelFormat.Bgra8888,AlphaFormat.Opaque);
        using var buffer=bitmap.Lock();for(var y=0;y<tile.Height;y++)Marshal.Copy(primary?tile.PrimaryBgra??tile.Bgra:tile.Bgra,y*tile.Width*4,buffer.Address+y*buffer.RowBytes,tile.Width*4);return bitmap;
    }
}
