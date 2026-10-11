namespace Yoake.Core.Audio;

public static class AudioViewportMath
{
    public static double ScrollPixels(double start,double span,double width,double pixels,double duration)
    {
        Validate(start,span,duration);
        if(!double.IsFinite(width)||width<=0||!double.IsFinite(pixels))throw new ArgumentException("Audio viewport width and pixel offset must be finite.");
        return Clamp(start+pixels*span/width,span,duration);
    }
    public static double Center(double seconds,double span,double duration)
    {Validate(seconds,span,duration);return Clamp(seconds-span/2,span,duration);}
    // Exact release display semantics: retain a visible range with 5% gutters;
    // center a fitting range; retain a middle viewport of a long range; otherwise
    // bring its nearer end/start into view without changing horizontal zoom.
    public static double RangeInView(double start,double span,double begin,double end,double duration)
    {
        Validate(start,span,duration);
        if(!double.IsFinite(begin)||!double.IsFinite(end)||begin<0||end<begin)throw new ArgumentException("Audio selection must be a finite ordered range.");
        var left=start+span/20;var inner=span*.9;var target=start;
        if(begin>=left&&end<=left+inner)return Clamp(start,span,duration);
        if(end-begin<inner)target=(begin+end)/2-span/2;
        else if(begin<left&&end>left+inner)return Clamp(start,span,duration);
        else if(end>=left&&end<left+inner)target=end-inner-span/20;
        else target=begin-span/20;
        return Clamp(target,span,duration);
    }
    private static double Clamp(double start,double span,double duration)=>Math.Clamp(start,0,Math.Max(0,duration-span));
    private static void Validate(double value,double span,double duration)
    {
        if(!double.IsFinite(value)||!double.IsFinite(span)||span<=0||!double.IsFinite(duration)||duration<0)throw new ArgumentException("Audio viewport must be finite with positive span and non-negative duration.");
    }
    public static double Span(double zoom)=>3600*Math.Pow(0.02/3600,Math.Clamp(zoom,0,100)/100);
    public static double Zoom(double span)=>Math.Clamp(100*Math.Log(Math.Clamp(span,0.02,3600)/3600)/Math.Log(0.02/3600),0,100);
    public static double AnchoredStart(double start,double oldSpan,double newSpan,double anchor,double duration)
    {
        var fraction=Math.Clamp((anchor-start)/Math.Max(0.02,oldSpan),0,1);
        return Math.Clamp(anchor-fraction*newSpan,0,Math.Max(0,duration-newSpan));
    }
    public static double Amplitude(double position)=>Math.Pow(Math.Clamp(position,10,100)/50,3);
    public static double AmplitudePosition(double intensity)=>Math.Clamp(50*Math.Cbrt(intensity),10,100);
    // Playback provider supports attenuation, not amplification. Link the
    // normalized display-control position to that provider's 0..1 range.
    public static double LinkedVolume(double intensity)=>AmplitudePosition(intensity)/100;
}
