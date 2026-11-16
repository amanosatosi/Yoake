using System.Text;

namespace Yoake.Core.Subtitles;

public sealed record SubtitleEncodingChoice(int CodePage,string Name,string Description)
{
    public override string ToString()=>$"{Description} · {Name} ({CodePage})";
}
public static class SubtitleEncodings
{
    static SubtitleEncodings()=>Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    public static bool IsUnicode(int codePage)=>codePage is 65001 or 1200 or 1201 or 12000 or 12001;
    public static Encoding Get(int codePage)=>Encoding.GetEncoding(codePage,EncoderFallback.ExceptionFallback,DecoderFallback.ExceptionFallback);
    public static SubtitleEncodingChoice[] Choices()=>Encoding.GetEncodings().Select(e=>new SubtitleEncodingChoice(e.CodePage,e.Name,e.DisplayName)).OrderBy(e=>e.CodePage==65001?0:1).ThenBy(e=>e.Description).ToArray();
}

public sealed partial class AssDocument
{
    public int EncodingCodePage=>_encoding.CodePage;
    internal void RestoreSaveEncoding(int codePage) { _encoding=SubtitleEncodings.Get(codePage);_bom=false; }
    public static AssDocument Load(string path,Encoding encoding)=>LoadBytes(File.ReadAllBytes(path),encoding);
    public static AssDocument LoadBytes(byte[] bytes,Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(bytes);ArgumentNullException.ThrowIfNull(encoding);
        var strict=SubtitleEncodings.Get(encoding.CodePage);var preamble=strict.GetPreamble();
        var skip=preamble.Length>0&&bytes.AsSpan().StartsWith(preamble)?preamble.Length:0;
        try
        {
            var document=Parse(strict.GetString(bytes,skip,bytes.Length-skip));document._encoding=strict;document._bom=skip>0;return document;
        }
        catch(DecoderFallbackException error) { throw new InvalidDataException("The file is not valid "+strict.EncodingName+". Choose another character set; the original file was preserved.",error); }
    }
    public void SaveUtf8(string path,CancellationToken token=default)
    {
        var encoding=new UTF8Encoding(false,true);Save(path,encoding,false,token);
        // Remember the successful conversion for subsequent ordinary Saves.
        _encoding=encoding;_bom=false;
    }
}
