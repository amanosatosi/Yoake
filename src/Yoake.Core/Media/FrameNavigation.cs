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
    public static int StepKeyframe(IReadOnlyList<int> keys,int frame,int delta)
    {
        if(delta>0){foreach(var key in keys)if(key>frame)return key;return keys.Count>0?keys[^1]:frame;}
        for(var i=keys.Count-1;i>=0;i--)if(keys[i]<frame)return keys[i];return keys.Count>0?keys[0]:frame;
    }
    public static string Relative(double seconds,long start,long end)
    {
        var milliseconds=(long)Math.Round(seconds*1000);
        return $"{milliseconds-start:+0;-0;0}ms; {milliseconds-end:+0;-0;0}ms";
    }
}
