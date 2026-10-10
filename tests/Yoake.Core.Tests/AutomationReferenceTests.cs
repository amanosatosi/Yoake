using Yoake.Core.Automation;
using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class AutomationReferenceTests
{
    [Fact]
    public void SaveAsRebasesKnownLocationsAndPreservesMissingAndUnknownReferences()
    {
        var root = Path.Combine(Path.GetTempPath(), "yoake-references");
        var automationBase = Path.Combine(root, "automation");
        var oldSubtitle = Path.Combine(root, "song", "old.ass");
        var newSubtitle = Path.Combine(root, "export", "new.ass");
        var script = Path.Combine(root, "song", "日本語 မြန်မာ.lua");
        var before = "  ~日本語 မြန်မာ.lua  ||~missing.lua|!future.lua|";
        var after = AutomationScriptReference.RebaseForSaveAs(before, newSubtitle, automationBase,
            new Dictionary<string, string> { ["~日本語 မြန်မာ.lua"] = script });
        Assert.EndsWith("||~missing.lua|!future.lua|", after);
        Assert.StartsWith("  ", after); Assert.EndsWith("  ", after.Split('|')[0]);
        Assert.Equal(AutomationScriptReference.Resolve("~日本語 မြန်မာ.lua", oldSubtitle, automationBase),
            AutomationScriptReference.Resolve(after.Split('|')[0], newSubtitle, automationBase));
        Assert.Equal(after, AutomationScriptReference.RebaseForSaveAs(after, newSubtitle, automationBase,
            new Dictionary<string, string> { [after.Split('|')[0].Trim()] = script }));
    }

    [Fact]
    public void FailedSaveRollsBackRebasedMetadataAndRetainsRedoBranch()
    {
        var root = Path.Combine(Path.GetTempPath(), "yoake-reference-save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var document = AssDocument.Parse("[Script Info]\nTitle: Keep\nAutomation Scripts: ~script.lua\n");
            var editor = new SubtitleEditor(document);
            editor.SetScriptInfo(new Dictionary<string, string> { ["Title"] = "Redo me" }); editor.Undo.Undo();
            var before = document.Serialize(); var redo = editor.Undo.NextRedoName;
            using (editor.Undo.BeginTransaction("Save As references"))
            {
                editor.SetProjectProperties(new Dictionary<string, string> { ["Automation Scripts"] = "~../song/script.lua" });
                var failure = Record.Exception(() => document.Save(root));
                Assert.True(failure is IOException or UnauthorizedAccessException, failure?.ToString());
            }
            Assert.Equal(before, document.Serialize()); Assert.False(editor.IsDirty);
            Assert.True(editor.Undo.CanRedo); Assert.Equal(redo, editor.Undo.NextRedoName);
            Assert.True(editor.Undo.Redo()); Assert.Equal("Redo me", document.GetScriptInfo("Title"));
            Assert.Empty(Directory.GetFileSystemEntries(root));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void AllThree322ReferenceMarkersResolveAndRoundTripUnicode()
    {
        var root = Path.Combine(Path.GetTempPath(), "yoake-references");
        var basePath = Path.Combine(root, "automation"); var subtitle = Path.Combine(root, "song", "lyrics.ass");
        var script = Path.Combine(root, "song", "日本語 မြန်မာ.lua");
        Assert.Equal(script, AutomationScriptReference.Resolve("~日本語 မြန်မာ.lua", subtitle, basePath));
        Assert.Equal(Path.Combine(basePath, "include", "helper.lua"), AutomationScriptReference.Resolve("$include/helper.lua", subtitle, basePath));
        var encoded = AutomationScriptReference.Encode(script, subtitle, basePath);
        Assert.Equal(script, AutomationScriptReference.Resolve(encoded, subtitle, basePath));
        Assert.Equal(script, AutomationScriptReference.Resolve("/" + script.Replace('\\', '/'), subtitle, basePath));
        Assert.Throws<ArgumentException>(() => AutomationScriptReference.Resolve("!unknown.lua", subtitle, basePath));
        Assert.Throws<ArgumentException>(() => AutomationScriptReference.Resolve("~missing.lua", null, basePath));
    }
    [Fact]
    public void ProjectEditsPreserveUnknownSourceMissingReferencesAndUndo()
    {
        var before = "[Script Info]\nTitle: Keep\nAutomation Scripts: ~legacy.lua\n[Unknown]\nMystery: untouched\n";
        var document = AssDocument.Parse(before); var editor = new SubtitleEditor(document);
        editor.SetProjectProperties(new Dictionary<string, string> { ["Automation Scripts"] = "~missing.lua|!unknown.lua" });
        Assert.Equal("~missing.lua|!unknown.lua", document.GetSectionValue("[Aegisub Project Garbage]", "Automation Scripts"));
        Assert.Contains("[Unknown]\nMystery: untouched\n", document.Serialize());
        editor.Undo.Undo(); Assert.Equal(before, document.Serialize());
        editor.Undo.Redo();
        editor.SetProjectProperties(new Dictionary<string, string> { ["Automation Scripts"] = "" });
        Assert.True(document.TryGetSectionValue("[Aegisub Project Garbage]", "Automation Scripts", out var value)); Assert.Equal("", value);
        Assert.Equal("~legacy.lua", document.GetScriptInfo("Automation Scripts"));
    }
}
