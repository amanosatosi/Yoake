using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class AuthoringPolishTests
{
    [Fact] public void ToolbarReadsWholeOverrideAtCaretAndKeepsExactAlpha()
    {
        var style=AssDocument.CreateEmpty().Styles[0];
        const string text=@"Hello {\b1\1c&H402010&\1a&H80&\future(opaque)}world";
        var state=AssFormattingState.Read(text,10,10,style);
        Assert.True(state.Bold);Assert.False(state.Italic);
        Assert.Equal(new AssColor(16,32,64,128),state.Primary);
        var edit=AssFormatting.Toggle(text,10,10,"b",style);
        Assert.Equal(@"Hello {\b1\1c&H402010&\1a&H80&\future(opaque)}{\b0}world",edit.Text);
    }

    [Fact] public void MixedAndAnimatedSelectionsDoNotPretendToHaveAnActiveFlag()
    {
        var style=AssDocument.CreateEmpty().Styles[0];
        const string mixed=@"ab{\b1}cd";
        Assert.Null(AssFormattingState.Read(mixed,0,mixed.Length,style).Bold);
        Assert.False(AssFormattingState.Read(mixed,0,mixed.Length,style).Italic);
        const string animated=@"{\b1\t(0,500,\b0)}Hello";
        Assert.Null(AssFormattingState.Read(animated,animated.Length,animated.Length,style).Bold);
        Assert.Null(AssFormattingState.Read(@"{\b700}Hello",12,12,style).Bold);
    }

    [Fact] public void RendererColorExpressionsArePreservedWithoutInventingBlackSwatches()
    {
        var style=AssDocument.CreateEmpty().Styles[0];const string source=@"{\1c$shiro\future(opaque)}Text";
        var state=AssFormattingState.Read(source,source.Length,source.Length,style);
        Assert.Null(state.Primary);Assert.NotNull(state.Outline);
        Assert.Equal("$shiro",AssFormatting.State(source,source.Length,style)["1c"]);
    }

    [Theory]
    [InlineData(@"{\1grd(0,&HFF0000&,&H0000FF&)}テスト",@"\1grd")]
    [InlineData(@"{\5gra(0,&H80&,&HFF&)}Text",@"\5gra")]
    [InlineData(@"{\1pgrd(0,1,2)}Text",@"\1pgrd")]
    public void NumberedMangetsuGradientsHaveTheirOwnExactRanges(string source,string tag)
    {
        var token=Assert.Single(AssSyntax.Tokenize(source),t=>t.Kind==AssSyntaxKind.MangetsuTag);
        Assert.Equal(tag,source.Substring(token.Start,token.Length));
        Assert.Equal(source.Length,AssSyntax.Tokenize(source).Sum(t=>t.Length));
    }

    [Fact] public void StyleDraftSwitchCommitsOneLogicalEditAndKeepsUnknownValues()
    {
        var doc=AssDocument.Parse("[V4+ Styles]\nFormat: Name,Fontname,Fontsize,BorderStyle,PrimaryColour,Future\nStyle: 日本,Missing 字体,60,17,&H80402010,opaque\nStyle: Other,Arial,40,1,&H00FFFFFF,future\n");
        var editor=new SubtitleEditor(doc);var session=new StyleEditSession();session.Select(editor,doc.Styles[0]);
        session.Draft!.SetNumber("Fontsize",7);session.Draft.SetNumber("Fontsize",70);
        session.Select(editor,doc.Styles[1]);
        Assert.Equal("70",doc.Styles[0].Get("Fontsize"));Assert.Equal("17",doc.Styles[0].Get("BorderStyle"));
        Assert.Equal("&H80402010",doc.Styles[0].Get("PrimaryColour"));Assert.Equal("Missing 字体",doc.Styles[0].Get("Fontname"));
        Assert.Equal("opaque",doc.Styles[0].Get("Future"));editor.Undo.Undo();Assert.Equal("60",doc.Styles[0].Get("Fontsize"));
        Assert.False(editor.Undo.CanUndo);editor.Undo.Redo();Assert.Equal(doc.Serialize(),AssDocument.Parse(doc.Serialize()).Serialize());
    }

    [Fact] public void InvalidDraftCannotSwitchOrLoseItsOriginalTarget()
    {
        var editor=new SubtitleEditor(AssDocument.CreateEmpty());var first=editor.Document.Styles[0];var second=editor.AddStyle();
        var session=new StyleEditSession();session.Select(editor,first);session.Draft!.Set("Name",second.Name);
        Assert.Throws<ArgumentException>(()=>session.Select(editor,second));Assert.Same(first,session.Style);
        Assert.Equal(second.Name,session.Draft.Get("Name"));Assert.Equal("Default",first.Name);
    }

    [Fact] public void ScriptLibraryAndCollectionSwitchesCommitToTheirOwnUndoHistories()
    {
        var root=Path.Combine(Path.GetTempPath(),"Yoake-polish-"+Guid.NewGuid());var path=Path.Combine(root,"library.json");
        try
        {
            var store=new StyleLibraryStore(path);var one=store.Create("日本");var two=store.Create("မြန်မာ");
            var script=new SubtitleEditor(AssDocument.CreateEmpty());var preset=StyleLibraryStore.Copy(script.Document.Styles[0],store.Editor(one));
            var other=StyleLibraryStore.Copy(preset,store.Editor(two));var session=new StyleEditSession();session.Select(script,script.Document.Styles[0]);
            session.Draft!.SetNumber("Fontsize",70);session.Select(store.Editor(one),preset);
            session.Draft!.Set("Fontname","Missing 日本 字体");session.Select(store.Editor(two),other);store.Save();
            session.Draft!.SetNumber("Outline",4);session.Select(script,script.Document.Styles[0]);store.Save();
            Assert.Equal("70",script.Document.Styles[0].Get("Fontsize"));Assert.Equal("Missing 日本 字体",preset.Get("Fontname"));
            Assert.Equal("4",other.Get("Outline"));Assert.NotEqual("4",preset.Get("Outline"));
            store.Editor(one).Undo.Undo();Assert.NotEqual("Missing 日本 字体",preset.Get("Fontname"));Assert.Equal("70",script.Document.Styles[0].Get("Fontsize"));
            var reopened=new StyleLibraryStore(path);Assert.Equal("Missing 日本 字体",reopened.Editor(reopened.Collections[0]).Document.Styles[0].Get("Fontname"));
            var copied=StyleLibraryStore.Copy(other,script);Assert.NotEqual(script.Document.Styles[0].Name,copied.Name);
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }

    [Fact] public void UnreferencedStyleNeedsNoReplacementEvenWhenItIsTheLastStyle()
    {
        var editor=new SubtitleEditor(AssDocument.CreateEmpty());var style=editor.Document.Styles[0];
        editor.DeleteStyle(style);Assert.Empty(editor.Document.Styles);editor.Undo.Undo();Assert.Same(style,editor.Document.Styles[0]);
        var line=editor.Insert(null,false);Assert.Equal(style.Name,line.Style);
        Assert.Throws<ArgumentException>(()=>editor.DeleteStyle(style));Assert.Same(style,editor.Document.Styles[0]);Assert.Equal(style.Name,line.Style);
    }
}
