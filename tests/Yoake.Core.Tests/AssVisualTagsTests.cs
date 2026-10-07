using Yoake.Core.Subtitles;
namespace Yoake.Core.Tests;
public sealed class AssVisualTagsTests
{
    [Fact] public void PositionSplicesOnlyStaticTag()
    {
        const string text="{\\future(foo\\bar)\\pos(1,2)\\t(0,100,\\pos(3,4))\\distort(1,2,3,4)}日本語";
        Assert.Equal(text.Replace("\\pos(1,2)","\\pos(30,40)"),AssVisualTags.SetPosition(text,new(30,40)));
    }
    [Theory] [InlineData("clip")] [InlineData("iclip")]
    public void RectangularClipPreservesUnknownAndTransform(string kind)
    {
        var text="{\\"+kind+"(1,2,30,40)\\t(0,100,\\clip(5,6,7,8))\\unknown(x)}Text";
        Assert.Equal(text.Replace("(1,2,30,40)","(10,20,30,40)"),AssVisualTags.MoveClipPoint(text,0,new(10,20)));
    }
    [Theory] [InlineData("clip")] [InlineData("iclip")]
    public void VectorScaleIsIntegerAndDrawingSyntaxIsPreserved(string kind)
    {
        var text="{\\"+kind+"(3,m  0 0 l 400 0 b 400 400 0 400 0 0)\\future(x)}Text";
        var clip=AssVisualTags.Clip(text)!; Assert.Equal(3,clip.Scale); Assert.Equal(new AssPoint(100,0),clip.Points[1]);
        Assert.Equal(text.Replace("l 400 0","l 440 80"),AssVisualTags.MoveClipPoint(text,1,new(110,20)));
        Assert.Null(AssVisualTags.Clip("{\\clip(1.5,m 0 0 l 1 1)}"));
    }
    [Fact] public void MissingPositionAddedWithoutReserializingBlock() => Assert.Equal("{\\pos(5,6)\\unknown(foo)}Text",AssVisualTags.SetPosition("{\\unknown(foo)}Text",new(5,6)));
    [Fact] public void MovementIsNotSilentlyOverwritten() => Assert.Throws<InvalidOperationException>(()=>AssVisualTags.SetPosition("{\\move(1,2,3,4)}Text",new(5,6)));
    [Fact] public void ReplaceAllIsOneUndoOperation()
    {
        var e=new SubtitleEditor(AssDocument.CreateEmpty()); e.Insert(null,false); e.Document.Events[0].Text="日本語 abc ABC";
        var search=new SubtitleSearch(new("abc")); Assert.Equal(1,search.ReplaceAll(e,e.Document.Events,"x")); Assert.Equal("日本語 x x",e.Document.Events[0].Text); e.Undo.Undo(); Assert.Equal("日本語 abc ABC",e.Document.Events[0].Text);
    }

    [Fact] public void FirstStaticPositionMatchesMangetsuAuthority()
    {
        const string text="{\\pos(1,2)\\pos(3,4)\\future(x)}Text";
        Assert.Equal(text.Replace("pos(1,2)","pos(5,6)"),AssVisualTags.SetPosition(text,new(5,6)));Assert.Equal(new AssPoint(1,2),AssVisualTags.Position(text));
    }
    [Fact] public void RelativeMangetsuPositionIsNotSilentlyReinterpreted()=>Assert.Throws<InvalidOperationException>(()=>AssVisualTags.SetPosition("{\\pos(+10,+20)}Text",new(5,6)));
    [Fact] public void VectorTranslationPreservesScaleAndCommandSpacing()
    {
        const string text="{\\iclip(3,m  0 0 l 400 0)\\unknown(x)}Text";
        Assert.Equal("{\\iclip(3,m  40 80 l 440 80)\\unknown(x)}Text",AssVisualTags.TranslateClip(text,new(10,20)));
    }
}
