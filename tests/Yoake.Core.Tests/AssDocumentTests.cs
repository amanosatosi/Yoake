using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class AssDocumentTests
{
    [Fact]
    public void ParsesDialogueAndPreservesCommasInText()
    {
        var document = AssDocument.Parse("""
[Script Info]
ScriptType: v4.00+

[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
Dialogue: 0,0:00:01.20,0:00:03.40,Default,Miku,0,0,0,,Hello, world, again
""");

        var line = Assert.Single(document.Events);
        Assert.Equal("Miku", line.Actor);
        Assert.Equal("Hello, world, again", line.Text);
        Assert.Equal(1200, line.StartMilliseconds);
        Assert.Equal(3400, line.EndMilliseconds);
    }

    [Fact]
    public void EditingEventRoundTripsUnknownFileContent()
    {
        var document = AssDocument.Parse("""
[Script Info]
Title: Keep Me

[Aegisub Project Garbage]
Video File: ?dummy:23.976000:1920:1080:0:0:0:

[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
Dialogue: 0,0:00:01.00,0:00:02.00,Default,OldActor,0,0,0,,Old text
""");

        var line = Assert.Single(document.Events);
        line.Actor = "NewActor";
        line.Text = "New, text";
        line.Start = "0:00:01.50";

        var serialized = document.Serialize();
        Assert.Contains("Title: Keep Me", serialized);
        Assert.Contains("Video File: ?dummy:23.976000:1920:1080:0:0:0:", serialized);
        Assert.Contains("Dialogue: 0,0:00:01.50,0:00:02.00,Default,NewActor,0,0,0,,New, text", serialized);
    }

    [Theory]
    [InlineData("0:00:00.00", 0)]
    [InlineData("0:01:02.34", 62_340)]
    [InlineData("1:02:03.456", 3_723_456)]
    public void ParsesAssTimes(string text, long expectedMilliseconds)
    {
        Assert.True(AssTime.TryParse(text, out var actual));
        Assert.Equal(expectedMilliseconds, actual);
    }
}
