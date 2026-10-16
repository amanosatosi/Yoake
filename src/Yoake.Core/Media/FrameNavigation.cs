namespace Yoake.Core.Media;

public static class FrameNavigation
{
    public static int AtTime(IReadOnlyList<double> times,double seconds)
    {
        if(times.Count==0)return 0;var lo=0;var hi=times.Count;
        while(lo<hi){var mid=lo+(hi-lo)/2;if(times[mid]<=seconds+0.0000001)lo=mid+1;else hi=mid;}
        return Math.Clamp(lo-1,0,times.Count-1);
    }
    public static int NearestKeyframe(IReadOnlyList<int> keys,int frame)
    {
        if(keys.Count==0)return frame;var lo=0;var hi=keys.Count;
        while(lo<hi){var mid=lo+(hi-lo)/2;if(keys[mid]<frame)lo=mid+1;else hi=mid;}
        if(lo==0)return keys[0];if(lo==keys.Count)return keys[^1];
        return frame-keys[lo-1]<=keys[lo]-frame?keys[lo-1]:keys[lo];
    }
    public static int StepKeyframe(IReadOnlyList<int> keys,int frame,int delta,int frameCount)
    {
        if(delta>0){foreach(var key in keys)if(key>frame)return key;return Math.Max(0,frameCount-1);}
        for(var i=keys.Count-1;i>=0;i--)if(keys[i]<frame)return keys[i];return keys.Count>0?keys[0]:0;
    }
    // Exact release command order; null means move to the adjacent event.
    public static int? Boundary(FrameTiming timing,int current,long start,long end,bool next)
    {
        if(start<0||end<start)throw new ArgumentException("Invalid subtitle timing range.");
        var first=timing.FrameAtMilliseconds(start,FrameTimeKind.Start);
        var last=timing.FrameAtMilliseconds(end,FrameTimeKind.End);
        if(next){if(first>current)return first;if(last>current)return last;}
        else{if(last<current)return last;if(first<current)return first;}
        return null;
    }
    public static int LargeStep(int current,int delta,int step,int frameCount)
    {
        if(step<=0||frameCount<=0)throw new ArgumentException("A positive step and loaded frame map are required.");
        return (int)Math.Clamp((long)current+(delta>0?step:-(long)step),0,frameCount-1);
    }
    public static (double Start,double End)? LineRange(FrameTiming timing,IReadOnlyList<double> times,long start,long end,double duration)
    {
        if(times.Count==0||start<0||end<=start||!double.IsFinite(duration)||duration<=0)return null;
        var first=timing.FrameAtMilliseconds(start,FrameTimeKind.Start);
        var last=timing.FrameAtMilliseconds(end,FrameTimeKind.End);
        if(first>=times.Count||last<0||last<first)return null;
        var left=times[Math.Clamp(first,0,times.Count-1)];
        var right=last<times.Count-1?times[Math.Max(0,last+1)]:duration;
        return right>left?(left,Math.Min(right,duration)):null;
    }
    public static string Relative(double seconds,long start,long end)
    {
        var milliseconds=(long)Math.Round(seconds*1000);
        return $"{milliseconds-start:+0;-0;0}ms; {milliseconds-end:+0;-0;0}ms";
    }
}
