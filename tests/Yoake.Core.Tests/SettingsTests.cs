using Yoake.Core.Settings;

namespace Yoake.Core.Tests;

public sealed class SettingsTests
{
    [Fact]
    public void UiFontRoundTripsAndSurvivesDamagedUnrelatedSetting()
    {
        var root=Path.Combine(Path.GetTempPath(),"yoake-font-settings-"+Guid.NewGuid());
        Directory.CreateDirectory(root);
        var store=new SettingsStore(Path.Combine(root,"settings.json"));
        try
        {
            store.Save(new AppSettings { UiFontFamily=" Noto Sans Myanmar ", UiFontSize=15, GlobalTimingHotkeys=true });
            var loaded=store.Load();
            Assert.Equal("Noto Sans Myanmar",loaded.UiFontFamily);
            Assert.Equal(15,loaded.UiFontSize);
            Assert.True(loaded.GlobalTimingHotkeys);
            File.WriteAllText(store.Path,"{\"uiFontFamily\":\"日本語\",\"uiFontSize\":16,\"viewMode\":{}}");
            loaded=store.Load();
            Assert.Equal("日本語",loaded.UiFontFamily);
            Assert.Equal(16,loaded.UiFontSize);
            Assert.Null((loaded with { UiFontSize=double.NaN }).Normalize().UiFontSize);
            Assert.Null((loaded with { UiFontFamily="  " }).Normalize().UiFontFamily);
        }
        finally { Directory.Delete(root,true); }
    }

    [Theory]
    [InlineData(WorkspaceViewMode.Default)]
    [InlineData(WorkspaceViewMode.Audio)]
    [InlineData(WorkspaceViewMode.Video)]
    [InlineData(WorkspaceViewMode.AudioVideo)]
    public void WorkspaceModeRoundTripsIndependentlyOfMedia(WorkspaceViewMode mode)
    {
        var root=Path.Combine(Path.GetTempPath(),"yoake-view-settings-"+Guid.NewGuid());
        var store=new SettingsStore(Path.Combine(root,"settings.json"));
        try
        {
            store.Save(new AppSettings { ViewMode=mode, AudioDisplayHeight=240 });
            Assert.Equal(mode,store.Load().ViewMode); Assert.Equal(240,store.Load().AudioDisplayHeight);
        }
        finally { Directory.Delete(root,true); }
    }

    [Theory]
    [InlineData("999")]
    [InlineData("-1")]
    [InlineData("\"future-mode\"")]
    [InlineData("null")]
    [InlineData("{}")]
    public void DamagedOrUnknownWorkspaceModeFallsBackWithoutLosingOtherSettings(string value)
    {
        var root=Path.Combine(Path.GetTempPath(),"yoake-view-settings-"+Guid.NewGuid());
        Directory.CreateDirectory(root); var store=new SettingsStore(Path.Combine(root,"settings.json"));
        try
        {
            File.WriteAllText(store.Path,"{\"viewMode\":"+value+",\"audioDisplayHeight\":240}");
            var loaded=store.Load(); Assert.Equal(WorkspaceViewMode.Default,loaded.ViewMode);
            Assert.Equal(240,loaded.AudioDisplayHeight);
            store.Save(loaded); Assert.Equal(WorkspaceViewMode.Default,store.Load().ViewMode);
            Assert.Equal(1,File.ReadAllText(store.Path).Split("\"viewMode\"").Length-1);
        }
        finally { Directory.Delete(root,true); }
    }
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
