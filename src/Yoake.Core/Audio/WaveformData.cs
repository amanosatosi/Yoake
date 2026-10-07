namespace Yoake.Core.Audio;

public readonly record struct WaveformEnvelope(float Minimum,float Maximum)
{
    public static WaveformEnvelope Merge(WaveformEnvelope a,WaveformEnvelope b)=>new(Math.Min(a.Minimum,b.Minimum),Math.Max(a.Maximum,b.Maximum));
    public static WaveformEnvelope FromSamples(ReadOnlySpan<float> samples)
    {
        if(samples.IsEmpty)return default;var min=1f;var max=-1f;
        foreach(var sample in samples){var s=float.IsFinite(sample)?Math.Clamp(sample,-1,1):0;min=Math.Min(min,s);max=Math.Max(max,s);}return new(min,max);
    }
}
public sealed class WaveformData
{
    private readonly List<WaveformEnvelope[]> _levels=[];
    public double StartSeconds{get;}
    public double StepSeconds{get;}
    public int Count=>_levels[0].Length;
    public IReadOnlyList<WaveformEnvelope> Envelopes=>_levels[0];
    public WaveformData(WaveformEnvelope[] samples,double stepSeconds,double startSeconds=0,CancellationToken token=default)
    {
        StartSeconds=startSeconds;StepSeconds=stepSeconds;_levels.Add(samples);
        while(samples.Length>1)
        {
            token.ThrowIfCancellationRequested();var next=new WaveformEnvelope[(samples.Length+1)/2];
            for(var i=0;i<next.Length;i++)next[i]=2*i+1<samples.Length?WaveformEnvelope.Merge(samples[2*i],samples[2*i+1]):samples[2*i];
            _levels.Add(next);samples=next;
        }
    }
    public WaveformEnvelope Range(double from,double to)
    {
        var level=Math.Clamp((int)Math.Floor(Math.Log2(Math.Max(1,(to-from)/StepSeconds))),0,_levels.Count-1);
        var samples=_levels[level];if(samples.Length==0||to<StartSeconds||from>=StartSeconds+Count*StepSeconds)return default;
        var step=StepSeconds*Math.Pow(2,level);var first=(int)Math.Clamp(Math.Floor((from-StartSeconds)/step),0,samples.Length-1);var last=(int)Math.Clamp(Math.Ceiling((to-StartSeconds)/step)-1,first,samples.Length-1);
        var value=samples[first];for(var i=first+1;i<=last;i++)value=WaveformEnvelope.Merge(value,samples[i]);return value;
    }
}
