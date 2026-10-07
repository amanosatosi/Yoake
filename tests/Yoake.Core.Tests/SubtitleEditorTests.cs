using System.Text;
using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class SubtitleEditorTests
{
    private static string Fixture => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "fansub.ass"));
    private static SubtitleEditor Open() => new(AssDocument.Parse(Fixture));
    [Fact] public void RealFixtureRoundTripsExactly() => Assert.Equal(Fixture, Open().Document.Serialize());
    [Fact] public void MixedNewlinesAndSpellingSurvive()
    {
        const string source = "[Events]\r\nFormat: Text, Start, Name, End, Unknown\n  dIaLoGuE:\tHello, world,0:00:01.00,Actor,0:00:02.00,raw\r";
        var doc = AssDocument.Parse(source); Assert.Equal(source, doc.Serialize());
        Assert.Equal("Hello, world", doc.Events[0].Text); doc.Events[0].Actor = "日本語";
        Assert.Equal(source.Replace("Actor", "日本語"), doc.Serialize());
    }
    [Fact] public void FieldEditIsSemanticAndUndoRestoresExactSource()
    {
        var editor = Open(); var line = editor.Document.Events[0];
        editor.EditEvent(line, new Dictionary<string,string> { ["Text"] = "မြန်မာ\n日本語 👩‍👩‍👧‍👦", ["Name"] = "New actor" });
        Assert.Contains("\\N", line.Text); Assert.True(editor.IsDirty); Assert.Equal("Edit subtitle", editor.Undo.NextUndoName);
        editor.Undo.Undo(); Assert.Equal(Fixture, editor.Document.Serialize()); Assert.False(editor.IsDirty);
        editor.Undo.Redo(); Assert.Equal("New actor", line.Actor);
    }
    [Fact] public void SavepointTracksUndoRedoAndBranching()
    {
        var e = Open(); var line = e.Document.Events[0];
        e.SetField(line,"Text","saved","Edit subtitle text"); e.MarkSaved();
        e.SetField(line,"Text","later","Edit subtitle text"); Assert.True(e.IsDirty);
        e.Undo.Undo(); Assert.False(e.IsDirty); e.Undo.Undo(); Assert.True(e.IsDirty);
        e.SetField(line,"Text","branch","Edit subtitle text"); Assert.True(e.IsDirty); Assert.False(e.Undo.CanRedo);
    }
    [Fact] public void InsertDeleteDuplicateMoveLeaveUnknownSourceAlone()
    {
        var e = Open(); var first = e.Document.Events[0]; var second = e.Document.Events[1];
        var copies = e.Duplicate([first, second]); Assert.Equal(5, e.Document.Events.Count); Assert.True(copies[1].IsComment);
        Assert.Contains("; a source comment between events must stay here", e.Document.Serialize());
        Assert.Contains("FutureEvent", e.Document.Serialize()); Assert.Contains("opaque", e.Document.Serialize());
        e.Move(copies, -1); e.Delete([first, second]);
        e.Undo.Undo(); e.Undo.Undo(); e.Undo.Undo(); Assert.Equal(Fixture, e.Document.Serialize());
        var inserted = e.Insert(first, false, 1230); Assert.Equal(inserted, e.Document.Events[0]); Assert.Equal(1230, inserted.StartMilliseconds);
        e.Undo.Undo(); Assert.Equal(Fixture, e.Document.Serialize());
    }
    [Fact] public void ClipboardCarriesUnknownFormatsAndComments()
    {
        var e = Open(); var text = e.Copy(e.Document.Events); var other = new SubtitleEditor(AssDocument.CreateEmpty());
        other.Paste(text, null); Assert.Equal(e.Document.Events.Select(l=>l.Text), other.Document.Events.Select(l=>l.Text));
        Assert.True(other.Document.Events[1].IsComment); Assert.Equal("raw", other.Document.Events[0].Get("FutureEvent"));
        Assert.Equal(other.Document.Serialize(), AssDocument.Parse(other.Document.Serialize()).Serialize());
    }
    [Fact] public void MoveAcrossFormatsAddsCorrectLocalFormat()
    {
        var doc = AssDocument.Parse("[Events]\nFormat: Start,End,Text\nDialogue: 0:00:00.00,0:00:01.00,A,B\n;keep\nFormat: Text,End,Start,Custom\nComment: C,D,0:00:03.00,0:00:02.00,extra\n");
        var e = new SubtitleEditor(doc); e.Move([doc.Events[1]], -1);
        var reopened = AssDocument.Parse(doc.Serialize()); Assert.Equal("C,D", reopened.Events[0].Text); Assert.Equal("extra", reopened.Events[0].Get("Custom")); Assert.Equal("A,B", reopened.Events[1].Text);
        e.Undo.Undo(); Assert.StartsWith("A,B", doc.Events[0].Text);
    }
    [Fact] public void TimingGestureCommitsOnceAndCaptureCancellationRollsBack()
    {
        var e = Open(); var line = e.Document.Events[0];
        using (var drag = e.Undo.BeginTransaction("Drag line timing")) { e.SetTiming(line,1100,3100); e.SetTiming(line,1200,3200); drag.Commit(); }
        Assert.Equal("Drag line timing", e.Undo.NextUndoName); e.Undo.Undo(); Assert.Equal(1000,line.StartMilliseconds); Assert.False(e.IsDirty);
        using (e.Undo.BeginTransaction("Cancelled")) e.SetTiming(line,2000,4000);
        Assert.Equal(1000,line.StartMilliseconds); Assert.False(e.IsDirty); Assert.True(e.Undo.CanRedo);
    }
    [Fact] public void ValidationCannotPartiallyCommit()
    {
        var e = Open(); Assert.Throws<ArgumentException>(()=>e.EditEvent(e.Document.Events[0],new Dictionary<string,string>{{"Text","changed"},{"End","bad"}}));
        Assert.Equal(Fixture,e.Document.Serialize()); Assert.False(e.IsDirty);
    }
    [Fact] public void StylesPreserveExtensionsAndRenameReferencesAtomically()
    {
        var e = Open(); var style = e.Document.Styles[0];
        e.EditStyle(style,new Dictionary<string,string>{{"Name","မြန်မာ"},{"Fontsize","54"}});
        Assert.Equal("မြန်မာ",e.Document.Events[0].Style); Assert.Equal("opaque",style.Get("FutureStyle"));
        e.Undo.Undo(); Assert.Equal(Fixture,e.Document.Serialize());
        var added=e.AddStyle(style); Assert.NotEqual(style.Name,added.Name);
        e.DeleteStyle(style,added.Name); Assert.All(e.Document.Events.Where(l=>l.Style!="日本語"),l=>Assert.Equal(added.Name,l.Style));
        e.Undo.Undo(); e.Undo.Undo(); Assert.Equal(Fixture,e.Document.Serialize());
    }
    [Fact] public void DocumentHistoriesAreIndependent()
    {
        var a=Open(); var b=Open(); a.SetField(a.Document.Events[0],"Text","A","Edit");
        Assert.False(b.IsDirty); Assert.False(b.Undo.CanUndo); Assert.Throws<InvalidOperationException>(()=>a.SetField(b.Document.Events[0],"Text","bad","Edit"));
    }
    [Theory] [InlineData("éx",1,0)] [InlineData("👩‍👩‍👧‍👦x",3,0)] [InlineData("A\\Nx",2,1)] [InlineData("{\\pos(1,2)}x",5,0)]
    public void SplitUsesGraphemeAndOverrideBoundaries(string text,int cursor,int expected) => Assert.Equal(expected,AssText.SafeSplitIndex(text,cursor));
    [Fact] public void SplitJoinAndCommentsUndo()
    {
        var e=Open(); var line=e.Document.Events[2]; var original=line.Text; var cursor=original.IndexOf("Sign",StringComparison.Ordinal)+2;
        var second=e.Split(line,cursor); Assert.Equal(line.End,second.Start); e.Join([line,second]); e.ToggleComment([line]); Assert.True(line.IsComment);
        e.Undo.Undo(); e.Undo.Undo(); e.Undo.Undo(); Assert.Equal(Fixture,e.Document.Serialize());
    }
    [Fact] public void ScriptInfoEditPreservesUnknownSectionAndUndo()
    {
        var e=Open(); e.SetScriptInfo(new Dictionary<string,string>{{"Title","New"},{"PlayResX","1280"}}); Assert.Equal("1280",e.Document.GetScriptInfo("PlayResX"));
        Assert.Contains("Binary-ish: 0,0,whatever\\unknown",e.Document.Serialize()); e.Undo.Undo(); Assert.Equal(Fixture,e.Document.Serialize());
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void SaveReopenPreservesBomAndUnknownContent(bool utf16)
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".ass");
        try { var encoding=utf16 ? (Encoding)new UnicodeEncoding(false,true,true) : new UTF8Encoding(true,true); File.WriteAllText(path,Fixture,encoding);
            var e=new SubtitleEditor(AssDocument.Load(path)); e.SetField(e.Document.Events[0],"Name","Saved actor","Change actor"); e.Document.Save(path);
            var reopened=AssDocument.Load(path); Assert.Equal(e.Document.Serialize(),reopened.Serialize()); Assert.True(File.ReadAllBytes(path).AsSpan().StartsWith(encoding.GetPreamble()));
        } finally { File.Delete(path); }
    }
    [Fact] public void LargeFileSupportsRowOperationsWithoutReparsingIdentity()
    {
        var text="[Events]\nFormat: "+string.Join(',',AssDocument.EventFormat)+"\n"+string.Concat(Enumerable.Range(0,5000).Select(i=>$"Dialogue: 0,0:00:00.00,0:00:02.00,Default,,0,0,0,,Line {i}\n"));
        var e=new SubtitleEditor(AssDocument.Parse(text)); var last=e.Document.Events[^1]; e.Delete(e.Document.Events.Take(10).ToArray()); Assert.Same(last,e.Document.Events[^1]); e.Undo.Undo(); Assert.Equal(text,e.Document.Serialize());
    }
}
