using Xunit;
using Yoake.Core.Media;
using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class SubtitleMediaLinksTests
{
    [Fact] public void AegisubViewMetadataRoundTripsWithoutChangingUnknownFields()
    {
        var document=AssDocument.Parse("[Aegisub Project Garbage]\nActive Line: 135\nScroll Position: 120\nVideo Position: 15382\nVideo Zoom Percent: 0.375000\nVideo AR Mode: 4\nVideo AR Value: 1.777778\nUnknown: keep\n");
        var view=SubtitleProjectView.Read(document);
        Assert.Equal(135,view.ActiveLine);Assert.Equal(120,view.ScrollPosition);Assert.Equal(15382,view.VideoPosition);
        Assert.Equal(.375,view.VideoZoom);Assert.Equal(new VideoAspect(VideoAspectKind.Custom,1.777778),view.Aspect);
        var editor=new SubtitleEditor(document);
        editor.SetProjectProperties(SubtitleProjectView.Capture(7,3,42,.5,new(VideoAspectKind.Wide),16d/9));
        var saved=SubtitleProjectView.Read(document);
        Assert.Equal(7,saved.ActiveLine);Assert.Equal(3,saved.ScrollPosition);Assert.Equal(42,saved.VideoPosition);
        Assert.Equal(.5,saved.VideoZoom);Assert.Equal(VideoAspectKind.Wide,saved.Aspect!.Kind);
        Assert.Equal("keep",document.GetSectionValue(SubtitleMediaLinks.Section,"Unknown"));
    }

    [Fact] public void InvalidProjectViewValuesAreIgnoredIndependently()
    {
        var view=SubtitleProjectView.Read(AssDocument.Parse("[Aegisub Project Garbage]\nActive Line: -1\nScroll Position: 3\nVideo Position: bad\nVideo Zoom Percent: NaN\nVideo AR Mode: 4\nVideo AR Value: Infinity\n"));
        Assert.Null(view.ActiveLine);Assert.Equal(3,view.ScrollPosition);Assert.Null(view.VideoPosition);Assert.Null(view.VideoZoom);Assert.Null(view.Aspect);
    }

    [Fact] public void DuplicateProjectSectionsRetainFirstValuesAndResolveTimingLinks()
    {
        var path=Path.Combine(Path.GetTempPath(),"episode.ass");
        var document=AssDocument.Parse("[Aegisub Project Garbage]\nVideo File: first.mkv\nTimecodes File: ?script/times.txt\n[Events]\n[Aegisub Project Garbage]\nVideo File: second.mkv\nKeyframes File: cuts.txt\nAudio File: \"?VIDEO\"\nYoake Audio Stream: 1\n");
        var links=SubtitleMediaLinks.Read(document,path);
        Assert.Equal(Path.Combine(Path.GetTempPath(),"first.mkv"),links.Video);Assert.Equal(links.Video,links.Audio);
        Assert.Equal(Path.Combine(Path.GetTempPath(),"times.txt"),links.Timecodes);Assert.Equal(Path.Combine(Path.GetTempPath(),"cuts.txt"),links.Keyframes);
        Assert.Equal(1,links.AudioStream);
    }

    [Fact] public void RelativeUnicodePathsVideoAliasAndStreamAreReadWithoutChangingSource()
    {
        var path=Path.Combine(Path.GetTempPath(),"日本語 မြန်မာ","episode.ass");
        var document=AssDocument.Parse("[Aegisub Project Garbage]\nVideo File: \"?script/media/日本語.mkv\"\nAudio File: ?video\nYoake Audio Stream: 4\nUnknown: keep\n[Events]\n");
        var before=document.Serialize(); var links=SubtitleMediaLinks.Read(document,path);
        Assert.Equal(Path.Combine(Path.GetDirectoryName(path)!,"media","日本語.mkv"),links.Video);
        Assert.Equal(links.Video,links.Audio); Assert.True(links.VideoSpecified); Assert.True(links.AudioSpecified); Assert.Equal(4,links.AudioStream);
        Assert.Equal(before,document.Serialize());
    }
    [Theory]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("1.2")]
    [InlineData("2147483648")]
    [InlineData("NaN")]
    public void InvalidStreamIndicesDoNotPreventReadingLegacyLinks(string stream)
    {
        var document=AssDocument.Parse("[Aegisub Project Garbage]\nVideo File: episode.mkv\nYoake Audio Stream: "+stream+"\n");
        var links=SubtitleMediaLinks.Read(document,Path.Combine(Path.GetTempPath(),"episode.ass"));
        Assert.NotNull(links.Video); Assert.Null(links.AudioStream); Assert.False(links.AudioSpecified);
    }
    [Fact] public void ExplicitClosureDiffersFromMissingAssociationAndDummyCannotSupplyAudio()
    {
        var path=Path.Combine(Path.GetTempPath(),"episode.ass");
        var missing=SubtitleMediaLinks.Read(AssDocument.Parse("[Script Info]\n"),path);
        Assert.False(missing.VideoSpecified); Assert.False(missing.AudioSpecified);
        var closed=SubtitleMediaLinks.Read(AssDocument.Parse("[Aegisub Project Garbage]\nVideo File:\nAudio File:\n"),path);
        Assert.True(closed.VideoSpecified); Assert.True(closed.AudioSpecified); Assert.Null(closed.Video); Assert.Null(closed.Audio);
        var dummy=SubtitleMediaLinks.Read(AssDocument.Parse("[Aegisub Project Garbage]\nVideo File: ?dummy:23.976000:12:320:180:47:163:254:c\nAudio File: ?video\n"),path);
        Assert.StartsWith("?dummy:",dummy.Video); Assert.Null(dummy.Audio);
    }
    [Fact] public void SavedProviderLinksUseExistingProjectPropertyUndoAndPreserveRendererEvents()
    {
        var document=AssDocument.Parse("[Aegisub Project Garbage]\nUnknown: untouched\n[Events]\nFormat: Layer,Start,End,Style,Name,MarginL,MarginR,MarginV,Effect,Text\nComment: 0,0:00:00.00,0:00:00.00,Default,,0,0,0,mangetsu-style,{\\vert1}\nDialogue: 0,0:00:00.10,0:00:01.00,Default,A,1,2,3,{uketsugu},日本語\n");
        var before=document.Serialize(); var editor=new SubtitleEditor(document); var events=before[before.IndexOf("[Events]",StringComparison.Ordinal)..];
        var video=Path.Combine(Path.GetTempPath(),"video.mkv");var audio=Path.Combine(Path.GetTempPath(),"audio.mka");
        editor.SetProjectProperties(SubtitleMediaLinks.Capture(video,audio,4));
        var after=document.Serialize();var links=SubtitleMediaLinks.Read(document,Path.Combine(Path.GetTempPath(),"project.ass"));
        Assert.Equal(video,links.Video); Assert.Equal(audio,links.Audio); Assert.Equal(4,links.AudioStream);
        Assert.Equal(events,after[after.IndexOf("[Events]",StringComparison.Ordinal)..]); Assert.Equal("untouched",document.GetSectionValue(SubtitleMediaLinks.Section,"Unknown"));
        editor.Undo.Undo();Assert.Equal(before,document.Serialize());editor.Undo.Redo();Assert.Equal(after,document.Serialize());
    }
}
