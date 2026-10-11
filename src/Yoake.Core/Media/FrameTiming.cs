namespace Yoake.Core.Media;

public enum FrameTimeKind { Exact, Start, End }

// Immutable boundary calculations over the central provider's timestamps.
// This owns no playback position, provider, clock or external timecode state.
public sealed class FrameTiming
{
    private const long Denominator=1_000_000_000;
    private readonly long[] _times;
    private readonly long _origin;
    private readonly Int128 _numerator,_last;
    public int Count=>_times.Length;
    public FrameTiming(IReadOnlyList<double> seconds,double framesPerSecond,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(seconds);
        if(seconds.Count==0)throw new ArgumentException("Frame timestamps must not be empty.",nameof(seconds));
        _times=new long[seconds.Count];
        for(var index=0;index<_times.Length;index++)
        {
            cancellationToken.ThrowIfCancellationRequested();var value=seconds[index]*1000;
            if(!double.IsFinite(value)||value<=long.MinValue||value>=long.MaxValue)throw new ArgumentException("Invalid frame timestamp.",nameof(seconds));
            _times[index]=checked((long)Math.Round(value,MidpointRounding.AwayFromZero));
            if(index>0&&_times[index]<_times[index-1])throw new ArgumentException("Frame timestamps must be ordered.",nameof(seconds));
        }
        _origin=_times[0];for(var index=0;index<_times.Length;index++)_times[index]=checked(_times[index]-_origin);
        if(_times.Length>1)
        {
            if(_times[^1]==0)throw new ArgumentException("Frame timestamps must span a positive duration.",nameof(seconds));
            _last=(Int128)(_times.Length-1)*Denominator*1000;_numerator=_last/_times[^1];
        }
        else
        {
            if(!double.IsFinite(framesPerSecond)||framesPerSecond<=0||framesPerSecond>=long.MaxValue/(double)Denominator)
                throw new ArgumentException("Frame rate must be finite and positive.",nameof(framesPerSecond));
            _numerator=checked((long)(framesPerSecond*Denominator));
        }
        if(_numerator<=0)throw new ArgumentException("Frame rate is too small.",nameof(framesPerSecond));
    }
    // External files carry their own extrapolation rate/anchor. V1 assumed
    // rates must not be replaced by the stored timestamps' average rate.
    internal static FrameTiming FromExternal(IReadOnlyList<long> milliseconds,Int128 numerator,Int128 last,CancellationToken token)
    {
        if(milliseconds.Count==0||numerator<=0||last<0)throw new ArgumentException("Invalid external frame map.");
        var times=new long[milliseconds.Count];var origin=milliseconds[0];
        for(var index=0;index<times.Length;index++)
        {
            token.ThrowIfCancellationRequested();times[index]=checked(milliseconds[index]-origin);
            if(index>0&&times[index]<times[index-1])throw new ArgumentException("Frame timestamps must be ordered.");
        }
        return new(times,numerator,last);
    }
    private FrameTiming(long[] times,Int128 numerator,Int128 last){_times=times;_numerator=numerator;_last=last;_origin=0;}
    public long MillisecondsAtFrame(int frame,FrameTimeKind kind=FrameTimeKind.Exact)
    {
        var current=Exact(frame);
        return checked(_origin+(kind switch
        {
            FrameTimeKind.Exact=>current,
            FrameTimeKind.Start=>Midpoint(Exact((long)frame-1),current),
            FrameTimeKind.End=>Midpoint(current,Exact((long)frame+1)),
            _=>throw new ArgumentException("Unknown frame boundary.",nameof(kind)),
        }));
    }
    public int FrameAtMilliseconds(long milliseconds,FrameTimeKind kind=FrameTimeKind.Exact)
    {
        if(!Enum.IsDefined(kind))throw new ArgumentException("Unknown frame boundary.",nameof(kind));
        var time=checked(milliseconds-_origin-(kind==FrameTimeKind.Exact?0:1));
        var frame=ExactFrame(time);return checked(frame+(kind==FrameTimeKind.Start?1:0));
    }
    // Match the release's ASS time rounding without changing Yoake's existing
    // general formatter or the frozen 3.2 Automation conversion profile.
    public long SubtitleBoundary(int frame,FrameTimeKind kind)
    {
        var value=Math.Max(0,MillisecondsAtFrame(frame,kind));return checked((value+5)/10*10);
    }
    private static long Midpoint(long previous,long current)=>checked(previous+(current-previous+1)/2);
    private long Exact(long frame)=>frame<0?checked((long)(frame*Denominator*(Int128)1000/_numerator))
        :frame>=Count?checked((long)(((frame-Count+1)*Denominator*(Int128)1000+_last+_numerator/2)/_numerator)):_times[(int)frame];
    private int ExactFrame(long time)
    {
        if(time<0)return checked((int)((time*_numerator/Denominator-999)/1000));
        if(time>_times[^1])return checked((int)(((time+(Int128)1)*_numerator-_last-_numerator/2+(1000*(Int128)Denominator-1))/(1000*(Int128)Denominator))+Count-2);
        var low=0;var high=Count;
        while(low<high){var middle=low+(high-low)/2;if(_times[middle]<=time)low=middle+1;else high=middle;}
        return low-1;
    }
}
