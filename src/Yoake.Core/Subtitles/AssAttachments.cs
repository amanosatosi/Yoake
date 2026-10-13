using System.Text;

// ASS encoding algorithm adapted from the supplied Aegisub 3.5 source:
// libaegisub/ass/uuencode.cpp, Copyright (c) 2013 Thomas Goyne.
// Permission to use, copy, modify, and distribute this software for any
// purpose with or without fee is hereby granted, provided that the above
// copyright notice and this permission notice appear in all copies.
// THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
// WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
// MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
// ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
// WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
// ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
// OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.

namespace Yoake.Core.Subtitles;

public enum AssAttachmentKind { Font, Graphic }

// Shared by the lossless parser and attachment scanner. A valid encoded line
// wins over apparent section syntax while an attachment is in progress.
internal sealed class AssAttachmentContext
{
    private bool _active;
    private string _section="";
    public (bool Section,bool Data) Observe(string raw)
    {
        if(_active)
        {
            if(AssAttachmentCodec.IsDataLine(raw)) {_active=raw.Length==80;return(false,true);}
            _active=false;
        }
        var trim=raw.Trim();
        if(trim.StartsWith('[')&&trim.EndsWith(']')) {_section=trim;return(true,false);}
        if((_section.Equals("[Fonts]",StringComparison.OrdinalIgnoreCase)&&raw.StartsWith("fontname: ",StringComparison.Ordinal))||
           (_section.Equals("[Graphics]",StringComparison.OrdinalIgnoreCase)&&raw.StartsWith("filename: ",StringComparison.Ordinal)))_active=true;
        return(false,false);
    }
}

public static class AssAttachmentCodec
{
    public const int MaximumDecodedBytes=32*1024*1024;
    internal static bool IsDataLine(string line)=>line.Length is >0 and <=80&&line.All(c=>c is >= '!' and <= '`');
    public static string[] Encode(ReadOnlySpan<byte> bytes,CancellationToken token=default)
    {
        if(bytes.Length>MaximumDecodedBytes)throw new InvalidDataException("Attachment exceeds the 32 MiB limit.");
        var lines=new List<string>();var line=new StringBuilder(80);Span<int> values=stackalloc int[4];
        for(var pos=0;pos<bytes.Length;pos+=3)
        {
            if((pos&4095)==0)token.ThrowIfCancellationRequested();
            var a=bytes[pos];var b=pos+1<bytes.Length?bytes[pos+1]:0;var c=pos+2<bytes.Length?bytes[pos+2]:0;
            values[0]=a>>2;values[1]=((a&3)<<4)|(b>>4);values[2]=((b&15)<<2)|(c>>6);values[3]=c&63;
            for(var i=0;i<Math.Min(bytes.Length-pos+1,4);i++)
            {
                line.Append((char)(values[i]+33));
                if(line.Length==80){lines.Add(line.ToString());line.Clear();}
            }
        }
        if(line.Length>0)lines.Add(line.ToString());token.ThrowIfCancellationRequested();return lines.ToArray();
    }
    public static int Inspect(IReadOnlyList<string> lines,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();
        long length=0;var last=0;
        for(var i=0;i<lines.Count;i++)
        {
            token.ThrowIfCancellationRequested();var line=lines[i];
            if(!IsDataLine(line)||(i<lines.Count-1&&line.Length!=80))throw new InvalidDataException("Malformed ASS attachment encoding or line wrapping.");
            length+=line.Length;last=line[^1]-33;
            if(length>MaximumDecodedBytes*4L/3+3)throw new InvalidDataException("Attachment exceeds the 32 MiB limit.");
        }
        var remainder=length%4;
        if(remainder==1||(remainder==2&&(last&15)!=0)||(remainder==3&&(last&3)!=0))throw new InvalidDataException("Invalid final ASS attachment group.");
        var decoded=length/4*3+(remainder==0?0:remainder-1);
        if(decoded>MaximumDecodedBytes)throw new InvalidDataException("Attachment exceeds the 32 MiB limit.");
        return (int)decoded;
    }
    public static byte[] Decode(IReadOnlyList<string> lines,CancellationToken token=default)
    {
        var bytes=new byte[Inspect(lines,token)];var offset=0;var bits=0;var accumulator=0;
        foreach(var line in lines)
        {
            token.ThrowIfCancellationRequested();
            foreach(var c in line)
            {
                accumulator=(accumulator<<6)|(c-33);bits+=6;
                if(bits>=8){bits-=8;bytes[offset++]=(byte)(accumulator>>bits);accumulator&=(1<<bits)-1;}
            }
        }
        return bytes;
    }
}

