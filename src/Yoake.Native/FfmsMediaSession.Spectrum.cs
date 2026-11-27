using System.Numerics;

namespace Yoake.Native;

public sealed record AudioSpectrumTile(int Width, int Height, byte[] Bgra, byte[]? PrimaryBgra=null);

public sealed partial class FfmsMediaSession
{
    // Bounded viewport analysis, independent of waveform peak storage. No giant
    // whole-film spectrogram allocation; callers cache a few tiles per document.
    public AudioSpectrumTile BuildSpectrum(double start, double duration, int columns, CancellationToken token,double intensity=1,int quality=1,int frequency=0)
    {
        var size=256<<Math.Clamp(quality,0,3);var bands=size/4;
        columns=Math.Clamp(columns,32,1024);
        var pixels=new byte[columns*bands*4];var primary=new byte[pixels.Length];
        var normalPalette=IcyBluePalette(false);var primaryPalette=IcyBluePalette(true);
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
                var fraction=y/(double)(bands-1);
                var mapped=frequency switch {1=>Math.Pow(fraction,1.3),2=>Math.Pow(fraction,1.7),3=>fraction*fraction,4=>(Math.Pow(size/2d,fraction)-1)/(size/2d-1),_=>fraction};
                var bin=1+(int)Math.Round(mapped*(size/2-1));
                var db=20*Math.Log10(Math.Max(1e-7,fft[bin].Magnitude/(size/2)));
                var value=Math.Clamp((db+80+20*Math.Log10(Math.Clamp(intensity,0.1,8)))/80,0,1);
                var offset=((bands-1-y)*columns+x)*4;
                var index=(int)Math.Round(value*255)*4;
                normalPalette.AsSpan(index,4).CopyTo(pixels.AsSpan(offset,4));
                primaryPalette.AsSpan(index,4).CopyTo(primary.AsSpan(offset,4));
            }
        }
        return new(columns,bands,pixels,primary);
    }
    // Icy Blue scheme parameters from Aegisub's default audio color configuration.
    // Generate both palettes once per tile; changing the selected line never reruns FFT.
    private static byte[] IcyBluePalette(bool primary)
    {
        var result=new byte[256*4];
        for(int i=0;i<256;i++)
        {
            var t=i/255d;var h=(191-128*t)/255d*6;var s=(127+128*t)/255d;
            var l=Math.Clamp((primary?64+192*t:255*t)/255d,0,1);
            var c=(1-Math.Abs(2*l-1))*s;var x=c*(1-Math.Abs(h%2-1));var m=l-c/2;
            var (r,g,b)=h switch{<1=>(c,x,0d),<2=>(x,c,0d),<3=>(0d,c,x),<4=>(0d,x,c),<5=>(x,0d,c),_=>(c,0d,x)};
            result[i*4]=(byte)Math.Round((b+m)*255);result[i*4+1]=(byte)Math.Round((g+m)*255);result[i*4+2]=(byte)Math.Round((r+m)*255);result[i*4+3]=255;
        }
        return result;
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
