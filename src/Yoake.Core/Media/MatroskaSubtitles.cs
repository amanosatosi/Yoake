using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Yoake.Core.Subtitles;

namespace Yoake.Core.Media;

public sealed record MatroskaSubtitleTrack(ulong Number,string Name,string Language,string Codec,bool Supported)
{
    public override string ToString()=>$"{Number}: {(Name.Length==0?"Subtitles":Name)} · {Language} · {Codec}{(Supported?"":" (unsupported codec, compression or timestamp settings)")}";
}

// A bounded, seek-based reader of the Matroska subtitle subset. Video/audio
// payloads are never decoded or allocated. No native provider or CLI ownership.
public static class MatroskaSubtitles
{
    private const uint Segment=0x18538067,Cluster=0x1F43B675,Info=0x1549A966,Tracks=0x1654AE6B;
    private const int MaxPacket=1024*1024,MaxHeader=8*1024*1024,MaxText=64*1024*1024;
    private static readonly UTF8Encoding Utf8=new(false,true);
    private sealed record Track(MatroskaSubtitleTrack Public,byte[] Header,ulong DefaultDuration);
    private readonly record struct Element(uint Id,long Start,long End,bool Unknown);
    public static MatroskaSubtitleTrack[] ReadTracks(string path,CancellationToken token=default)
    {
        using var reader=new Reader(path,token);return ReadIndex(reader).Tracks.Select(t=>t.Public).ToArray();
    }
    public static AssDocument Extract(string path,ulong trackNumber,CancellationToken token=default)
    {
        using var reader=new Reader(path,token);var index=ReadIndex(reader);
        var track=index.Tracks.SingleOrDefault(t=>t.Public.Number==trackNumber)??throw new InvalidDataException("The subtitle track no longer exists.");
        if(!track.Public.Supported)throw new NotSupportedException("This subtitle codec, compression or timestamp settings are not supported. Choose an uncompressed ASS or UTF-8 track.");
        var header=Utf8.GetString(track.Header).TrimStart('\uFEFF');
        if(header.Contains('\0'))throw new InvalidDataException("Embedded ASS header contains NUL.");
        var document=track.Public.Codec=="S_TEXT/ASS"?AssDocument.Parse(header):AssDocument.CreateEmpty();
        if(document.Styles.Count==0)throw new InvalidDataException("The embedded ASS track has no usable V4+ style header.");
        var packets=new List<(long Order,AssEvent Line)>();long textSize=track.Header.Length;
        foreach(var cluster in index.Clusters)
        {
            token.ThrowIfCancellationRequested();ulong clusterTime=0;
            // Cluster timestamps apply to every block, even when not first.
            foreach(var child in reader.Children(cluster))if(child.Id==0xE7)clusterTime=reader.UInt(child);
            foreach(var child in reader.Children(cluster))
            {
                if(child.Id==0xA0)
                {
                    Element? block=null;ulong? duration=null;var codecState=false;
                    foreach(var field in reader.Children(child))
                    {
                        if(field.Id==0xA1) { if(block is not null)throw new InvalidDataException("Duplicate subtitle block.");block=field; }
                        else if(field.Id==0x9B)duration=reader.UInt(field);
                        else if(field.Id==0xA4)codecState=true;
                    }
                    if(block is {} value)ReadBlock(value,clusterTime,duration,codecState);
                }
                else if(child.Id==0xA3)ReadBlock(child,clusterTime,null);
            }
        }
        // Publish the complete event batch once, avoiding repeated source scans
        // for episode-sized scripts. A fresh Format protects unusual headers.
        if(packets.Count>0)
        {
            var source=document.Source.ToList();
            var position=source.FindIndex(line=>line.IsSectionHeader&&line.Raw.Trim().Equals("[Events]",StringComparison.OrdinalIgnoreCase));
            if(position<0)
            {
                if(source.Count>0&&source[^1].Ending.Length==0)source[^1]=source[^1] with { Ending="\r\n" };
                source.Add(new("[Events]","\r\n",null,true));position=source.Count-1;
            }
            position++;while(position<source.Count&&!source[position].IsSectionHeader)position++;
            if(position>0&&source[position-1].Ending.Length==0)source[position-1]=source[position-1] with { Ending="\r\n" };
            var added=new List<AssDocument.SourceLine> { new("Format: "+string.Join(", ",AssDocument.EventFormat),"\r\n") };
            foreach(var packet in packets.OrderBy(p=>p.Order)) { token.ThrowIfCancellationRequested();added.Add(new("","\r\n",packet.Line)); }
            source.InsertRange(position,added);document.Restore(source,true);
        }
        return document;

        void ReadBlock(Element block,ulong clusterTime,ulong? duration,bool codecState=false)
        {
            reader.Seek(block.Start);var number=reader.VInt(false,out _);
            if(number!=trackNumber)return;
            if(codecState)throw new NotSupportedException("Subtitle codec changes within a track are not supported.");
            if(block.End-reader.Position<3)throw new InvalidDataException("Truncated subtitle block.");
            var relative=unchecked((short)((reader.Byte()<<8)|reader.Byte()));var flags=reader.Byte();
            if((flags&6)!=0)throw new NotSupportedException("Laced subtitle blocks are not supported.");
            var length=block.End-reader.Position;
            if(length>MaxPacket||length<0||(textSize+=length)>MaxText||packets.Count>=500_000)throw new InvalidDataException("The embedded subtitle exceeds the safe import limit.");
            var text=Utf8.GetString(reader.Bytes((int)length));
            if(text.IndexOf('\0')>=0)throw new InvalidDataException("Embedded subtitle text contains NUL.");
            var startTicks=checked((decimal)clusterTime+relative);
            if(startTicks<0)throw new InvalidDataException("The subtitle block has a negative timestamp.");
            var startNanoseconds=startTicks*index.Scale;
            var start=(long)(startNanoseconds/1_000_000);
            var end=duration is {} ticks?(long)((startNanoseconds+(decimal)ticks*index.Scale)/1_000_000):
                track.DefaultDuration>0?(long)((startNanoseconds+track.DefaultDuration)/1_000_000):
                throw new InvalidDataException("The subtitle block has no duration; importing would lose timing.");
            var line=document.NewEvent();line.Start=AssTime.Format(start);line.End=AssTime.Format(end);
            long order=packets.Count;
            if(track.Public.Codec=="S_TEXT/ASS")
            {
                var fields=text.Split(',',9);
                if(fields.Length!=9||!long.TryParse(fields[0],NumberStyles.None,CultureInfo.InvariantCulture,out order)||!int.TryParse(fields[1],NumberStyles.Integer,CultureInfo.InvariantCulture,out _))throw new InvalidDataException("Invalid embedded ASS packet.");
                line.Set("Layer",fields[1]);line.Style=fields[2];line.Actor=fields[3];
                line.Set("MarginL",fields[4]);line.Set("MarginR",fields[5]);line.Set("MarginV",fields[6]);line.Effect=fields[7];line.Text=fields[8];
                if(line.Text.Contains('\r')||line.Text.Contains('\n'))throw new InvalidDataException("An ASS packet contains physical line breaks.");
            }
            else line.Text=text.Replace("\r\n","\\N").Replace("\r","\\N").Replace("\n","\\N");
            packets.Add((order,line));
        }
    }
    private static (List<Track> Tracks,List<Element> Clusters,ulong Scale) ReadIndex(Reader reader)
    {
        var first=reader.Header(reader.Length);
        if(first.Id!=0x1A45DFA3||first.Unknown)throw new InvalidDataException("This file is not a Matroska container.");
        reader.Seek(first.End);Element segment;
        do { segment=reader.Header(reader.Length);reader.Seek(segment.End); } while(segment.Id!=Segment&&reader.Position<reader.Length);
        if(segment.Id!=Segment)throw new InvalidDataException("The Matroska segment is missing.");
        var tracks=new List<Track>();var clusters=new List<Element>();ulong scale=1_000_000;long headersSize=0;
        reader.Seek(segment.Start);
        while(reader.Position<segment.End)
        {
            var element=reader.Header(segment.End);
            if(element.Unknown&&element.Id!=Cluster)throw new InvalidDataException("Unsupported unknown-sized Matroska element.");
            if(element.Id==Cluster)
            {
                if(element.Unknown)element=element with { End=reader.ClusterEnd(segment.End) };
                if(clusters.Count>=1_000_000)throw new InvalidDataException("Too many Matroska clusters.");clusters.Add(element);
            }
            else if(element.Id==Info)
            {
                foreach(var field in reader.Children(element))if(field.Id==0x2AD7B1)scale=reader.UInt(field);
                if(scale==0||scale>1_000_000_000)throw new InvalidDataException("Invalid Matroska timestamp scale.");
            }
            else if(element.Id==Tracks)
            {
                foreach(var entry in reader.Children(element).Where(e=>e.Id==0xAE))
                {
                    ulong number=0,type=0,defaultDuration=0;string name="",language="und",codec="";byte[] header=[];var compressed=false;var scaled=false;
                    foreach(var field in reader.Children(entry))switch(field.Id)
                    {
                        case 0xD7:number=reader.UInt(field);break;
                        case 0x83:type=reader.UInt(field);break;
                        case 0x86:codec=reader.String(field);break;
                        case 0x536E:name=reader.String(field);break;
                        case 0x22B59C:language=reader.String(field);break;
                        case 0x22B59D:language=reader.String(field);break;
                        case 0x23E383:defaultDuration=reader.UInt(field);break;
                        case 0x63A2:header=reader.Payload(field,MaxHeader);break;
                        case 0x6D80:compressed=true;break;
                        case 0x23314F:scaled|=reader.Float(field)!=1;break;
                        case 0x56AA:scaled|=reader.UInt(field)!=0;break;
                        case 0x537F:scaled|=reader.UInt(field)!=0;break;
                    }
                    if(type!=0x11)continue;
                    if(number==0||tracks.Any(t=>t.Public.Number==number)||tracks.Count>=256)throw new InvalidDataException("Invalid subtitle track number/count.");
                    if((headersSize+=header.Length)>MaxText)throw new InvalidDataException("Subtitle headers exceed the safe import limit.");
                    tracks.Add(new(new(number,name,language,codec,!compressed&&!scaled&&codec is "S_TEXT/ASS" or "S_TEXT/UTF8"),header,defaultDuration));
                }
            }
            reader.Seek(element.End);
        }
        return(tracks,clusters,scale);
    }
    private sealed class Reader : IDisposable
    {
        private readonly FileStream _stream;private readonly CancellationToken _token;
        public Reader(string path,CancellationToken token){_token=token;token.ThrowIfCancellationRequested();_stream=new(path,FileMode.Open,FileAccess.Read,FileShare.Read);}
        public long Length=>_stream.Length;
        public long Position=>_stream.Position;
        public void Seek(long position){_token.ThrowIfCancellationRequested();_stream.Position=position;}
        public int Byte(){var value=_stream.ReadByte();if(value<0)throw new EndOfStreamException("Truncated Matroska element.");return value;}
        public ulong VInt(bool id,out bool unknown)
        {
            var first=Byte();var marker=0x80;var width=1;
            while((first&marker)==0&&width<=8){marker>>=1;width++;}
            if(width>8||id&&width>4)throw new InvalidDataException("Invalid Matroska variable integer.");
            ulong value=(ulong)(id?first:first&(marker-1));
            for(var i=1;i<width;i++)value=(value<<8)|(uint)Byte();
            unknown=!id&&value==((1UL<<(width*7))-1);return value;
        }
        public Element Header(long end)
        {
            _token.ThrowIfCancellationRequested();var id=(uint)VInt(true,out _);var size=VInt(false,out var unknown);
            if(Position>end||!unknown&&size>(ulong)(end-Position))throw new InvalidDataException("A Matroska element exceeds its parent.");
            return new(id,Position,unknown?end:Position+(long)size,unknown);
        }
        public IEnumerable<Element> Children(Element parent)
        {
            Seek(parent.Start);
            while(Position<parent.End)
            {
                var child=Header(parent.End);if(child.Unknown)throw new InvalidDataException("Unsupported unknown-sized nested element.");
                yield return child;Seek(child.End);
            }
        }
        public long ClusterEnd(long end)
        {
            while(Position<end)
            {
                var start=Position;var child=Header(end);
                if(child.Id is Cluster or Info or Tracks or 0x1C53BB6B or 0x114D9B74 or 0x1941A469 or 0x1043A770 or 0x1254C367)return start;
                if(child.Unknown)throw new InvalidDataException("Unsupported unknown-sized cluster child.");Seek(child.End);
            }
            return end;
        }
        public byte[] Bytes(int length){var bytes=new byte[length];_stream.ReadExactly(bytes);return bytes;}
        public byte[] Payload(Element e,int maximum){if(e.End-e.Start>maximum)throw new InvalidDataException("Matroska field exceeds the safe import limit.");Seek(e.Start);return Bytes((int)(e.End-e.Start));}
        public string String(Element e)=>Utf8.GetString(Payload(e,64*1024)).TrimEnd('\0');
        public double Float(Element e)
        {
            var bytes=Payload(e,8);
            return bytes.Length switch { 4=>BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(bytes)),8=>BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(bytes)),_=>throw new InvalidDataException("Invalid Matroska float.") };
        }
        public ulong UInt(Element e)
        {
            var bytes=Payload(e,8);if(bytes.Length==0)throw new InvalidDataException("Empty Matroska integer.");ulong value=0;foreach(var b in bytes)value=(value<<8)|b;return value;
        }
        public void Dispose()=>_stream.Dispose();
    }
}