public static class AttachmentFileNames
{
    public static bool IsSafe(string name)
    {
        if(string.IsNullOrEmpty(name)||name is "." or ".."||name.Length>255||name[^1] is '.' or ' '||
           name.Any(c=>char.IsControl(c)||c is '\\' or '/' or ':' or '<' or '>' or '"' or '|' or '?' or '*')||Path.IsPathRooted(name))return false;
        var stem=name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        if(stem is "CON" or "PRN" or "AUX" or "NUL" or "CLOCK$" or "CONIN$" or "CONOUT$"||
           (stem.Length==4&&(stem.StartsWith("COM",StringComparison.Ordinal)||stem.StartsWith("LPT",StringComparison.Ordinal))&&stem[3] is >= '1' and <= '9' or '\u00b9' or '\u00b2' or '\u00b3'))return false;
        return true;
    }
    public static void RequireSafe(string name){if(!IsSafe(name))throw new InvalidDataException("Unsafe attachment filename: "+name);}
}

/// <summary>Immutable encoded strings, shared with source/undo; listing allocates no decoded payload.</summary>
public sealed class AssAttachment
{
    internal Guid Owner {get;}
    internal int Start {get;}
    internal int Count {get;}
    internal string[] Lines {get;}
    public long Revision {get;}
    public string Name {get;}
    public AssAttachmentKind Kind {get;}
    public int? DecodedSize {get;}
    public string? Error {get;}
    public string ExtractionName
    {
        get
        {
            // Strip the canonical SSA encoding suffix, retaining ordinary
            // underscores in filenames rather than upstream's blanket removal.
            if(Kind==AssAttachmentKind.Font&&Name.EndsWith(".ttf",StringComparison.OrdinalIgnoreCase))
            {
                var stem=Name[..^4];var underscore=stem.LastIndexOf('_');
                if(underscore>=0&&stem[(underscore+1)..] is {Length:>0} suffix&&suffix.All(char.IsAsciiDigit))return stem[..underscore]+Name[^4..];
            }
            return Name;
        }
    }
    public bool CanExtract=>Error is null&&DecodedSize is not null&&AttachmentFileNames.IsSafe(ExtractionName);
    public bool BelongsTo(AssDocument document)=>Owner==document.AttachmentOwner&&Revision==document.Revision;
    internal AssAttachment(Guid owner,long revision,int start,int count,string name,AssAttachmentKind kind,string[] lines,string? error,CancellationToken token)
    {
        (Owner,Revision,Start,Count,Name,Kind,Lines)=(owner,revision,start,count,name,kind,lines);
        if(error is null)try{DecodedSize=AssAttachmentCodec.Inspect(lines,token);}catch(InvalidDataException e){error=e.Message;}
        Error=error;
    }
    public byte[] Decode(CancellationToken token=default)
    {if(Error is {} error)throw new InvalidDataException(error);return AssAttachmentCodec.Decode(Lines,token);}
    public override string ToString()=>$"{Name}  ·  {Kind}  ·  {(DecodedSize is {} size?$"{size:N0} bytes":"unrecognized payload")}"+(Error is {} error?"  ·  "+error:!CanExtract?"  ·  unsafe extraction filename":"");
}

public sealed class PreparedAssAttachment
{
    public string Name {get;}
    public AssAttachmentKind Kind {get;}
    internal string[] Lines {get;}
    private PreparedAssAttachment(AssAttachmentKind kind,string name,string[] lines)=>(Kind,Name,Lines)=(kind,name,lines);
    public static PreparedAssAttachment Create(AssAttachmentKind kind,string name,ReadOnlySpan<byte> bytes,CancellationToken token=default)
    {
        if(!Enum.IsDefined(kind))throw new InvalidDataException("Unsupported attachment type.");
        AttachmentFileNames.RequireSafe(name);
        if(kind==AssAttachmentKind.Font&&name.EndsWith(".ttf",StringComparison.OrdinalIgnoreCase))name=name[..^4]+"_0"+name[^4..];
        AttachmentFileNames.RequireSafe(name);return new(kind,name,AssAttachmentCodec.Encode(bytes,token));
    }
}

public static class AssAttachmentExtraction
{
    public static Task WriteAsync(AssAttachment attachment,string directory,bool overwrite,RecoveryLease lease,long generation,CancellationToken token=default)=>Task.Run(()=>
    {
        AttachmentFileNames.RequireSafe(attachment.ExtractionName);
        if(!attachment.CanExtract)throw new InvalidDataException(attachment.Error??"Attachment cannot be extracted.");
        var destination=Path.Combine(Path.GetFullPath(directory),attachment.ExtractionName);
        var temporary=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            var bytes=attachment.Decode(token);
            using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                for(var offset=0;offset<bytes.Length;offset+=65536){token.ThrowIfCancellationRequested();stream.Write(bytes,offset,Math.Min(65536,bytes.Length-offset));}
                stream.Flush(true);
            }
            lease.Publish(generation,()=>{token.ThrowIfCancellationRequested();File.Move(temporary,destination,overwrite);});
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    },token);
}

