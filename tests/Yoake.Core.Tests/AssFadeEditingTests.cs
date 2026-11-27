using Xunit;
using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;
public sealed class AssFadeEditingTests
{
    [Fact] public void ColoredFadePreservesOtherSideUnknownTagsAndTransforms()
    {
        const string text=@"{\future(x)\fad(100,200,&H112233&+a,&H445566&)\t(0,500,\fad(1,2))}မြန်မာ";
        var result=AssFadeEditing.Set(text,true,300,0,1000,"&HFFFFFF&");
        Assert.Equal(@"{\future(x)\fad(300,200,&HFFFFFF&+a,&H445566&)\t(0,500,\fad(1,2))}မြန်မာ",result.Text);
        Assert.Equal(result.Text.Length,result.Map(text.Length));
        Assert.Equal(text,AssFadeEditing.Set(text,true,-1,0,1000).Text);
    }
    [Fact] public void LaterShortFadeMovesBeforeLongFadeWithoutDuplicate()
    {
        const string text=@"{\fade(1,2,3,4,5,6,7)\bord2}a{\fad(10,20)}b";
        var result=AssFadeEditing.Set(text,false,800,0,1000);
        Assert.Equal(@"{\fad(10,200)\fade(1,2,3,4,5,6,7)\bord2}ab",result.Text);
        Assert.Equal(result.Text.Length,result.Map(text.Length));
    }
    [Fact] public void NewFadeInsertsAtBeginningAndNormalFadeKeepsOtherColor()
    {
        Assert.Equal(@"{\fad(200,0)}日本語",AssFadeEditing.Set("日本語",true,200,0,1000).Text);
        Assert.Equal(@"{\fad(100,200,&HFFFFFF&,)}x",AssFadeEditing.Set(@"{\fad(100,20,&HFFFFFF&,&H000000&)}x",false,800,0,1000).Text);
    }
}
