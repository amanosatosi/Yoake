using Yoake.Core.Subtitles;

namespace Yoake.Native;

// A separate provider-owned renderer is reused for shaping measurements.
// No native state escapes; callers schedule this work off the UI thread.
public sealed class SubtitleGeometryProvider : IDisposable
{
    private readonly object _gate=new();
    private MangetsuSubtitleRenderer? _renderer;
    private bool _disposed;
    private readonly Dictionary<(string Track,int Width,int Height),AssBox?> _cache=[];
    public AssBox? Measure(string track,int width,int height,CancellationToken token)
    {
        lock(_gate)
        {
            token.ThrowIfCancellationRequested();ObjectDisposedException.ThrowIf(_disposed,this);
            var key=(track,width,height);if(_cache.TryGetValue(key,out var cached))return cached;
            if(_renderer is null)_renderer=new(track,width,height);else _renderer.UpdateTrack(track);
            var pixels=new byte[checked(width*height*4)];var frame=new DecodedVideoFrame(width,height,width*4,pixels);
            _renderer.Composite(frame,1);token.ThrowIfCancellationRequested();
            var left=width;var top=height;var right=-1;var bottom=-1;
            for(var y=0;y<height;y++)
            {
                if((y&63)==0)token.ThrowIfCancellationRequested();
                for(var x=0;x<width;x++){var i=(y*width+x)*4;if(pixels[i]==0&&pixels[i+1]==0&&pixels[i+2]==0)continue;left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);}
            }
            AssBox? result=right<left?null:new(left,top,right-left+1,bottom-top+1);
            if(_cache.Count>=16)_cache.Remove(_cache.Keys.First());_cache[key]=result;return result;
        }
    }
    public void Dispose(){lock(_gate){_disposed=true;_renderer?.Dispose();_renderer=null;_cache.Clear();}}
}
