namespace Yoake.Core.Audio;

public enum AudioPlaybackKind { Current, Line, Selection, Before, After, Begin, End, ToEnd, Cursor }

// An immutable interval in the existing audio provider's sample domain, not a clock.
public sealed record AudioPlaybackPlan
{
    private AudioPlaybackPlan(AudioPlaybackKind kind,int rate,long frames,long start,long end)
    { Kind=kind;SampleRate=rate;FrameCount=frames;StartFrame=start;EndFrame=end; }
    public AudioPlaybackKind Kind { get; }
    public int SampleRate { get; }
    public long FrameCount { get; }
    public long StartFrame { get; }
    public long EndFrame { get; }
    public double StartSeconds=>StartFrame/(double)SampleRate;
    public double EndSeconds=>EndFrame/(double)SampleRate;
    public bool IsEmpty=>StartFrame==EndFrame;
    public bool FollowsSelectionEnd=>Kind is AudioPlaybackKind.Selection or AudioPlaybackKind.End;

    public static AudioPlaybackPlan Create(AudioPlaybackKind kind,int sampleRate,long frameCount,
        AudioClipInterval selection,double cursorSeconds=0)
    {
        if(!Enum.IsDefined(kind)||sampleRate<=0||frameCount<0||selection.StartMilliseconds<0||
            selection.EndMilliseconds<selection.StartMilliseconds||!double.IsFinite(cursorSeconds))
            throw new ArgumentException("Invalid audio playback range.");
        var start=selection.StartMilliseconds;var end=selection.EndMilliseconds;
        switch(kind)
        {
            case AudioPlaybackKind.Before:end=start;start=Math.Max(0,start-500);break;
            case AudioPlaybackKind.After:start=end;end=end>long.MaxValue-500?long.MaxValue:end+500;break;
            case AudioPlaybackKind.Begin:end=start+Math.Min(500,end-start);break;
            case AudioPlaybackKind.End:start=end-Math.Min(500,end-start);break;
        }
        var left=CeilingFrame(start,sampleRate,frameCount);var right=CeilingFrame(end,sampleRate,frameCount);
        if(kind==AudioPlaybackKind.ToEnd)right=frameCount;
        if(kind==AudioPlaybackKind.Cursor)
        {left=FrameAtSeconds(cursorSeconds,sampleRate,frameCount);right=frameCount;}
        return new(kind,sampleRate,frameCount,left,Math.Max(left,right));
    }
    public AudioPlaybackPlan WithSelectionEnd(long milliseconds)
    {
        if(milliseconds<0)throw new ArgumentException("Invalid audio selection end.");
        return FollowsSelectionEnd?new(Kind,SampleRate,FrameCount,StartFrame,
            Math.Max(StartFrame,CeilingFrame(milliseconds,SampleRate,FrameCount))):this;
    }
    public static long CeilingFrame(long milliseconds,int rate,long count)
    {
        if(milliseconds<0||rate<=0||count<0)throw new ArgumentException("Invalid audio sample boundary.");
        return (long)Int128.Min(count,((Int128)milliseconds*rate+999)/1000);
    }
    public static long FrameAtSeconds(double seconds,int rate,long count)
    {
        if(!double.IsFinite(seconds)||rate<=0||count<0)throw new ArgumentException("Invalid audio sample boundary.");
        if(seconds<=0)return 0;if(seconds>=count/(double)rate)return count;
        var frame=Math.Ceiling(seconds*rate);return frame>=count?count:(long)frame;
    }
    public static int FramesToQueue(long nextFrame,int requested,long frameCount,long endFrame)
    {
        if(nextFrame<0||requested<=0||frameCount<0||endFrame<0)throw new ArgumentException("Invalid audio buffer range.");
        var end=Math.Min(frameCount,endFrame);
        return nextFrame>=end?0:(int)Math.Min(requested,end-nextFrame);
    }
}
