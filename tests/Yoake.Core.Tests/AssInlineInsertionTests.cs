using Xunit;
using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class AssInlineInsertionTests
{
    public static IEnumerable<object[]> Pairs=>AssInlineInsertion.Brackets.Select(p=>new object[]{p.Id,p.Left,p.Right});
    [Theory,MemberData(nameof(Pairs))]
    public void AllToshiBracketPairsWrapSelectionAndKeepItSelected(string id,string left,string right)
    {
        const string source="日本語 မြန်မာ";
        var edit=AssInlineInsertion.Bracket(source,3,0,id);
        Assert.Equal(left+"日本語"+right+" မြန်မာ",edit.Text);
        Assert.Equal("日本語",edit.Text[edit.SelectionStart..edit.SelectionEnd]);
        var caret=AssInlineInsertion.Bracket(source,3,3,id);
        Assert.Equal("日本語"+left+right+" မြန်မာ",caret.Text);Assert.Equal(3+left.Length,caret.SelectionStart);Assert.Equal(caret.SelectionStart,caret.SelectionEnd);
    }
    [Fact] public void BracketRangesDoNotSplitUnicodeClustersOrAssEscapes()
    {
        var combining=AssInlineInsertion.Bracket("é!",1,2,"corner");Assert.Equal("「é」!",combining.Text);
        const string family="👩‍👩‍👧‍👦";Assert.Equal("「"+family+"」!",AssInlineInsertion.Bracket(family+"!",1,2,"corner").Text);
        Assert.Equal(@"\N「」\N",AssInlineInsertion.Bracket(@"\N\N",2,2,"corner").Text);
        Assert.Equal(@"\N「」x",AssInlineInsertion.Bracket(@"\Nx",1,1,"corner").Text);
        Assert.Equal(@"\|\「」x",AssInlineInsertion.Bracket(@"\|\x",2,2,"corner").Text);
    }
    [Theory]
    [InlineData("{comment}text",3,"{comment}「」text")]
    [InlineData(@"{\future(x)}text",4,@"{\future(x)}「」text")]
    public void CaretInsideAClosedAssBlockMovesOutsideWithoutChangingTheBlock(string source,int caret,string expected)
        =>Assert.Equal(expected,AssInlineInsertion.Bracket(source,caret,caret,"corner").Text);
    [Fact] public void IncompleteBlockInsertionRejectsWithoutRepairingSource()
    {
        Assert.Throws<ArgumentException>(()=>AssInlineInsertion.Bracket(@"{\future(x",4,4,"corner"));
        Assert.Throws<ArgumentException>(()=>AssInlineInsertion.Bracket("{comment",8,8,"corner"));
    }
    [Theory]
    [InlineData("にほんご","","<日本語|にほんご>")]
    [InlineData("","にほんご","<日本語||にほんご>")]
    [InlineData("にほんご","Japanese","<日本語|にほんご|Japanese>")]
    public void FuriganaUsesActualMangetsuSingleAndDoubleReadingSyntax(string reading,string opposite,string expected)
    {
        const string source=@"{\k30\future(opaque)}日本語 é မြန်မာ";
        var start=source.IndexOf("日本語",StringComparison.Ordinal);
        var edit=AssInlineInsertion.Furigana(source,start,start+3,new(reading,opposite));
        Assert.Equal(source[..start]+expected+" é မြန်မာ",edit.Text);
        Assert.Equal("日本語",edit.Text[edit.SelectionStart..edit.SelectionEnd]);
        Assert.Contains(AssSyntax.Tokenize(edit.Text),t=>t.Kind==AssSyntaxKind.Furigana);
    }
    [Fact] public void LiteralReadingDelimitersAndOverrideLookingTextCannotExecuteTags()
    {
        var edit=AssInlineInsertion.Furigana("ABC|DEF",0,7,new(@"x|<{\k30}>\", "မြန်မာ"));
        Assert.StartsWith(@"<ABC\|\DEF|",edit.Text);Assert.Contains(@"\|\",edit.Text);
        Assert.DoesNotContain(AssSyntax.Tokenize(edit.Text),t=>t.Kind is AssSyntaxKind.Karaoke or AssSyntaxKind.Tag);
        Assert.Contains(AssSyntax.Tokenize(edit.Text),t=>t.Kind==AssSyntaxKind.Furigana);
    }
    [Theory]
    [InlineData(@"{\furi0}日本語",8,11)]
    [InlineData(@"{\vert1}日本語",8,11)]
    [InlineData(@"{\p1}m 1 2",5,10)]
    [InlineData("<漢|かん>",1,2)]
    [InlineData(@"日{\b1}本",0,8)]
    [InlineData(@"日\h\N本",0,7)]
    public void UnsafeOrDisabledRubyAuthoringRejectsAtomically(string source,int start,int end)
        =>Assert.Throws<ArgumentException>(()=>AssInlineInsertion.Furigana(source,start,end,new("reading")));
    [Fact] public void EmptyReadingAndMultilineReadingAreRejected()
    {
        Assert.Throws<ArgumentException>(()=>AssInlineInsertion.Furigana("漢",0,1,new("")));
        Assert.Throws<ArgumentException>(()=>AssInlineInsertion.Furigana("漢",0,1,new("a\nb")));
        Assert.Throws<ArgumentException>(()=>AssInlineInsertion.Furigana("漢",0,0,new("reading")));
    }
}
