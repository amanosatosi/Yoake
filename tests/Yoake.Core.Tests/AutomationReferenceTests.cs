using Yoake.Core.Automation;
using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class AutomationReferenceTests
{
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
