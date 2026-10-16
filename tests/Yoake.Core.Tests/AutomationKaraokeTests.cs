using Yoake.Core.Automation;

namespace Yoake.Core.Tests;

public sealed class AutomationKaraokeTests
{
    private static IReadOnlyDictionary<string, object?> Parse(string text) =>
        AutomationKaraokeParser.Parse(new AutomationLine { ["class"] = "dialogue", ["text"] = text });
    private static IReadOnlyDictionary<string, object?> At(IReadOnlyDictionary<string, object?> result, int i) =>
        Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(result[i.ToString()]);

    [Fact]
    public void MillisecondsAreRelativeAndNotNormalizedToLineEnd()
    {
        var result = Parse("{\\k20}日本{\\K35}語{\\ko10} မြန်မာ");
        Assert.Equal(4, result.Count);
        Assert.Equal("", At(result, 0)["tag"]);
        Assert.Equal(200, At(result, 1)["duration"]);
        Assert.Equal(200, At(result, 2)["start_time"]);
        Assert.Equal(550, At(result, 2)["end_time"]);
        Assert.Equal("\\kf", At(result, 2)["tag"]);
        Assert.Equal(" မြန်မာ", At(result, 3)["text_stripped"]);
    }

    [Fact]
    public void CommentsDrawingsTransformsAndUnknownTagsSurvive()
    {
        var result = Parse("{comment}{\\bord2\\k10\\t(0,100,\\k999)\\future(x)}a{\\p1}m 0 0 l 1 1{\\p0}{\\k20}b");
        Assert.Equal(3, result.Count);
        Assert.Equal("a", At(result, 1)["text_stripped"]);
        Assert.Equal("{comment}{\\bord2}{\\t(0,100,\\k999)\\future(x)}a{\\p1}m 0 0 l 1 1{\\p0}", At(result, 1)["text"]);
        Assert.Equal(100, At(result, 1)["duration"]);
        Assert.Equal("b", At(result, 2)["text"]);
    }

    [Fact]
    public void PrefixTextZeroDurationAndOldKtQuirkMatch322()
    {
        var result = Parse("prefix{\\k0\\i1\\k10}a{\\kt500}b{\\k(-2)}c");
        Assert.Equal(5, result.Count);
        Assert.Equal("prefix", At(result, 1)["text_stripped"]);
        Assert.Equal("{\\i1}a", At(result, 2)["text"]);
        Assert.Equal(0, At(result, 3)["duration"]);
        Assert.Equal(-20, At(result, 4)["duration"]);
        Assert.Equal(80, At(result, 4)["end_time"]);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("{\\k20unclosed", "{\\k20unclosed")]
    [InlineData("a\\Nb\\h🙂é", "a\\Nb\\h🙂é")]
    public void PlainTextAndMalformedBracesRemainLiteral(string text, string expected)
    {
        var result = Parse(text);
        Assert.Equal(2, result.Count);
        Assert.Equal(expected, At(result, 1)["text_stripped"]);
        Assert.Equal(0, At(result, 1)["duration"]);
    }

    [Fact]
    public void NonDialogueIsRejected() => Assert.Throws<ArgumentException>(() =>
        AutomationKaraokeParser.Parse(new AutomationLine { ["class"] = "style", ["text"] = "" }));
}
