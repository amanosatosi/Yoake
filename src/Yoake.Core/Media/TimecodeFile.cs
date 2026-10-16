using System.Globalization;
using System.Text;

namespace Yoake.Core.Media;

// Immutable metadata over the existing frame-boundary calculations. No clock,
// playback state, document or provider lifetime is introduced.
public sealed class ExternalTimecodes
{
    public IReadOnlyList<long> Milliseconds {get;}
    public FrameTiming Timing {get;}
    internal Int128 Numerator {get;}
    internal Int128 Last {get;}
    internal ExternalTimecodes(long[] milliseconds,Int128 numerator,Int128 last,CancellationToken token)
    {
        Milliseconds=Array.AsReadOnly(milliseconds);Numerator=numerator;Last=last;
        Timing=FrameTiming.FromExternal(milliseconds,numerator,last,token);
    }
    public IReadOnlyList<double> ForVideo(int frameCount,CancellationToken token=default)
    {
        if(frameCount is <0 or >10_000_000)throw new ArgumentException("Video frame count exceeds the supported range.");
        var seconds=new double[frameCount];
        for(var frame=0;frame<frameCount;frame++){token.ThrowIfCancellationRequested();seconds[frame]=Timing.MillisecondsAtFrame(frame)/1000d;}
        return Array.AsReadOnly(seconds);
    }
    public VideoTimecodeMap ForVideoMap(int frameCount,CancellationToken token=default)
        =>new(this,ForVideo(frameCount,token),Timing.MillisecondsAtFrame(frameCount)/1000d);
}

// Prepared off the UI thread, then published atomically by the provider.
public sealed class VideoTimecodeMap
{
    public ExternalTimecodes Source {get;}
    public IReadOnlyList<double> FrameTimes {get;}
    public double DurationSeconds {get;}
    internal VideoTimecodeMap(ExternalTimecodes source,IReadOnlyList<double> times,double duration)
    {Source=source;FrameTimes=times;DurationSeconds=duration;}
}

