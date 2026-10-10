using Yoake.Core.Settings;

namespace Yoake.Core.Tests;

public sealed class SettingsTests
{
    [Fact]
    public void AutomationSearchPathsRoundTripAndEmptyAutoloadRemainsDistinctFromDefaults()
    {
        var root = Path.Combine(Path.GetTempPath(), "yoake-automation-settings-" + Guid.NewGuid().ToString("N"));
        var store = new SettingsStore(Path.Combine(root, "settings.json"));
        try
        {
            Assert.Null(store.Load().AutomationAutoloadDirectories);
            store.Save(new AppSettings { AutomationAutoloadDirectories = [], AutomationIncludeDirectories = [" ?user/日本語 ", "?data/automation/include", "?user/日本語"] });
            var loaded = store.Load();
            Assert.NotNull(loaded.AutomationAutoloadDirectories); Assert.Empty(loaded.AutomationAutoloadDirectories);
            Assert.Equal(new[] { "?user/日本語", "?data/automation/include" }, loaded.AutomationIncludeDirectories);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void NormalModeUsesRoamingDirectory()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var exe = System.IO.Path.Combine(root, "app");
        var roaming = System.IO.Path.Combine(root, "roaming");
        Directory.CreateDirectory(exe);
        try
        {
            var path = SettingsPathResolver.Resolve(exe, roaming);
            Assert.Equal(System.IO.Path.Combine(roaming, "Yoake", "settings.json"), path);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void PortableMarkerKeepsSettingsBesideExecutable()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var exe = System.IO.Path.Combine(root, "app");
        Directory.CreateDirectory(exe);
        File.WriteAllText(System.IO.Path.Combine(exe, "yoake.portable"), string.Empty);
        try
        {
            var path = SettingsPathResolver.Resolve(exe, System.IO.Path.Combine(root, "roaming"));
            Assert.Equal(System.IO.Path.Combine(exe, "config", "settings.json"), path);
        }
        finally { Directory.Delete(root, true); }
    }
}
