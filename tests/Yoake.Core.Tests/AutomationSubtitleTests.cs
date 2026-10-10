using System.Collections.Specialized;
using Yoake.Core.Automation;
using Yoake.Core.Subtitles;
using Yoake.Core.Undo;

namespace Yoake.Core.Tests;

public sealed class AutomationSubtitleTests
{
    private const string Source = "[Script Info]\n; preserve comment\nTitle: 日本語 မြန်မာ\nAutomation Scripts: ~missing.lua\n\n[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding, Future\nStyle: Default,Arial,060,&H00FFFFFF,&H0000FFFF,&H00000000,&H64000000,0,0,0,0,100,100,0,0,1,2,0,2,0030,0030,0030,1,style-secret\n\n[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text, Future\nDialogue: 0,0:00:01.00,0:00:02.00,Default,Actor,0000,0000,0000,,{\\distort(1,2)\\clippos(3,4)\\1grd&HFFFFFF&\\future(foo)}漢字|かんじ,a-secret\n; anchored comment\nDialogue: 1,0:00:02.00,0:00:03.00,Default,,0,0,0,,second,b-secret\n\n[Unknown]\nMystery: untouched\n";

    [Fact]
    public void ProjectionUsesFileIndexesAndDetachedReads()
    {
        var document = AssDocument.Parse(Source);
        using var subs = new AutomationSubtitleDocument(document);
        Assert.Equal(4, subs.Count);
        Assert.Equal("info", subs.Read(1)["class"]);
        Assert.Equal("style", subs.Read(2)["class"]);
        Assert.Equal(3, subs.IndexOf(document.Events[0]));
        Assert.Equal(new[] { 3, 4 }, subs.SelectionIndexes(document.Events.Reverse()));
        var detached = subs.Read(3); detached["text"] = "not assigned";
        Assert.NotEqual("not assigned", subs.Read(3)["text"]);
        Assert.Equal(Source, document.Serialize());
    }

    [Fact]
    public void TimingEditPreservesUnknownFieldsSyntaxWhitespaceAndOldUndoReferences()
    {
        var document = AssDocument.Parse(Source); var editor = new SubtitleEditor(document);
        var original = document.Events[0];
        editor.SetField(original, "Name", "previous edit", "Actor edit");
        var before = document.Serialize();
        using var subs = new AutomationSubtitleDocument(document);
        var line = subs.Read(3); line["start_time"] = 1230;
        subs.Write(3, line); subs.Commit(editor, "Timing macro");
        Assert.Same(original, document.Events[0]);
        Assert.Equal(before.Replace("0:00:01.00", "0:00:01.23"), document.Serialize());
        Assert.True(editor.Undo.Undo()); Assert.Equal(before, document.Serialize());
        Assert.True(editor.Undo.Undo()); Assert.Equal(Source, document.Serialize());
        Assert.True(editor.Undo.Redo()); Assert.True(editor.Undo.Redo());
        Assert.Equal(1230, original.StartMilliseconds);
    }

    [Fact]
    public void MultipleUndoPointsAndImplicitTailRemainSeparate()
    {
        var document = AssDocument.Parse(Source); var editor = new SubtitleEditor(document);
        using var subs = new AutomationSubtitleDocument(document);
        var line = subs.Read(3); line["text"] = "first";
        subs.Write(3, line); subs.SetUndoPoint("First point");
        line["text"] = "second"; subs.Write(3, line); subs.SetUndoPoint("Second point");
        line["text"] = "tail"; subs.Write(3, line);
        Assert.Equal(Source, document.Serialize());
        subs.Commit(editor, "Macro tail");
        Assert.Equal("Macro tail", editor.Undo.NextUndoName);
        editor.Undo.Undo(); Assert.Equal("second", document.Events[0].Text);
        Assert.Equal("Second point", editor.Undo.NextUndoName);
        editor.Undo.Undo(); Assert.Equal("first", document.Events[0].Text);
        Assert.Equal("First point", editor.Undo.NextUndoName);
        editor.Undo.Undo(); Assert.Equal(Source, document.Serialize());
        editor.Undo.Redo(); editor.Undo.Redo(); editor.Undo.Redo();
        Assert.Equal("tail", document.Events[0].Text);
    }