public static class TimecodeFile
{
    private const int MaxFrames=10_000_000;
    private const long Denominator=1_000_000_000;
    public static ExternalTimecodes FromVideo(IReadOnlyList<double> seconds,double fps,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(seconds);
        if(seconds.Count==0||seconds.Count>MaxFrames)throw new ArgumentException("Video timestamps must not be empty or exceed the frame limit.");
        var timing=new FrameTiming(seconds,fps,token);var times=new long[seconds.Count];var origin=timing.MillisecondsAtFrame(0);
        for(var frame=0;frame<times.Length;frame++){token.ThrowIfCancellationRequested();times[frame]=checked(timing.MillisecondsAtFrame(frame)-origin);}
        var last=times.Length>1?(times.Length-1)*(Int128)Denominator*1000:0;
        var numerator=times.Length>1?last/times[^1]:(Int128)(Rate(fps.ToString("R",CultureInfo.InvariantCulture))*Denominator);
        return new(times,numerator,last,token);
    }
    public static ExternalTimecodes Load(string path,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();using var file=File.OpenRead(path);
        if(file.Length>128L*1024*1024)throw new ArgumentException("Timecode file exceeds the 128 MiB limit.");
        using var reader=new StreamReader(file,new UTF8Encoding(false,true),detectEncodingFromByteOrderMarks:true);return Read(reader,token);
    }
    public static ExternalTimecodes Read(TextReader reader,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(reader);token.ThrowIfCancellationRequested();
        var header=reader.ReadLine()?.TrimStart('\uFEFF')??throw new ArgumentException("Timecode file is empty.");
        if(header=="# timecode format v2")return V2(reader,token);
        if(header=="# timecode format v1")header=reader.ReadLine()??"";
        if(!header.StartsWith("Assume ",StringComparison.Ordinal))throw new ArgumentException("Choose a timecode format v1 or v2 file.");
        return V1(reader,Rate(header[7..]),token);
    }
    private static double Rate(string value)
    {
        if(!double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var rate)||!double.IsFinite(rate)||rate<=0||rate>1000||rate*Denominator<1)
            throw new ArgumentException("Frame rates must be finite, greater than zero and at most 1000 fps.");
        return rate;
    }
    private static ExternalTimecodes V2(TextReader reader,CancellationToken token)
    {
        List<long> times=[];
        while(reader.ReadLine() is {} line)
        {
            token.ThrowIfCancellationRequested();line=line.Trim();if(line.Length==0||line.StartsWith('#'))continue;
            // The release reads integer prefixes from decimal timestamp lines.
            // Accept valid decimal numbers with that truncation, but reject
            // malformed numeric tails rather than silently skipping entries.
            if(!double.TryParse(line,NumberStyles.Float,CultureInfo.InvariantCulture,out var value)||!double.IsFinite(value)||value<int.MinValue||value>int.MaxValue)
                throw new ArgumentException("V2 timestamps must be finite milliseconds in the supported range.");
            var time=(long)value;if(times.Count>0&&time<times[^1])throw new ArgumentException("V2 timestamps must be ordered.");
            times.Add(time);if(times.Count>MaxFrames)throw new ArgumentException("Timecode file exceeds the 10000000 frame limit.");
        }
        if(times.Count<2||times[0]==times[^1])throw new ArgumentException("V2 timecodes require at least two timestamps spanning a positive duration.");
        var first=times[0];for(var index=0;index<times.Count;index++){token.ThrowIfCancellationRequested();times[index]-=first;}
        if(times[^1]>int.MaxValue)throw new ArgumentException("Normalized V2 duration exceeds the supported range.");
        var last=(times.Count-1)*(Int128)Denominator*1000;var numerator=last/times[^1];
        return new(times.ToArray(),numerator,last,token);
    }
    private sealed record Range(int Start,int End,double Rate);
    private static ExternalTimecodes V1(TextReader reader,double assumed,CancellationToken token)
    {
        List<Range> ranges=[];
        while(reader.ReadLine() is {} line)
        {
            token.ThrowIfCancellationRequested();line=line.Trim();if(line.Length==0||line.StartsWith('#'))continue;
            var parts=line.Split(',');
            if(parts.Length!=3||!int.TryParse(parts[0],NumberStyles.Integer,CultureInfo.InvariantCulture,out var start)||!int.TryParse(parts[1],NumberStyles.Integer,CultureInfo.InvariantCulture,out var end))
                throw new ArgumentException("V1 override ranges must be start,end,fps.");
            if(start<0||end<start||end>MaxFrames-2)throw new ArgumentException("V1 frame ranges must be non-negative, ordered and within the 10000000 frame limit.");
            ranges.Add(new(start,end,Rate(parts[2])));if(ranges.Count>MaxFrames)throw new ArgumentException("Too many V1 override ranges.");
        }
        ranges.Sort((left,right)=>left.Start.CompareTo(right.Start));token.ThrowIfCancellationRequested();
        List<long> times=[];var frame=0;var time=0d;
        void Append()
        {
            token.ThrowIfCancellationRequested();if(!double.IsFinite(time)||time<0||time>int.MaxValue-.5)throw new ArgumentException("V1 timestamp exceeds the supported range.");
            times.Add((long)(time+.5));
        }
        foreach(var range in ranges)
        {
            if(frame>range.Start)throw new ArgumentException("V1 override ranges must not overlap.");
            for(;frame<range.Start;frame++){Append();time+=1000/assumed;}
            for(;frame<=range.End;frame++){Append();time+=1000/range.Rate;}
        }
        Append();var anchor=time*assumed*Denominator;
        if(!double.IsFinite(anchor)||anchor>=Math.Pow(2,63))throw new ArgumentException("V1 extrapolation timestamp exceeds the supported range.");
        return new(times.ToArray(),(long)(assumed*Denominator),(long)anchor,token);
    }
    public static void Save(string path,ExternalTimecodes timecodes,int videoFrameCount=0,CancellationToken token=default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);ArgumentNullException.ThrowIfNull(timecodes);token.ThrowIfCancellationRequested();
        var full=Path.GetFullPath(path);var temporary=full+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var writer=new StreamWriter(new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None),new UTF8Encoding(false)))Write(writer,timecodes,videoFrameCount,token);
            token.ThrowIfCancellationRequested();File.Move(temporary,full,overwrite:true);
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    public static void Write(TextWriter writer,ExternalTimecodes timecodes,int videoFrameCount=0,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(writer);ArgumentNullException.ThrowIfNull(timecodes);token.ThrowIfCancellationRequested();
        if(videoFrameCount is <0 or >MaxFrames)throw new ArgumentException("Video frame count exceeds the supported range.");
        writer.WriteLine("# timecode format v2");var count=Math.Max(timecodes.Milliseconds.Count,videoFrameCount);
        for(var frame=0;frame<count;frame++){token.ThrowIfCancellationRequested();writer.WriteLine(timecodes.Timing.MillisecondsAtFrame(frame).ToString(CultureInfo.InvariantCulture));}
        token.ThrowIfCancellationRequested();
    }
}
