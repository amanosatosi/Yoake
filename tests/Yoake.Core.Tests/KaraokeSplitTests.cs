using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class KaraokeSplitTests
{
    [Fact]
    public void SplitsSelectedKaraokeWithTimingMetadataAndSingleUndo()
    {
        var document=AssDocument.CreateEmpty();var editor=new SubtitleEditor(document);
        var line=editor.Insert(null,false);line.Start="0:00:01.00";line.End="0:00:03.00";
        line.Text=@"{\k30}日本{\kf70}မြန်မာ";line.Style="Default";line.Actor="歌";
        var untouched=editor.Insert(line,true);untouched.Text="unchanged";
        editor.MarkSaved();var before=document.Serialize();
        var result=editor.SplitByKaraoke([line,untouched]);
        Assert.Equal(2,result.Count);Assert.Equal(new[]{"0:00:01.00","0:00:01.30"},result.Select(x=>x.Start));
        Assert.Equal(new[]{"0:00:01.30","0:00:02.00"},result.Select(x=>x.End));
        Assert.Contains(@"\k30",result[0].Text);Assert.Contains(@"\kf70",result[1].Text);
        Assert.Contains("日本",result[0].Text);Assert.Contains("မြန်မာ",result[1].Text);
        Assert.All(result,x=>Assert.Equal("歌",x.Actor));Assert.Same(untouched,document.Events.Last());
        var after=document.Serialize();editor.Undo.Undo();Assert.Equal(before,document.Serialize());
        editor.Undo.Redo();Assert.Equal(after,document.Serialize());
    }

    [Fact]
    public void InvalidKaraokeInSelectionDoesNotPartiallyChangeDocument()
    {
        var document=AssDocument.CreateEmpty();var editor=new SubtitleEditor(document);
        var first=editor.Insert(null,false);first.Text=@"{\k10}a{\k20}b";
        var second=editor.Insert(first,true);second.Text=@"{\k10}x{\kt20}y";
        var before=document.Serialize();Assert.Throws<ArgumentException>(()=>editor.SplitByKaraoke([first,second]));
        Assert.Equal(before,document.Serialize());Assert.Same(first,document.Events[0]);
    }
    [Theory]
    [InlineData(@"{\k10}a{\k20}́")]
    [InlineData(@"{\k10\future(opaque)}a{\k20}b")]
    [InlineData(@"{\an8\k10}a{\k20}b")]
    [InlineData(@"{\k10}<漢字|かんじ>{\k20}語")]
    public void UnsafeTextBoundariesLeaveSourceUntouched(string text)
    {
        var document=AssDocument.CreateEmpty();var editor=new SubtitleEditor(document);
        var line=editor.Insert(null,false);line.Text=text;var before=document.Serialize();
        Assert.Throws<ArgumentException>(()=>editor.SplitByKaraoke([line]));
        Assert.Equal(before,document.Serialize());
    }

}
