namespace Yoake.Core.Audio;

public static class AudioViewportMath
{
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
