using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class RecombineTests
{
    private static AssEvent Add(SubtitleEditor editor,string text,long start,long end)
    {var line=editor.Insert(null,true);line.Text=text;line.Start=AssTime.Format(start);line.End=AssTime.Format(end);line.Actor="Actor 日本語";return line;}
    [Fact]
    public void RecombinesThreeLineFragmentsWithTimesUnicodeAndOneUndo()
    {
        var editor=new SubtitleEditor(AssDocument.CreateEmpty());var a=Add(editor,"日本語",1000,2000);var combined=Add(editor,@" 日本語\Nမြန်မာ ",2000,3000);var b=Add(editor,"မြန်မာ",3000,4000);var before=editor.Document.Serialize();
        var result=editor.Recombine([a,combined,b]);
        Assert.Equal(new[] { a,b },result);Assert.Equal(new[] { "日本語","မြန်မာ" },editor.Document.Events.Select(e=>e.Text));
        Assert.Equal(1000,a.StartMilliseconds);Assert.Equal(3000,a.EndMilliseconds);Assert.Equal(2000,b.StartMilliseconds);Assert.Equal(4000,b.EndMilliseconds);
        Assert.All(result,e=>Assert.Equal("Actor 日本語",e.Actor));var after=editor.Document.Serialize();editor.Undo.Undo();Assert.Equal(before,editor.Document.Serialize());editor.Undo.Redo();Assert.Equal(after,editor.Document.Serialize());
    }
    [Theory]
    [InlineData("one",@"one\Ntwo","two")]
    [InlineData("one",@"two\None","two")]
    [InlineData(@"one\Ntwo","two","one")]
    [InlineData(@"two\None","two","one")]
    public void PrefixSuffixPatternsRecombineWithoutAddingOrLosingVisibleFragments(string first,string second,string third)
    {
        var editor=new SubtitleEditor(AssDocument.CreateEmpty());var a=Add(editor,first,0,1000);var b=Add(editor,second,1000,2000);var c=Add(editor,third,2000,3000);
        var result=editor.Recombine([a,b,c]);Assert.Equal(2,result.Count);Assert.Equal(new[] { "one","two" },result.Select(e=>e.Text).Order());
    }
    [Fact]
    public void DuplicateLinesKeepTheSurvivorsMetadataAndExpandTiming()
    {
        var editor=new SubtitleEditor(AssDocument.CreateEmpty());var first=Add(editor,@"{\future(x)}same",0,1000);var second=Add(editor,first.Text,500,2000);second.Actor="Survivor";
        var result=editor.Recombine([first,second]);Assert.Same(second,Assert.Single(result));Assert.Equal("Survivor",second.Actor);Assert.Equal(0,second.StartMilliseconds);Assert.Equal(2000,second.EndMilliseconds);Assert.Equal(@"{\future(x)}same",second.Text);
    }
    [Fact]
    public void InvalidBoundaryForeignSelectionAndMetadataNeverGetPartiallyRecombined()
    {
        var editor=new SubtitleEditor(AssDocument.CreateEmpty());var a=Add(editor,"a",0,1000);var b=Add(editor,"áb",1000,2000);var before=editor.Document.Serialize();
        Assert.Throws<ArgumentException>(()=>editor.Recombine([a,b]));Assert.Equal(before,editor.Document.Serialize());
        var foreign=new SubtitleEditor(AssDocument.CreateEmpty()).Insert(null,true);Assert.Throws<InvalidOperationException>(()=>editor.Recombine([a,foreign]));Assert.Equal(before,editor.Document.Serialize());
        b.Text="a";editor.SetMangetsuMetadata(MangetsuMetadataKind.Actor,"Actor",@"{\c&HFFFFFF&}");var metadata=editor.Document.Events[0];var text=metadata.Text;
        var inherited=Add(editor,"{uketsugu}a",2000,3000);editor.Recombine(editor.Document.Events.ToArray());Assert.Contains(metadata,editor.Document.Events);Assert.Equal(text,metadata.Text);Assert.Contains(inherited,editor.Document.Events);
    }
}
