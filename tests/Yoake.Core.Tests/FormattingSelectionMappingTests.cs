using Xunit;
using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class FormattingSelectionMappingTests
{
    [Fact] public void VisibleSelectionIgnoresDifferentOverrideLengthsAndPreservesTargetGraphemes()
    {
        const string source=@"{\future(opaque)}日本語";
        var selected=AssFormatting.VisibleSelection(source,source.Length-3,source.Length);
        Assert.Equal((0,3),selected);
        const string target="{\\b1\\future(x)}é!";
        var mapped=AssFormatting.SourceSelection(target,selected.Start,selected.End);
        Assert.Equal("é!",target[mapped.Start..mapped.End]);
        Assert.Equal("{\\b1\\future(x)}",target[..mapped.Start]);
        var cluster=AssFormatting.SourceSelection("မြန်မာ",0,1);
        Assert.True(cluster.End>1);Assert.Equal(0,cluster.Start);
    }
    [Fact] public void RangeDoesNotConsumeAnOverrideFollowingItsLastVisibleCharacter()
    {
        const string target=@"{\future(x)}abc{\b1}def";
        var range=AssFormatting.SourceSelection(target,0,3);
        Assert.Equal("abc",target[range.Start..range.End]);Assert.Equal(@"{\b1}def",target[range.End..]);
        var caret=AssFormatting.SourceSelection(target,3,3);
        Assert.Equal("def",target[caret.Start..]);Assert.Equal(caret.Start,caret.End);
    }
    [Fact] public void ShortLinesClampAndAssEscapesRemainWhole()
    {
        Assert.Equal((1,1),AssFormatting.SourceSelection("x",5,9));
        var newline=AssFormatting.SourceSelection(@"{\i1}\Ntext",0,1);
        Assert.Equal(@"\N",@"{\i1}\Ntext"[newline.Start..newline.End]);
    }
}
