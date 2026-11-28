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

    [Fact]
    public void FramePreviewPreservesMetadataBoundaryAndUsesDraftTiming()
    {
        var document=AssDocument.Parse("""
[Script Info]
ScriptType: v4.00+
[V4+ Styles]
Format: Name, Fontname, Fontsize
Style: Default,Arial,20
[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
Comment: 0,0:00:00.00,9:59:59.99,Default,A,0,0,0,mangetsu-colorcoding,{\c&HFFFFFF&}
Dialogue: 0,0:00:09.00,0:00:10.00,Default,A,0,0,0,,metadata boundary
Dialogue: 0,0:00:09.00,0:00:10.00,Default,A,0,0,0,,offscreen
Comment: 0,0:00:00.00,9:59:59.99,Default,B,0,0,0,mangetsu-colorcoding,late metadata
Dialogue: 0,0:00:01.00,0:00:02.00,Default,A,0,0,0,,visible
Dialogue: 0,0:00:00.00,0:00:01.00,Default,A,0,0,0,,ended
Dialogue: 0,0:00:09.00,0:00:10.00,Default,A,0,0,0,{uketsugu},effect retained
Dialogue: 0,0:00:09.00,0:00:10.00,Default,A,0,0,0,,draft
[Unknown]
Keep: yes
""");
        var source=document.Serialize();
        var draft=document.Events.Last();
        var preview=AssDocument.Parse(document.SerializeFramePreview(draft,new Dictionary<string,string>
            {["Start"]="0:00:00.00",["End"]="0:00:02.00",["Text"]="edited မြန်မာ"},1000));
        Assert.Equal(new[]{"{\\c&HFFFFFF&}","metadata boundary","late metadata","visible","effect retained","edited မြန်မာ"},preview.Events.Select(e=>e.Text));
        Assert.Single(MangetsuMetadata.Initial(preview));
        Assert.Contains("Keep: yes",preview.Serialize());
        Assert.Equal(source,document.Serialize());
        Assert.Equal("draft",draft.Text);
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
