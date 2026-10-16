using System.Globalization;
using System.Text;

namespace Yoake.Core.Media;

// Release formats are metadata only: this owns no media provider or clock.
public static class KeyframeFile
{
    private const int MaxEntries=1_000_000;
    private const long MaxBytes=64*1024*1024;
    public static IReadOnlyList<int> Load(string path,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();
        using var file=File.OpenRead(path);
        if(file.Length>MaxBytes)throw new ArgumentException("Keyframe file exceeds the 64 MiB limit.",nameof(path));
        using var reader=new StreamReader(file,new UTF8Encoding(false,true),detectEncodingFromByteOrderMarks:true);
        return Read(reader,token);
    }
    public static IReadOnlyList<int> Read(TextReader reader,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(reader);token.ThrowIfCancellationRequested();
        var header=reader.ReadLine()?.TrimStart('\uFEFF')??throw new ArgumentException("Keyframe file is empty.");
        var format=header=="# keyframe format v1"?0
            :header.StartsWith("# XviD 2pass stat file",StringComparison.Ordinal)||header.StartsWith("# ffmpeg 2-pass log file, using xvid codec",StringComparison.Ordinal)||header.StartsWith("# avconv 2-pass log file, using xvid codec",StringComparison.Ordinal)?1
            :header.StartsWith("##map version",StringComparison.Ordinal)?2
            :header.StartsWith("#options:",StringComparison.Ordinal)?3
            :header.StartsWith("# WWXD log file, using qpfile format",StringComparison.Ordinal)?4:-1;
        if(format<0)throw new ArgumentException("Unknown keyframe format. Choose an Aegisub keyframe list or supported encoder log.");
        if(format==0)
        {
            var fps=reader.ReadLine()?.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
            if(fps is not {Length:2}||fps[0]!="fps"||!double.TryParse(fps[1],NumberStyles.Float,CultureInfo.InvariantCulture,out var value)||!double.IsFinite(value))
                throw new ArgumentException("The keyframe list must contain an fps header (its value does not change video timing).");
        }
        SortedSet<int> keys=[];var frame=0;var lines=0;
        while(reader.ReadLine() is {} line)
        {
            token.ThrowIfCancellationRequested();
            if(++lines>10_000_000)throw new ArgumentException("Keyframe log exceeds the 10000000 line limit.");
            if(format==0)
            {
                if(string.IsNullOrWhiteSpace(line)||line.StartsWith('#'))continue;
                Add(ParseFrame(line));
            }
            else if(format==4)
            {
                if(string.IsNullOrWhiteSpace(line)||line.StartsWith('#'))continue;
                var parts=line.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
                if(parts.Length<2||parts[1].Length!=1)throw new ArgumentException("WWXD keyframe file must contain frame number and frame type.");
                var index=ParseFrame(parts[0]);if(parts[1]=="I")Add(index);
            }
            else
            {
                var type=format==1?(line.Length>0?line[0]:'\0'):format==2?Divx(line):X264(line);
                type=char.ToLowerInvariant(type);
                if(type=='i')Add(frame);
                if(type is 'i' or 'p' or 'b')frame=checked(frame+1);
            }
        }
        token.ThrowIfCancellationRequested();return Array.AsReadOnly(keys.ToArray());
        void Add(int index){keys.Add(index);if(keys.Count>MaxEntries)throw new ArgumentException("Keyframe file exceeds the 1000000 keyframe limit.");}
    }
    private static int ParseFrame(string value)=>int.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out var frame)&&frame>=0?frame
        :throw new ArgumentException("Keyframe indices must be non-negative integers.");
    private static char Divx(string line){foreach(var type in "IPB")if(line.Contains(type))return type;return '\0';}
    private static char X264(string line){var index=line.IndexOf("type:",StringComparison.Ordinal);return index>=0&&index+5<line.Length?line[index+5]:'\0';}
    public static void Save(string path,IReadOnlyList<int> keyframes,CancellationToken token=default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);ArgumentNullException.ThrowIfNull(keyframes);token.ThrowIfCancellationRequested();
        var full=Path.GetFullPath(path);var temporary=full+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var writer=new StreamWriter(new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None),new UTF8Encoding(false)))Write(writer,keyframes,token);
            token.ThrowIfCancellationRequested();File.Move(temporary,full,overwrite:true);
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    public static void Write(TextWriter writer,IReadOnlyList<int> keyframes,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(writer);ArgumentNullException.ThrowIfNull(keyframes);token.ThrowIfCancellationRequested();
        writer.WriteLine("# keyframe format v1");writer.WriteLine("fps 0");
        var previous=-1;
        foreach(var frame in keyframes)
        {
            token.ThrowIfCancellationRequested();if(frame<0||frame<=previous)throw new ArgumentException("Keyframe indices must be ordered, unique and non-negative.");
            writer.WriteLine(frame.ToString(CultureInfo.InvariantCulture));previous=frame;
        }
        token.ThrowIfCancellationRequested();
    }
}