public sealed partial class SubtitleEditor
{
    public void ImportAttachments(IReadOnlyList<PreparedAssAttachment> attachments,long expectedRevision,CancellationToken token=default)
    {
        if(Document.Revision!=expectedRevision)throw new InvalidOperationException("The subtitle document changed during attachment import.");
        var names=Document.AttachmentNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
        long encoded=0;
        foreach(var attachment in attachments)
        {token.ThrowIfCancellationRequested();encoded+=attachment.Lines.Sum(line=>(long)line.Length);if(encoded>64L*1024*1024*4/3+4)throw new InvalidDataException("Attachment batch exceeds the 64 MiB limit.");AttachmentFileNames.RequireSafe(attachment.Name);if(!names.Add(attachment.Name))throw new InvalidDataException("Duplicate attachment filename: "+attachment.Name);}
        if(attachments.Count==0)return;
        using var update=Document.BeginUpdate();using var transaction=Undo.BeginTransaction("Import attachments");
        Structure("Import attachments",()=>{foreach(var attachment in attachments){token.ThrowIfCancellationRequested();Document.InsertAttachment(attachment);}});
        token.ThrowIfCancellationRequested();transaction.Commit();
    }
    public void RemoveAttachments(IReadOnlyList<AssAttachment> attachments,CancellationToken token=default)
    {
        if(attachments.Any(a=>a.Owner!=Document.AttachmentOwner||a.Revision!=Document.Revision))throw new InvalidOperationException("Attachment selection belongs to another document revision.");
        var ordered=attachments.DistinctBy(a=>a.Start).OrderByDescending(a=>a.Start).ToArray();
        if(ordered.Length==0)return;
        using var update=Document.BeginUpdate();using var transaction=Undo.BeginTransaction("Remove attachments");
        Structure("Remove attachments",()=>{foreach(var attachment in ordered){token.ThrowIfCancellationRequested();Document.Source.RemoveRange(attachment.Start,attachment.Count);}Document.Touch();});
        token.ThrowIfCancellationRequested();transaction.Commit();
    }
}

public sealed partial class AssDocument
{
    private Guid _attachmentOwner=Guid.NewGuid();
    internal Guid AttachmentOwner=>_attachmentOwner;
    internal IEnumerable<string> AttachmentNames()
    {
        var section="";
        foreach(var line in Source)
        {
            if(line.IsSectionHeader)section=line.Raw.Trim();
            else if(IsAttachmentHeader(line.Raw,section,out _))yield return line.Raw[10..];
        }
    }
    private static bool IsAttachmentHeader(string raw,string section,out AssAttachmentKind kind)
    {
        kind=AssAttachmentKind.Font;
        if(section.Equals("[Fonts]",StringComparison.OrdinalIgnoreCase)&&raw.StartsWith("fontname: ",StringComparison.Ordinal))return true;
        kind=AssAttachmentKind.Graphic;
        return section.Equals("[Graphics]",StringComparison.OrdinalIgnoreCase)&&raw.StartsWith("filename: ",StringComparison.Ordinal);
    }
    public AssAttachment[] GetAttachments(CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();
        var entries=new List<AssAttachment>();var section="";
        for(var index=0;index<Source.Count;index++)
        {
            token.ThrowIfCancellationRequested();var line=Source[index];
            if(line.IsSectionHeader){section=line.Raw.Trim();continue;}
            if(!IsAttachmentHeader(line.Raw,section,out var kind))continue;
            var start=index;var data=new List<string>();string? error=null;
            while(index+1<Source.Count)
            {
                var next=Source[index+1];
                if(next.IsSectionHeader||IsAttachmentHeader(next.Raw,section,out _)||next.Raw.Length==0)break;
                if(!next.IsAttachmentData){error="Unrecognized attachment data preserved; extraction is unavailable.";break;}
                index++;data.Add(next.Raw);
                if(next.IsAttachmentData&&next.Raw.Length<80)break;
            }
            entries.Add(new(_attachmentOwner,Revision,start,index-start+1,line.Raw[10..],kind,data.ToArray(),error,token));
        }
        return entries.ToArray();
    }
    internal void InsertAttachment(PreparedAssAttachment attachment)
    {
        var section=attachment.Kind==AssAttachmentKind.Font?"[Fonts]":"[Graphics]";
        var header=Source.FindIndex(l=>l.IsSectionHeader&&l.Raw.Trim().Equals(section,StringComparison.OrdinalIgnoreCase));
        if(header<0){Terminate();Source.Add(new(section,_newline,null,true));header=Source.Count-1;}
        var index=header+1;while(index<Source.Count&&!Source[index].IsSectionHeader)index++;
        if(index>0&&Source[index-1].Ending.Length==0)Source[index-1]=Source[index-1] with{Ending=_newline};
        var inserted=new List<SourceLine>{new((attachment.Kind==AssAttachmentKind.Font?"fontname: ":"filename: ")+attachment.Name,_newline)};
        foreach(var data in attachment.Lines)inserted.Add(new(data,_newline,null,false,true));
        // The explicit separator terminates empty or exactly-80-character data
        // before a later section whose spelling might itself be valid encoding.
        inserted.Add(new("",_newline));Source.InsertRange(index,inserted);Touch();
    }
}
