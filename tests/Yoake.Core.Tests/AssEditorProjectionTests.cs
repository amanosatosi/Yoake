using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class AssEditorProjectionTests
{
    [Theory]
    [InlineData(@"Hello\NWorld", "Hello\nWorld")]
    [InlineData(@"a\\Nb", @"a\\Nb")]
    [InlineData(@"{\future(\N)}a\Nb", "{\\future(\\N)}a\nb")]
    [InlineData(@"a\n\h\N日本語 é", "a\\n\\h\n日本語 é")]
    [InlineData(@"{\furi0}a\\Nb", "{\\furi0}a\\\nb")]
    public void ProjectionRoundTripsWithoutChangingOtherSyntax(string source, string display)
    {
        var projection = new AssEditorProjection(source);
        Assert.Equal(display, projection.Display);
        Assert.Equal(source, projection.Edit(display));
        for (var index = 0; index <= display.Length; index++)
            Assert.Equal(index, projection.ToDisplay(projection.ToSource(index)));
    }

    [Fact]
    public void SelectionAndFormattingUseSourceCoordinatesAfterBreak()
    {
        var projection = new AssEditorProjection(@"Hello\NWorld");
        Assert.Equal(7, projection.ToSource(6));
        Assert.Equal(12, projection.ToSource(11));
        var style = AssDocument.CreateEmpty().Styles[0];
        var edit = AssFormatting.Apply(projection.Source, projection.ToSource(6), projection.ToSource(11),
            new Dictionary<string, string> { ["1c"] = "&H0000FF&" }, style);
        Assert.StartsWith(@"Hello\N{\1c&H0000FF&}World", edit.Text);
    }

    [Fact]
    public void CutPasteAcrossBreaksPreservesLiteralEscapesAndUnknownTags()
    {
        var projection = new AssEditorProjection(@"{\future(x)}a\\Nb\NWorld\Nend");
        var display = projection.Display;
        var start = display.IndexOf("b\n", StringComparison.Ordinal);
        var end = display.IndexOf("end", StringComparison.Ordinal);
        var cut = projection.Edit(display[..start] + display[end..]);
        Assert.Equal(@"{\future(x)}a\\Nend", cut);
        Assert.Equal(@"{\future(x)}a\\Nb\NWorld\Nend", new AssEditorProjection(cut).Edit(display));
        Assert.Equal(@"x\Ny\Nz", new AssEditorProjection("x").Edit("x\r\ny\nz"));
    }
    [Theory]
    [InlineData("[yoake motion position]")]
    [InlineData("[toshiban motion track helper]")]
    public void BetterViewKeepsMotionSourceWhileEditingAroundItsCompactDisplay(string marker)
    {
        var helper="{"+marker+@"\t(0,100,\pos(~+1,~-2))}";
        var source="日本語"+helper+@"\Nမြန်မာ";var projection=new AssEditorProjection(source);
        Assert.Equal("日本語{motion tracking}\nမြန်မာ",projection.Display);
        Assert.Equal(source,projection.Edit(projection.Display));
        Assert.Equal("日本語!"+helper+@"\Nမြန်မာ",projection.Edit(projection.Display.Insert(3,"!")));
        Assert.Equal("日本語"+helper+@"\Nမြန်မာ!",projection.Edit(projection.Display+"!"));
        var begin=projection.Display.IndexOf("{motion tracking}",StringComparison.Ordinal);
        Assert.Equal(3,projection.ToSource(begin));Assert.Equal(3+helper.Length,projection.ToSource(begin+"{motion tracking}".Length));
        Assert.False(projection.TryEdit(projection.Display.Remove(begin+2,1),out var unchanged));Assert.Equal(source,unchanged);
        Assert.Equal("日本語"+@"\Nမြန်မာ",projection.Edit(projection.Display.Remove(begin,"{motion tracking}".Length)));
        Assert.Equal("日本語"+helper+"\nမြန်မာ",new AssEditorProjection(source,true,true).Display);
        Assert.Equal(source,new AssEditorProjection(source,false).Display);
    }
    [Fact] public void BetterViewOnlyCollapsesCompleteRecognizedBlocks()
    {
        const string source=@"x{[other motion]tag}y{[yoake motion position]unterminated";
        Assert.Equal(source,new AssEditorProjection(source).Display);
    }
}
