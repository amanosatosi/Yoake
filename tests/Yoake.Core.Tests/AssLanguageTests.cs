using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class AssLanguageTests
{
    private static AssStyle Default => AssDocument.CreateEmpty().Styles[0];
    [Theory]
    [InlineData("{\\bord",AssSyntaxKind.Tag,"\\bord")]
    [InlineData("{\\t(0,500,\\bord4\\blur2)}x",AssSyntaxKind.Tag,"\\blur")]
    [InlineData("{\\1c&HFFFFFF&}",AssSyntaxKind.Color,"&HFFFFFF&")]
    [InlineData("{\\alpha&H80&}",AssSyntaxKind.Color,"&H80&")]
    [InlineData("{\\c$shiro}",AssSyntaxKind.Color,"$shiro")]
    [InlineData("a\\Nb",AssSyntaxKind.Escape,"\\N")]
    [InlineData("a\\nb",AssSyntaxKind.Escape,"\\n")]
    [InlineData("a\\hb",AssSyntaxKind.Escape,"\\h")]
    [InlineData("{\\kf20}歌",AssSyntaxKind.Karaoke,"\\kf20")]
    [InlineData("{\\K30}歌",AssSyntaxKind.Karaoke,"\\K30")]
    [InlineData("{\\p1}m 0 0 l 20 30{\\p0}日本語",AssSyntaxKind.Drawing,"m ")]
    [InlineData("{\\clip(2,m 0 0 l 2 3)}",AssSyntaxKind.Drawing,"m")]
    [InlineData("{\\distort(0,0,1,1)}",AssSyntaxKind.MangetsuTag,"\\distort")]
    [InlineData("{\\furichangepos1\\grd($kuro,$shiro)}",AssSyntaxKind.MangetsuTag,"\\furichangepos")]
    [InlineData("{\\msgleft\\scrollt500}",AssSyntaxKind.MangetsuTag,"\\scrollt")]
    [InlineData("{\\vendor_xyz(1,\\blur3)}",AssSyntaxKind.Unknown,"\\vendor_xyz")]
    [InlineData("<仮|かり>",AssSyntaxKind.Furigana,"|かり>")]
    public void TokenRanges(string source,AssSyntaxKind kind,string expected)
    {
        var tokens=AssSyntax.Tokenize(source);Assert.Equal(source.Length,tokens.Sum(t=>t.Length));
        var offset=0;foreach(var token in tokens){Assert.Equal(offset,token.Start);offset+=token.Length;}
        Assert.Contains(tokens,t=>t.Kind==kind&&source.Substring(t.Start,t.Length)==expected);
    }
    [Theory]
    [InlineData("{\\t((0,1),\\blur")]
    [InlineData("{\\")]
    [InlineData("{\\clip(2,m 1 2")]
    [InlineData("日本語 မြန်မာ é 👩‍👩‍👧‍👦")]
    public void IncompleteAndUnicodeNeverLoseRanges(string text)=>Assert.Equal(text.Length,AssSyntax.Tokenize(text).Sum(t=>t.Length));
    [Theory]
    [InlineData("<ordinary>")]
    [InlineData("<a|b|c|d>")]
    [InlineData("<|reading>")]
    [InlineData("<base|>")]
    [InlineData("<a\\N|b>")]
    [InlineData("\\<仮|かり>")]
    [InlineData("{\\furi0}<仮|かり>")]
    public void OrdinaryAnglesAreNotFurigana(string text)=>Assert.DoesNotContain(AssSyntax.Tokenize(text),t=>t.Kind==AssSyntaxKind.Furigana);
    [Fact] public void FuriganaEscapedPipeAndOverridesAreStructural()
    {
        const string text="<仮\\|\\名{\\b1}|かり>";
        Assert.Contains(AssSyntax.Tokenize(text),t=>t.Kind==AssSyntaxKind.Furigana);
        Assert.Contains(AssSyntax.Tokenize(text),t=>t.Kind==AssSyntaxKind.Tag);
    }
    [Fact] public void TransformTagsDoNotBecomeStaticFormattingState()
    {
        const string text="{\\b1\\t(0,500,\\b0\\clip(1,2,3,4))}Hello";
        Assert.Equal(new[]{"b","t"},AssSyntax.Tags(text).Select(t=>t.Name));Assert.Equal("1",AssFormatting.State(text,text.Length,Default)["b"]);
    }
    [Fact] public void BoldSelectionRestoresInheritedState()
    {
        var edit=AssFormatting.Toggle("Hello world",6,11,"b",Default);Assert.Equal("Hello {\\b1}world{\\b0}",edit.Text);Assert.Equal("world",edit.Text[edit.SelectionStart..edit.SelectionEnd]);
        var style=Default;style.Set("Bold","-1");Assert.Equal("Hello {\\b0}world{\\b1}",AssFormatting.Toggle("Hello world",6,11,"b",style).Text);
    }
    [Fact] public void ExistingTagsAndEndStateArePreserved()
    {
        const string text="{\\b1\\future(opaque)\\t(0,1,\\blur5)}ab{\\b0}cd";
        var start=text.IndexOf("ab",StringComparison.Ordinal);var edit=AssFormatting.Apply(text,start,text.Length,new Dictionary<string,string>{{"b","1"}},Default);
        Assert.Contains("\\future(opaque)\\t(0,1,\\blur5)",edit.Text);Assert.EndsWith("{\\b0}",edit.Text);Assert.Contains("ab{\\b1}cd",edit.Text);
    }
    [Fact] public void GraphemesAndEscapesCannotBeSplit()
    {
        const string text="Aé👩‍👩‍👧‍👦Z";
        var edit=AssFormatting.Toggle(text,2,5,"i",Default);Assert.Contains("é👩‍👩‍👧‍👦",edit.Text);
        Assert.Equal(1,AssFormatting.Boundary("a\\Nb",2,false));Assert.Equal(3,AssFormatting.Boundary("a\\Nb",2,true));
    }
    [Fact] public void CaretAndNamedResetRespectCurrentStyle()
    {
        var style=Default;style.Set("Name","他");style.Set("Bold","-1");const string text="{\\r他}hello";
        Assert.EndsWith("{\\b0}",AssFormatting.Toggle(text,text.Length,text.Length,"b",Default,n=>n=="他"?style:null).Text);
    }
    [Fact] public void HeavyKaraokeLineUsesCachedRanges()
    {
        var text=string.Concat(Enumerable.Repeat("{\\kf20\\blur2\\1c&HFF0080&}歌",200));var syntax=new AssSyntaxDocument();var tokens=syntax.Update(text);
        Assert.Same(tokens,syntax.Update(text));Assert.Equal(text.Length,tokens.Sum(t=>t.Length));Assert.NotSame(tokens,syntax.Update(text+"x"));
    }
    [Fact] public void ColorSelectionKeepsOtherChannelsAndRestoresOriginalAlpha()
    {
        const string text="A{\alpha&H80&\future(opaque)}BC";
        var edit=AssFormatting.Apply(text,0,text.Length,new Dictionary<string,string>{{"1c","&H0000FF&"},{"1a","&H40&"}},Default);
        Assert.Contains("\alpha&H80&\1a&H40&\future(opaque)",edit.Text);
        var inside=AssFormatting.State(edit.Text,edit.Text.IndexOf("BC",StringComparison.Ordinal),Default);
        Assert.Equal("&H40&",inside["1a"]);Assert.Equal("&H80&",inside["2a"]);
        Assert.Equal("&H80&",AssFormatting.State(edit.Text,edit.Text.Length,Default)["1a"]);
    }
    [Fact] public void RelativeFontSizeRestoresAnAbsoluteValueAndEmptyOverrideResetsStyle()
    {
        var style=Default;style.Set("Fontsize","40");
        const string text="{\fs+10}ABC";
        Assert.Equal("50",AssFormatting.State(text,text.Length,style)["fs"]);
        var edit=AssFormatting.Apply(text,text.Length-3,text.Length,new Dictionary<string,string>{{"fs","70"}},style);
        Assert.EndsWith("{\fs50}",edit.Text);
        Assert.Equal("40",AssFormatting.State("{\fs+10\fs}A",100,style)["fs"]);
        Assert.Equal("0",AssFormatting.State("{\b1\b}A",100,style)["b"]);
    }

}
