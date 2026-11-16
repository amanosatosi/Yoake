using System.Text;
using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class SubtitleEncodingTests
{
    private static string Source=>AssDocument.CreateEmpty().Serialize()+"Dialogue: 0,0:00:00.00,0:00:01.00,Default,日本語,0,0,0,,日本語{\\future(opaque)}\r\n";
    [Fact]
    public void ShiftJisRetainsEncodingUntilExplicitUtf8Conversion()
    {
        var root=Path.Combine(Path.GetTempPath(),"Yoake-encoding-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            var encoding=SubtitleEncodings.Get(932);var bytes=encoding.GetBytes(Source);var document=AssDocument.LoadBytes(bytes,encoding);
            Assert.Equal(Source,document.Serialize());Assert.Equal(932,document.EncodingCodePage);
            var path=Path.Combine(root,"legacy.ass");document.Save(path);Assert.Equal(bytes,File.ReadAllBytes(path));
            document.Events[0].Text="မြန်မာ é";
            Assert.Throws<EncoderFallbackException>(()=>document.Save(path));Assert.Equal(bytes,File.ReadAllBytes(path));Assert.Empty(Directory.GetFiles(root,"*.tmp"));
            document.SaveUtf8(path);Assert.Equal(65001,document.EncodingCodePage);Assert.Equal(document.Serialize(),AssDocument.Load(path).Serialize());
            document.Events[0].Text+=" ထပ်";document.Save(path);Assert.Equal(document.Serialize(),AssDocument.Load(path).Serialize());
        }
        finally { Directory.Delete(root,true); }
    }
    [Fact]
    public async Task RecoveryRetainsLegacySaveEncodingAndUnrepresentableTranslation()
    {
        var root=Path.Combine(Path.GetTempPath(),"Yoake-encoding-recovery-"+Guid.NewGuid());
        try
        {
            var document=AssDocument.LoadBytes(SubtitleEncodings.Get(932).GetBytes(Source),SubtitleEncodings.Get(932));document.Events[0].Text="မြန်မာ 日本語";
            var store=new SubtitleRecoveryStore(root);var lease=new RecoveryLease();
            var path=await store.WriteAsync(document.CaptureSnapshot(),new() { DocumentId=Guid.NewGuid(),Sequence=1,Timestamp=DateTimeOffset.UtcNow,Title="legacy.ass" },lease,lease.Renew());
            var recovered=await store.ReadAsync(path);
            Assert.Equal(document.Serialize(),recovered.Document.Serialize());Assert.Equal(932,recovered.Document.EncodingCodePage);
            Assert.True((await store.ListAsync()).Single().CanRecover);
            recovered.Document.SaveUtf8(Path.Combine(root,"recovered.ass"));Assert.Equal(document.Serialize(),AssDocument.Load(Path.Combine(root,"recovered.ass")).Serialize());
        }
        finally { if(Directory.Exists(root))Directory.Delete(root,true); }
    }
    [Fact]
    public void ExplicitUnicodeHonorsBomAndRejectsMalformedInput()
    {
        var encoding=new UnicodeEncoding(false,true,true);var bytes=encoding.GetPreamble().Concat(encoding.GetBytes(Source)).ToArray();
        var document=AssDocument.LoadBytes(bytes,encoding);Assert.Equal(Source,document.Serialize());
        Assert.Throws<InvalidDataException>(()=>AssDocument.LoadBytes([0xff],new UTF8Encoding(false,true)));
        Assert.Contains(SubtitleEncodings.Choices(),c=>c.CodePage==932);
    }
}
