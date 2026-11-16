using System.Text;
using Yoake.Core.Media;
using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class MatroskaSubtitleTests
{
    private static string Episode=>Path.Combine(AppContext.BaseDirectory,"Fixtures","Matroska","episode.mkv");
    [Fact]
    public void RealMuxedEpisodeRestoresScriptOrderTimingAndUnicodeAndDoesNotModifyContainer()
    {
        var before=File.ReadAllBytes(Episode);var tracks=MatroskaSubtitles.ReadTracks(Episode);
        Assert.Equal(2,tracks.Length);Assert.All(tracks,t=>Assert.True(t.Supported));
        var track=Assert.Single(tracks,t=>t.Codec=="S_TEXT/ASS");Assert.Equal("jpn",track.Language);Assert.Equal("Japanese ASS",track.Name);
        var document=MatroskaSubtitles.Extract(Episode,track.Number);Assert.Equal(2,document.Events.Count);
        var first=document.Events[0];Assert.Equal("2",first.Layer);Assert.Equal("0:00:01.25",first.Start);Assert.Equal("0:00:02.75",first.End);Assert.Equal("日本語",first.Actor);
        Assert.Equal("0010",first.MarginL);Assert.Equal("0020",first.MarginR);Assert.Equal("0030",first.MarginV);Assert.Equal("effect",first.Effect);
        Assert.Equal(@"{\k10\future(opaque)}日本語, မြန်မာ é",first.Text);Assert.Equal("0:00:00.10",document.Events[1].Start);
        Assert.Contains("PlayResX: 1280",document.Serialize());Assert.Equal(document.Serialize(),AssDocument.Parse(document.Serialize()).Serialize());Assert.Equal(before,File.ReadAllBytes(Episode));
        var plain=MatroskaSubtitles.Extract(Episode,tracks.Single(t=>t.Codec=="S_TEXT/UTF8").Number);
        Assert.Equal(@"日本語\Nမြန်မာ é",Assert.Single(plain.Events).Text);Assert.Equal("0:00:00.20",plain.Events[0].Start);Assert.Equal("0:00:01.70",plain.Events[0].End);
    }
    [Fact]
    public void CancellationAndMissingTrackNeverReturnPartialDocument()
    {
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>MatroskaSubtitles.ReadTracks(Episode,cancellation.Token));
        Assert.Throws<OperationCanceledException>(()=>MatroskaSubtitles.Extract(Episode,2,cancellation.Token));
        Assert.Throws<InvalidDataException>(()=>MatroskaSubtitles.Extract(Episode,999));
    }
    private static byte[] Element(uint id,params byte[][] fields)
    {
        var idBytes=BitConverter.GetBytes(id).Reverse().SkipWhile(b=>b==0).ToArray();var payload=fields.SelectMany(b=>b).ToArray();
        byte[] size=payload.Length<127?[(byte)(0x80|payload.Length)]:[(byte)(0x40|(payload.Length>>8)),(byte)payload.Length];
        return [..idBytes,..size,..payload];
    }
    private static byte[] UInt(uint id,byte value)=>Element(id,[value]);
    private static byte[] Text(uint id,string value)=>Element(id,Encoding.UTF8.GetBytes(value));
    private static byte[] Container(byte[] cluster,bool unknown=false,bool compressed=false,string codec="S_TEXT/UTF8",string? header=null)
    {
        var track=Element(0xAE,UInt(0xD7,1),UInt(0x83,0x11),Text(0x86,codec),compressed?Element(0x6D80):[],header is null?[]:Text(0x63A2,header));
        var body=new byte[][] { Element(0x1654AE6B,track),cluster }.SelectMany(b=>b).ToArray();
        return [..Element(0x1A45DFA3),..(unknown?(byte[])[0x18,0x53,0x80,0x67,0xFF,..body]:Element(0x18538067,body))];
    }
    private static void WithFile(byte[] bytes,Action<string> run)
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".mkv");try{File.WriteAllBytes(path,bytes);run(path);}finally{File.Delete(path);}
    }
    [Fact]
    public void UnknownSegmentAndClusterSizesRetainDurationAndSignedBlockOffset()
    {
        var group=Element(0xA0,Element(0xA1,[0x81,0xFF,0xF6,0,..Encoding.UTF8.GetBytes("日本語")]),UInt(0x9B,100));
        byte[] cluster=[0x1F,0x43,0xB6,0x75,0xFF,..UInt(0xE7,50),..group];
        WithFile(Container(cluster,true),path=>
        {
            var line=Assert.Single(MatroskaSubtitles.Extract(path,1).Events);Assert.Equal("0:00:00.04",line.Start);Assert.Equal("0:00:00.14",line.End);
        });
    }
    [Fact]
    public void CodecPrivateMetadataAndFutureSectionsSurviveSaveReopenInSourceOrder()
    {
        var header=AssDocument.CreateEmpty().Serialize()+
            "Comment: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,mangetsu-style,{\\future(opaque)}\r\n"+
            "Comment: 0,0:00:00.00,0:00:01.00,Default,Nene,0,0,0,mangetsu-colorcoding,{\\1c&HFF0000&}\r\n"+
            "Comment: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,{uketsugu},first 日本語\r\n"+
            "Comment: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,{uketsugu},second မြန်မာ\r\n"+
            "[Future Section]\r\nopaque: a,b,c\r\n";
        var payload=Encoding.UTF8.GetBytes("0,0,Default,Nene,0,0,0,,{\\unknown(opaque)}日本語, မြန်မာ");
        var group=Element(0xA0,Element(0xA1,[0x81,0,0,0,..payload]),UInt(0x9B,100));
        WithFile(Container(Element(0x1F43B675,UInt(0xE7,0),group),true,false,"S_TEXT/ASS",header),path=>
        {
            var document=MatroskaSubtitles.Extract(path,1);Assert.Equal(5,document.Events.Count);
            Assert.Equal(MangetsuMetadataKind.Style,MangetsuMetadata.Kind(document.Events[0]));Assert.Equal(MangetsuMetadataKind.Actor,MangetsuMetadata.Kind(document.Events[1]));
            Assert.Equal("first 日本語",document.Events[2].Text);Assert.Equal("second မြန်မာ",document.Events[3].Text);Assert.Equal("{uketsugu}",document.Events[2].Effect);Assert.Equal("{uketsugu}",document.Events[3].Effect);
            var output=path+".ass";
            try { document.Save(output);Assert.Equal(document.Serialize(),AssDocument.Load(output).Serialize());Assert.Contains("[Future Section]\r\nopaque: a,b,c",File.ReadAllText(output)); }
            finally { File.Delete(output); }
        });
    }
    [Fact]
    public void MalformedBoundsMissingDurationLacingAndCompressionAreRejected()
    {
        WithFile([0x1A,0x45,0xDF,0xA3,0x8F],path=>Assert.Throws<InvalidDataException>(()=>MatroskaSubtitles.ReadTracks(path)));
        foreach(var flags in new byte[] { 0,2 })
        {
            var cluster=Element(0x1F43B675,UInt(0xE7,0),Element(0xA3,[0x81,0,0,flags,65]));
            WithFile(Container(cluster,true),path=>{ if(flags==0)Assert.Throws<InvalidDataException>(()=>MatroskaSubtitles.Extract(path,1));else Assert.Throws<NotSupportedException>(()=>MatroskaSubtitles.Extract(path,1)); });
        }
        WithFile(Container([],true,true),path=>
        {
            Assert.False(Assert.Single(MatroskaSubtitles.ReadTracks(path)).Supported);Assert.Throws<NotSupportedException>(()=>MatroskaSubtitles.Extract(path,1));
        });
    }
}
