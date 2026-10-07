using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class StyleAuthoringTests
{
    [Theory]
    [InlineData("&H80402010",16,32,64,128)]
    [InlineData("&HFFFFFF&",255,255,255,0)]
    [InlineData("-1",255,255,255,255)]
    [InlineData("&H00FF0000",0,0,255,0)]
    public void AssBgrAndInvertedAlpha(string text,int r,int g,int b,int a)
    {
        Assert.True(AssColor.TryParse(text,out var color));Assert.Equal(new AssColor((byte)r,(byte)g,(byte)b,(byte)a),color);Assert.Equal(255-a,color.Opacity);
        Assert.True(AssColor.TryParse(color.StyleValue,out var reopened));Assert.Equal(color,reopened);
    }
    [Fact] public void TypedControlsPreserveExtraFieldsAndStyleReferences()
    {
        var doc=AssDocument.Parse("[V4+ Styles]\nFormat: Name,Fontname,Fontsize,Bold,PrimaryColour,Alignment,MarginL,Future\nStyle: 日本,Missing 字体,70,0,&H80224466,2,20,opaque\n[Events]\nFormat: Start,End,Style,Text\nDialogue: 0:00:00.00,0:00:01.00,日本,Text\n");
        var editor=new SubtitleEditor(doc);var draft=new StyleDraft(doc.Styles[0]);draft.Set("Name","မြန်မာ");draft.SetNumber("Fontsize",72.5m);draft.SetFlag("Bold",true);draft.SetColor("PrimaryColour",new(255,10,20,128));draft.Apply(editor);
        Assert.Equal("မြန်မာ",doc.Events[0].Style);Assert.Equal("opaque",doc.Styles[0].Get("Future"));Assert.Equal("&H80140AFF",doc.Styles[0].Get("PrimaryColour"));Assert.Equal("Missing 字体",doc.Styles[0].Get("Fontname"));
        Assert.Equal("Missing 字体",FontAvailability.PreserveName("Missing 字体",new[]{"Arial"}));Assert.False(FontAvailability.IsInstalled("Missing 字体",new[]{"Arial"}));
        editor.Undo.Undo();Assert.Equal("日本",doc.Events[0].Style);editor.Undo.Redo();Assert.Equal(doc.Serialize(),AssDocument.Parse(doc.Serialize()).Serialize());
    }
    [Theory] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)]
    public void AllNineAlignmentsSerialize(int value)
    {
        var editor=new SubtitleEditor(AssDocument.CreateEmpty());var draft=new StyleDraft(editor.Document.Styles[0]);draft.SetNumber("Alignment",value);draft.Apply(editor);Assert.Equal(value.ToString(),AssDocument.Parse(editor.Document.Serialize()).Styles[0].Get("Alignment"));
    }
    [Fact] public void StyleDeleteStillReplacesReferencesAndUndoes()
    {
        var editor=new SubtitleEditor(AssDocument.CreateEmpty());var first=editor.Document.Styles[0];var second=editor.AddStyle();var line=editor.Insert(null,false);editor.DeleteStyle(first,second.Name);Assert.Equal(second.Name,line.Style);editor.Undo.Undo();Assert.Equal(first.Name,line.Style);
    }
    [Fact] public void LibraryCopiesArePersistentAndIndependent()
    {
        var root=Path.Combine(Path.GetTempPath(),"Yoake-library-"+Guid.NewGuid());var path=Path.Combine(root,"styles.json");
        try
        {
            var source=AssDocument.Parse("[V4+ Styles]\nFormat: Name,Fontname,Future\nStyle: 日本語,字体,opaque\n");var library=new StyleLibraryStore(path);var collection=library.Create("မြန်မာ / 日本語");var stored=library.Import(source,library.Editor(collection))[0];library.Save();
            var reopened=new StyleLibraryStore(path);var reopenedStyle=reopened.Editor(reopened.Collections[0]).Document.Styles[0];Assert.Equal("opaque",reopenedStyle.Get("Future"));
            var script=new SubtitleEditor(AssDocument.CreateEmpty());var copied=StyleLibraryStore.Copy(reopenedStyle,script);script.SetField(copied,"Fontname","Different","Edit");Assert.Equal("字体",reopenedStyle.Get("Fontname"));Assert.False(library.Editor(collection).Document.Events.Any());
            library.DeletePreset(collection,stored);Assert.Empty(library.Editor(collection).Document.Styles);library.Editor(collection).Undo.Undo();library.Save();var finalStore=new StyleLibraryStore(path);Assert.Single(finalStore.Editor(finalStore.Collections[0]).Document.Styles);
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
