using System.Buffers.Binary;
using System.Text;

namespace Yoake.Core.Fonts;

public sealed record OpenTypeFaceMetadata(int Index,ushort? EmbeddingFlags,bool Variable,string[] Families,string[] FullNames,string[] Faces,string[] License);
public sealed record OpenTypeMetadata(OpenTypeFaceMetadata[] Faces)
{
    public bool CopyRestricted=>Faces.Any(f=>f.EmbeddingFlags is {} flags&&(flags&0x0202)!=0);
    public bool RightsUnknown=>Faces.Any(f=>f.EmbeddingFlags is null);
    public static OpenTypeMetadata Read(Stream stream,CancellationToken token=default)
    {
        if(!stream.CanSeek)throw new ArgumentException("Font metadata requires a seekable stream.");
        byte[] Read(long offset,int count)
        {token.ThrowIfCancellationRequested();if(offset<0||count<0||offset>stream.Length-count)throw new InvalidDataException("Font table lies outside its file.");stream.Position=offset;var bytes=new byte[count];stream.ReadExactly(bytes);return bytes;}
        var header=Read(0,12);var signature=BinaryPrimitives.ReadUInt32BigEndian(header);uint[] offsets;
        if(signature==0x74746366)
        {
            var count=BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8));if(count is 0 or >4096)throw new InvalidDataException("Invalid font collection face count.");
            var bytes=Read(12,checked((int)count*4));offsets=Enumerable.Range(0,(int)count).Select(i=>BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(i*4))).ToArray();
        }
        else offsets=[0];
        var result=new List<OpenTypeFaceMetadata>();
        for(var index=0;index<offsets.Length;index++)
        {
            var directory=Read(offsets[index],12);var version=BinaryPrimitives.ReadUInt32BigEndian(directory);
            if(version is not (0x00010000 or 0x4F54544F or 0x74727565))throw new InvalidDataException("Unsupported OpenType signature.");
            var count=BinaryPrimitives.ReadUInt16BigEndian(directory.AsSpan(4));if(count is 0 or >4096)throw new InvalidDataException("Invalid font table count.");
            var records=Read((long)offsets[index]+12,checked(count*16));ushort? rights=null;var variable=false;var names=new Dictionary<int,List<string>>();
            for(var i=0;i<count;i++)
            {
                token.ThrowIfCancellationRequested();var record=records.AsSpan(i*16,16);var tag=BinaryPrimitives.ReadUInt32BigEndian(record);var offset=BinaryPrimitives.ReadUInt32BigEndian(record[8..]);var length=BinaryPrimitives.ReadUInt32BigEndian(record[12..]);
                if(offset>stream.Length-length)throw new InvalidDataException("Font table lies outside its file.");
                if(tag==0x4F532F32&&length>=10)rights=BinaryPrimitives.ReadUInt16BigEndian(Read(offset,10).AsSpan(8));
                if(tag==0x66766172)variable=true;
                if(tag==0x6E616D65)
                {if(length>1024*1024)throw new InvalidDataException("Font name table exceeds the bounded metadata limit.");names=ReadNames(Read(offset,(int)length));}
            }
            string[] Names(params int[] ids)=>ids.SelectMany(id=>names.GetValueOrDefault(id)??[]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            result.Add(new(index,rights,variable,Names(16,1),Names(4),Names(17,2),Names(13,14)));
        }
        return new(result.ToArray());
    }
    public static Dictionary<int,List<string>> ReadNames(ReadOnlySpan<byte> table)
    {
        var result=new Dictionary<int,List<string>>();if(table.Length<6)throw new InvalidDataException("Incomplete font name table.");
        var count=BinaryPrimitives.ReadUInt16BigEndian(table[2..]);var strings=BinaryPrimitives.ReadUInt16BigEndian(table[4..]);
        if(6+count*12>table.Length||strings>table.Length)throw new InvalidDataException("Invalid font name records.");
        for(var i=0;i<count;i++)
        {
            var record=table.Slice(6+i*12,12);var platform=BinaryPrimitives.ReadUInt16BigEndian(record);var encoding=BinaryPrimitives.ReadUInt16BigEndian(record[2..]);var id=BinaryPrimitives.ReadUInt16BigEndian(record[6..]);var length=BinaryPrimitives.ReadUInt16BigEndian(record[8..]);var offset=strings+BinaryPrimitives.ReadUInt16BigEndian(record[10..]);
            if(offset>table.Length-length)throw new InvalidDataException("Font name lies outside its table.");var data=table.Slice(offset,length);string? name=null;
            if(platform==0||platform==3&&encoding is 0 or 1 or 10){if(length%2!=0)throw new InvalidDataException("Odd-length Unicode font name.");name=Encoding.BigEndianUnicode.GetString(data);}
            else if(platform==1&&encoding==0&&data.IndexOfAnyExceptInRange((byte)0,(byte)127)<0)name=Encoding.ASCII.GetString(data);
            if(name is null)continue;if(!result.TryGetValue(id,out var list))result[id]=list=[];if(!list.Contains(name,StringComparer.OrdinalIgnoreCase))list.Add(name);
        }
        return result;
    }
}