    [Fact]
    public void CancelDiscardsEvenEarlierUndoPointsAndExpiresView()
    {
        var document = AssDocument.Parse(Source); var editor = new SubtitleEditor(document);
        var subs = new AutomationSubtitleDocument(document);
        var line = subs.Read(3); line["text"] = "temporary";
        subs.Write(3, line); subs.SetUndoPoint("Not persistent"); subs.Delete(4);
        subs.Dispose();
        Assert.Equal(Source, document.Serialize()); Assert.False(editor.Undo.CanUndo);
        Assert.Throws<InvalidOperationException>(() => subs.Read(3));
    }

    [Fact]
    public void InvalidTablesAndStaleResultsDoNotTouchLiveDocument()
    {
        var document = AssDocument.Parse(Source); var editor = new SubtitleEditor(document);
        using var subs = new AutomationSubtitleDocument(document);
        var invalid = subs.Read(3); invalid.Fields.Remove("comment");
        Assert.Throws<ArgumentException>(() => subs.Write(3, invalid));
        var line = subs.Read(3); line["text"] = "worker result"; subs.Write(3, line);
        editor.SetField(document.Events[0], "Text", "newer editor result", "Edit");
        Assert.Throws<InvalidOperationException>(() => subs.Commit(editor, "Stale"));
        Assert.Equal("newer editor result", document.Events[0].Text);
        Assert.Equal("Edit", editor.Undo.NextUndoName);
    }

    [Fact]
    public void ReadOnlyContextsRejectEveryMutation()
    {
        using var subs = new AutomationSubtitleDocument(AssDocument.Parse(Source), writable: false);
        var line = subs.Read(3);
        Assert.Throws<InvalidOperationException>(() => subs.Write(3, line));
        Assert.Throws<InvalidOperationException>(() => subs.Append(line));
        Assert.Throws<InvalidOperationException>(() => subs.Insert(3, line));
        Assert.Throws<InvalidOperationException>(() => subs.Delete(3));
        Assert.Throws<InvalidOperationException>(() => subs.DeleteRange(3, 4));
        Assert.Throws<InvalidOperationException>(() => subs.SetUndoPoint("No"));
    }

