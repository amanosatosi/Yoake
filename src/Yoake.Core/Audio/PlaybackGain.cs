namespace Yoake.Core.Audio;
public static class PlaybackGain
{
    // Linear attenuation only: 0..1, no gain above unity. NaN/Inf samples
    // become silence; saturation occurs before signed PCM16 conversion.
    public static short Pcm16(float sample,double volume,bool muted)
    {
        if(muted||!float.IsFinite(sample))return 0;
        var gain=double.IsFinite(volume)?Math.Clamp(volume,0,1):0;
        return (short)Math.Round(Math.Clamp(sample*gain,-1,1)*short.MaxValue);
    }
}
