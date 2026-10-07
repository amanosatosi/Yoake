using System.Numerics;

namespace Yoake.Native;

public sealed record AudioSpectrumTile(int Width, int Height, byte[] Bgra);

public sealed partial class FfmsMediaSession
{
    // Bounded viewport analysis, independent of waveform peak storage. No giant
    // whole-film spectrogram allocation; callers cache a few tiles per document.
    public AudioSpectrumTile BuildSpectrum(double start, double duration, int columns, CancellationToken token,double intensity=1)
    {
        const int size=512, bands=128;
        columns=Math.Clamp(columns,32,512);
        var pixels=new byte[columns*bands*4];
        var fft=new Complex[size];
        for(var x=0;x<columns;x++)
        {
            token.ThrowIfCancellationRequested();
            var seconds=start+(x+0.5)*duration/columns;
            var audio=ReadAudioFrames(Math.Max(0,TimeToAudioFrame(seconds)-size/2),size);
            Array.Clear(fft);
            for(var i=0;i<audio.FrameCount;i++)
            {
                double mono=0; for(var c=0;c<audio.Channels;c++)mono+=audio.Samples[i*audio.Channels+c];
                fft[i]=new Complex(mono/audio.Channels*(0.5-0.5*Math.Cos(2*Math.PI*i/(size-1))),0);
            }
            Transform(fft);
            for(var y=0;y<bands;y++)
            {
                // Linear 0..Nyquist, with decibel brightness. Black at silence.
                var bin=1+y*(size/2-1)/(bands-1);
                var db=20*Math.Log10(Math.Max(1e-7,fft[bin].Magnitude/(size/2)));
                var value=Math.Clamp((db+80+20*Math.Log10(Math.Clamp(intensity,0.1,8)))/80,0,1);
                var offset=((bands-1-y)*columns+x)*4;
                pixels[offset]=(byte)(Math.Min(1,value*2)*180);
                pixels[offset+1]=(byte)(value*value*255);
                pixels[offset+2]=(byte)(Math.Pow(value,3)*255);
                pixels[offset+3]=255;
            }
        }
        return new(columns,bands,pixels);
    }
    private static void Transform(Complex[] data)
    {
        for(int i=1,j=0;i<data.Length;i++)
        {
            var bit=data.Length>>1; for(; (j&bit)!=0;bit>>=1)j^=bit; j^=bit;
            if(i<j)(data[i],data[j])=(data[j],data[i]);
        }
        for(var length=2;length<=data.Length;length<<=1)
        {
            var root=Complex.FromPolarCoordinates(1,-2*Math.PI/length);
            for(var start=0;start<data.Length;start+=length)
            {
                var w=Complex.One;
                for(var j=0;j<length/2;j++) { var u=data[start+j];var v=data[start+j+length/2]*w;data[start+j]=u+v;data[start+j+length/2]=u-v;w*=root; }
            }
        }
    }
}
