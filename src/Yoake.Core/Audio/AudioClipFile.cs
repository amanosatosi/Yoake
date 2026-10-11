using System.Text;
using Yoake.Core.Subtitles;

namespace Yoake.Core.Audio;

public readonly record struct AudioClipInterval(long StartMilliseconds,long EndMilliseconds)
{
    public static AudioClipInterval Capture(SubtitleEditor editor,IReadOnlyList<AssEvent> selection)
    {
        ArgumentNullException.ThrowIfNull(editor);ArgumentNullException.ThrowIfNull(selection);
        if(selection.Count==0)throw new ArgumentException("Select subtitle rows to export audio.");
        var owned=editor.Document.Events.ToHashSet();var start=long.MaxValue;var end=0L;
        foreach(var line in selection)
        {
            if(!owned.Contains(line))throw new ArgumentException("Audio clip selection belongs to another document.");
            if(line.StartMilliseconds is not {} left||line.EndMilliseconds is not {} right||left<0||right<left)
                throw new ArgumentException("A selected subtitle has invalid timing. Correct its stored start and end first.");
            start=Math.Min(start,left);end=Math.Max(end,right);
        }
        return new(start,end);
    }
}
public readonly record struct AudioClipFormat(int SampleRate,int Channels,long FrameCount);

// Bounded PCM conversion/export. Decoding remains behind the provider boundary;
// this service owns no playback gain, provider handle, document or clock.
public static class AudioClipFile
{
    private static (long Start,long End,uint Bytes) Geometry(AudioClipFormat format,AudioClipInterval interval)
    {
        if(format.SampleRate<=0||format.Channels is <1 or >32767||format.FrameCount<0||
            (long)format.SampleRate*format.Channels*2>uint.MaxValue||interval.StartMilliseconds<0||interval.EndMilliseconds<interval.StartMilliseconds)
            throw new ArgumentException("Invalid audio format or clip interval.");
        long Sample(long ms)=>(long)Int128.Min(format.FrameCount,((Int128)ms*format.SampleRate+999)/1000);
        var start=Sample(interval.StartMilliseconds);var end=Math.Max(start,Sample(interval.EndMilliseconds));
        var bytes=(Int128)(end-start)*format.Channels*2;
        if(bytes>uint.MaxValue-36)throw new ArgumentException("This clip exceeds the WAV 4 GiB limit. Select a shorter interval.");
        return(start,end,(uint)bytes);
    }
    public static short Pcm16(float sample)
    {
        if(!float.IsFinite(sample))return 0;
        var value=Math.Clamp((double)sample,-1,1);
        return (short)Math.Round(value*(value<0?32768:32767),MidpointRounding.AwayFromZero);
    }
    public static void Save(string path,AudioClipFormat format,AudioClipInterval interval,Func<long,int,float[]> read,
        CancellationToken token=default,Action<double>? progress=null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);ArgumentNullException.ThrowIfNull(read);token.ThrowIfCancellationRequested();Geometry(format,interval);
        var full=Path.GetFullPath(path);var temporary=full+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))Write(stream,format,interval,read,token,progress);
            token.ThrowIfCancellationRequested();File.Move(temporary,full,overwrite:true);
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    public static void Write(Stream stream,AudioClipFormat format,AudioClipInterval interval,Func<long,int,float[]> read,
        CancellationToken token=default,Action<double>? progress=null)
    {
        ArgumentNullException.ThrowIfNull(stream);ArgumentNullException.ThrowIfNull(read);token.ThrowIfCancellationRequested();
        var (start,end,bytes)=Geometry(format,interval);using var writer=new BinaryWriter(stream,Encoding.ASCII,leaveOpen:true);
        writer.Write("RIFF"u8);writer.Write(bytes+36);writer.Write("WAVEfmt "u8);writer.Write(16U);writer.Write((ushort)1);writer.Write((ushort)format.Channels);
        writer.Write((uint)format.SampleRate);writer.Write((uint)((long)format.SampleRate*format.Channels*2));writer.Write((ushort)(format.Channels*2));writer.Write((ushort)16);
        writer.Write("data"u8);writer.Write(bytes);progress?.Invoke(0);
        var chunk=Math.Max(1,65536/(format.Channels*2));var buffer=new byte[chunk*format.Channels*2];
        for(var frame=start;frame<end;)
        {
            token.ThrowIfCancellationRequested();var count=(int)Math.Min(chunk,end-frame);var samples=read(frame,count);
            if(samples is null||samples.Length!=count*format.Channels)throw new InvalidDataException("The audio provider returned incomplete clip frames.");
            for(var index=0;index<samples.Length;index++)
            {
                if((index&2047)==0)token.ThrowIfCancellationRequested();var pcm=Pcm16(samples[index]);
                buffer[index*2]=unchecked((byte)pcm);buffer[index*2+1]=unchecked((byte)(pcm>>8));
            }
            token.ThrowIfCancellationRequested();writer.Write(buffer,0,count*format.Channels*2);frame+=count;progress?.Invoke((double)(frame-start)/Math.Max(1,end-start));
        }
        token.ThrowIfCancellationRequested();writer.Flush();progress?.Invoke(1);token.ThrowIfCancellationRequested();
    }
}
