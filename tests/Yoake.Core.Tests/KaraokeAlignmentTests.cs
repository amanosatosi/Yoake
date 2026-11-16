using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class KaraokeAlignmentTests
{
    [Fact]
    public void ManyToManyKanjiMatchesSumTimingAndPreserveDestinationOverridesAndUnicode()
    {
        var match=new KaraokeAlignment(@"{\k10}to{\kf20}u{\ko30}kyo{\k40}u",@"{\future(opaque)\bord2\k99}東京{\i1} မြန်မာ é");
        match.Link(2,1);match.Link(2,match.DestinationRemaining);
        Assert.True(match.Complete);Assert.Equal(new long[] { 300,700 },match.Groups.Select(g=>g.Duration));
        var output=match.Render();Assert.Equal(@"{\k30}{\future(opaque)\bord2}東{\k70}京{\i1} မြန်မာ é",output);
        Assert.DoesNotContain("99",output);Assert.Equal(1000,KaraokeSource.Parse(output).Durations.Sum());
        match.Unlink();Assert.False(match.Complete);Assert.Equal(2,match.SourceRemaining);Assert.Throws<InvalidOperationException>(()=>match.Render());
    }
    [Fact]
    public void GraphemeClustersEscapesAndEmptyTimingGroupsRemainWhole()
    {
        var match=new KaraokeAlignment(@"{\k10}{\k20}a{\k30}b",@"é\N👩‍👩‍👧‍👦မြန်မာ");
        Assert.Equal("é",match.RemainingDestination[0]);Assert.Equal(@"\N",match.RemainingDestination[1]);Assert.Equal("👩‍👩‍👧‍👦",match.RemainingDestination[2]);
        match.Link(1,0);match.Link(0,1);match.Link(2,match.DestinationRemaining);
        Assert.Equal(600,KaraokeSource.Parse(match.Render()).Durations.Sum());Assert.StartsWith(@"{\k10}{\k0}é{\k50}\N",match.Render());
    }
    [Theory]
    [InlineData("<漢|かん>")]
    [InlineData(@"{\p1}m 0 0 l 1 1")]
    public void RubyAndDrawingDestinationsFailWithoutAnOutput(string target)
        =>Assert.Throws<NotSupportedException>(()=>new KaraokeAlignment(@"{\k10}a",target));
    [Fact]
    public void AbsoluteTimingIsNotSilentlyConverted()
        =>Assert.Throws<ArgumentException>(()=>new KaraokeAlignment(@"{\kt10\k20}a","漢"));
    [Fact]
    public void InvalidGroupNeverConsumesInputOrRewritesAnUnlinkedLine()
    {
        var match=new KaraokeAlignment(@"{\k10}a","漢");Assert.Throws<ArgumentException>(()=>match.Link(2,1));Assert.Throws<ArgumentException>(()=>match.Link(0,0));
        Assert.Empty(match.Groups);Assert.Equal(1,match.SourceRemaining);Assert.Equal(1,match.DestinationRemaining);Assert.Throws<InvalidOperationException>(()=>match.Render());
        match.Link(1,1);Assert.Equal(@"{\k10}漢",match.Render());
    }
}