    [Fact]
    public void NegativeWriteInsertsZeroAppendsAndReadsAreCopied()
    {
        using var subs = new AutomationSubtitleDocument(AssDocument.Parse(Source));
        var line = subs.Read(3); subs.Write(-3, line); subs.Write(0, line);
        Assert.Equal(6, subs.Count); Assert.Equal(line["text"], subs.Read(3)["text"]);
        subs.Write(3, null); Assert.Equal(5, subs.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => subs.Read(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => subs.Read(6));
        Assert.Throws<ArgumentOutOfRangeException>(() => subs.Write(6, line));
    }

    [Fact]
    public void AppendAndEndInsertRespectLastLineOfClass()
    {
        using var subs = new AutomationSubtitleDocument(AssDocument.Parse(Source));
        var style = subs.Read(2); style["name"] = "Added";
        subs.Insert(subs.Count + 1, style);
        Assert.Equal("Added", subs.Read(3)["name"]);
        Assert.Equal("dialogue", subs.Read(4)["class"]);
        var info = subs.Read(1); info["key"] = "New key";
        subs.Append(info); Assert.Equal("New key", subs.Read(2)["key"]);
    }

    [Fact]
    public void DeleteDuplicatesReproduceSourceQuirkAndRangeClamps()
    {
        using var subs = new AutomationSubtitleDocument(AssDocument.Parse(Source));
        subs.Delete(1, 1, 4); // duplicate blocks the source cursor before index 4
        Assert.Equal(3, subs.Count); Assert.Equal("second", subs.Read(3)["text"]);
        Assert.Throws<ArgumentOutOfRangeException>(() => subs.Delete(1, 50));
        Assert.Equal(3, subs.Count);
        subs.DeleteRange(0, 2); Assert.Equal(1, subs.Count);
        subs.DeleteRange(50, 99); Assert.Equal(1, subs.Count);
        subs.DeleteRange(1, 99); Assert.Equal(0, subs.Count);
    }

    [Fact]
    public void ReorderingAndDuplicatingPreserveProvenanceAndUnknownAnchors()
    {
        var document = AssDocument.Parse(Source); var editor = new SubtitleEditor(document);
        using var subs = new AutomationSubtitleDocument(document);
        var first = subs.Read(3); var second = subs.Read(4);
        subs.Write(3, second); subs.Write(4, first); subs.Append(first.Copy());
        subs.Commit(editor, "Reorder");
        Assert.Equal("b-secret", document.Events[0].Get("Future"));
        Assert.Equal("a-secret", document.Events[1].Get("Future"));
        Assert.Equal("a-secret", document.Events[2].Get("Future"));
        Assert.Contains("; anchored comment\n", document.Serialize());
        Assert.Contains("[Unknown]\nMystery: untouched\n", document.Serialize());
        editor.Undo.Undo(); Assert.Equal(Source, document.Serialize());
    }

    [Fact]
    public void StylesAndInfoEditsRetainTheirUnknownContent()
    {
        var document = AssDocument.Parse(Source); var editor = new SubtitleEditor(document);
        using var subs = new AutomationSubtitleDocument(document);
        var style = subs.Read(2); style["fontsize"] = 80; subs.Write(2, style);
        var info = subs.Read(1); info["value"] = "Changed"; subs.Write(1, info);
        subs.Commit(editor, "Metadata");
        Assert.Equal("style-secret", document.Styles[0].Get("Future"));
        Assert.Equal("0030", document.Styles[0].Get("MarginL"));
        Assert.Contains("Automation Scripts: ~missing.lua", document.Serialize());
        editor.Undo.Undo(); Assert.Equal(Source, document.Serialize());
    }

    [Fact]
    public void FiftyThousandAppendsPublishOneCollectionResetAndOneDocumentChange()
    {
        var document = AssDocument.Parse(Source); var editor = new SubtitleEditor(document);
        var changes = 0; var resets = 0; var historyChanges = 0;
        document.Changed += (_, _) => changes++;
        editor.Undo.Changed += (_, _) => historyChanges++;
        ((INotifyCollectionChanged)document.Events).CollectionChanged += (_, e) => { Assert.Equal(NotifyCollectionChangedAction.Reset, e.Action); resets++; };
        using var subs = new AutomationSubtitleDocument(document);
        var line = subs.Read(3);
        for (var i = 0; i < 50_000; i++) subs.Append(line);
        Assert.Equal(2, document.Events.Count);
        subs.Commit(editor, "KFX");
        Assert.Equal(50_002, document.Events.Count);
        Assert.Equal(1, changes); Assert.Equal(1, resets); Assert.Equal(1, historyChanges);
        editor.Undo.Undo(); Assert.Equal(Source, document.Serialize());
    }

    [Fact]
    public void CheckpointBatchFailurePreservesExistingUndoAndRedo()
    {
        var history = new UndoManager(); var value = 0;
        history.Execute(new DelegateUndoOperation("Existing", () => value = 0, () => value = 1));
        history.Undo();
        Assert.Throws<InvalidOperationException>(() => history.ExecuteBatch(new IUndoOperation[]
        {
            new DelegateUndoOperation("First", () => value = 0, () => value = 2),
            new DelegateUndoOperation("Fails", () => { }, () => throw new InvalidOperationException("fixture"))
        }));
        Assert.Equal(0, value); Assert.False(history.CanUndo); Assert.True(history.CanRedo);
        history.Redo(); Assert.Equal(1, value);
    }
}
