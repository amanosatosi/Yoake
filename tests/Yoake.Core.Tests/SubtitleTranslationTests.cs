using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class SubtitleTranslationTests
{
    [Fact]
    public void TranslationEditsOnlyTheSelectedPlainBlock()
    {
        var source=@"{\future(opaque)\bord2}日本語{comment}{\i1}二番\Ntext{\p1}m 0 0 l 2 2{\p0}終わり";
        var blocks=SubtitleTranslationText.Blocks(source);
        Assert.Equal(new[]{"日本語",@"二番\Ntext","終わり"},blocks.Select(b=>source.Substring(b.Start,b.Length)));
        Assert.Equal(@"{\future(opaque)\bord2}မြန်မာ\Nစာ{comment}{\i1}二番\Ntext{\p1}m 0 0 l 2 2{\p0}終わり",
            SubtitleTranslationText.Replace(source,blocks[0],"မြန်မာ\r\nစာ"));
    }
    [Fact]
    public void RubyBaseTranslationRetainsDelimitersReadingAndFormatting()
    {
        var source=@"<漢字|かんじ>{\future(x)}終";
        var blocks=SubtitleTranslationText.Blocks(source);
        Assert.Equal("漢字",source.Substring(blocks[0].Start,blocks[0].Length));
        Assert.Equal(@"<မြန်မာ|かんじ>{\future(x)}終",SubtitleTranslationText.Replace(source,blocks[0],"မြန်မာ"));
    }
    [Theory]
    [InlineData(@"{\p1}m 0 0\N l 2 2")]
    [InlineData(@"{comment}{\future(opaque)}")]
    [InlineData("   ")]
    public void NonTextPayloadIsNeverOfferedAsATranslationBlock(string source)=>Assert.Empty(SubtitleTranslationText.Blocks(source));
    [Fact]
    public void AStaleSpanCannotReplaceOverrides()=>Assert.Throws<ArgumentException>(()=>SubtitleTranslationText.Replace(@"{\i1}日本語",new(0,5),"text"));
}
