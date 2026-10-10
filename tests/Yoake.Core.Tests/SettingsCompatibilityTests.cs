using System.Text.Json;
using Yoake.Core.Settings;

namespace Yoake.Core.Tests;

public sealed class SettingsCompatibilityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Yoake-settings-" + Guid.NewGuid());
    private string SettingsPath => Path.Combine(_root, "settings.json");
    private readonly List<string> _warnings = [];
    public SettingsCompatibilityTests() => Directory.CreateDirectory(_root);
    private AppSettings Read(string json)
    {
        File.WriteAllText(SettingsPath, json);
        return new SettingsStore(SettingsPath).Load(_warnings.Add);
    }
    // Exact shape of the profile involved in the reported desktop crash.
    private const string LegacyProfile = """
        { "schemaVersion": 1, "theme": 2, "mainSplitRatio": 0.5, "gridHeight": 230 }
        """;
    [Fact] public void LegacyProfileRetainsNewCollectionDefaults()
    {
        var settings = Read(LegacyProfile);
        Assert.Equal(ThemePreference.Dark, settings.Theme);
        Assert.NotNull(settings.GridColumnWidths);
        Assert.Equal(new AppSettings().GridColumnWidths, settings.GridColumnWidths);
        Assert.NotNull(settings.RecentFiles);
        Assert.Empty(settings.RecentFiles);
        Assert.Equal(180,settings.AudioDisplayHeight);
        Assert.Equal(LegacyProfile, File.ReadAllText(SettingsPath)); // No startup rewrite.
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"gridColumnWidths\":null,\"recentFiles\":null}")]
    [InlineData("{\"gridColumnWidths\":[],\"recentFiles\":[]}")]
    public void MissingNullOrEmptyCollectionsUseSafeDefaults(string json)
    {
        var settings = Read(json);
        Assert.Equal(new AppSettings().GridColumnWidths, settings.GridColumnWidths);
        Assert.Empty(settings.RecentFiles);
        Assert.Equal(1, settings.SchemaVersion);
        Assert.Equal(0.5, settings.MainSplitRatio);
        Assert.Equal(230, settings.GridHeight);
    }
    [Fact] public void ShortAndOverlongWidthListsPreserveUsableValues()
    {
        Assert.Equal(new double[] { 50, 70, 48, 92, 92, 110, 100, 90 }, Read("{\"gridColumnWidths\":[50,70]}").GridColumnWidths);
        Assert.Equal(Enumerable.Repeat(80d,8), Read("{\"gridColumnWidths\":[80,80,80,80,80,80,80,80,999]}").GridColumnWidths);
    }
    [Fact] public void StoredAudioSplitIsPreservedWhileMissingHeightUsesBalancedDefault()
    {
        Assert.Equal(180,Read("{}").AudioDisplayHeight);
        Assert.Equal(160,Read("{\"audioDisplayHeight\":160}").AudioDisplayHeight);
        Assert.Equal(220,Read("{\"audioDisplayHeight\":220}").AudioDisplayHeight);
    }
    [Fact] public void NonFiniteNegativeAndExtremeWidthsAreRecoveredWithoutLosingTheme()
    {
        var settings = Read("""
            {"theme":2,"gridColumnWidths":["NaN","Infinity","-Infinity",-1,0,1,1000000,80]}
            """);
        Assert.Equal(ThemePreference.Dark, settings.Theme);
        Assert.Equal(new double[] { 40,65,48,92,92,24,600,80 }, settings.GridColumnWidths);
        Assert.NotEmpty(_warnings);
    }
    [Fact] public void BadInMemoryValuesAreNormalizedAsWell()
    {
        var settings = new AppSettings { GridColumnWidths = null!, RecentFiles = null!, Theme = (ThemePreference)999,
            MainSplitRatio = double.NaN, GridHeight = double.PositiveInfinity }.Normalize();
        Assert.Equal(new AppSettings().GridColumnWidths, settings.GridColumnWidths);
        Assert.Empty(settings.RecentFiles);
        Assert.Equal(ThemePreference.System, settings.Theme);
        Assert.Equal(0.5, settings.MainSplitRatio);
        Assert.Equal(230, settings.GridHeight);
    }
    [Fact] public void RecentFilesRemoveNullEmptyAndDuplicateEntries()
    {
        var settings = Read("""{"recentFiles":[null,""," ","one.ass","ONE.ASS","日本語.ass"]}""");
        Assert.Equal(new[] { "one.ass", "日本語.ass" }, settings.RecentFiles);
    }
    [Fact] public void UnknownFutureSettingsSurviveNormalizationAndSave()
    {
        var settings = Read("""{"theme":1,"futureFeature":{"enabled":true,"name":"မြန်မာ"}}""");
        new SettingsStore(SettingsPath).Save(settings);
        using var json = JsonDocument.Parse(File.ReadAllText(SettingsPath));
        Assert.Equal("မြန်မာ", json.RootElement.GetProperty("futureFeature").GetProperty("name").GetString());
        Assert.Equal(ThemePreference.Light, new SettingsStore(SettingsPath).Load().Theme);
    }
    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{\"gridColumnWidths\":[NaN]}")] // Bare NaN is invalid JSON.
    public void IrrecoverableJsonFallsBackReportsReasonAndLeavesOriginalFile(string json)
    {
        var settings = Read(json);
        Assert.Equal(new AppSettings().GridColumnWidths, settings.GridColumnWidths);
        Assert.NotEmpty(_warnings);
        Assert.Equal(json, File.ReadAllText(SettingsPath));
    }
    [Fact] public void CleanProfileNeedsNoExistingFileAndNoWarnings()
    {
        var settings = new SettingsStore(SettingsPath).Load(_warnings.Add);
        Assert.Equal(new AppSettings().GridColumnWidths, settings.GridColumnWidths);
        Assert.Empty(_warnings);
        Assert.False(File.Exists(SettingsPath));
    }
    [Theory]
    [InlineData("{\"theme\":2,\"gridHeight\":null,\"recentFiles\":[\"keep.ass\"],\"futureFlag\":true}")]
    [InlineData("{\"theme\":2,\"gridColumnWidths\":[80,\"bad\",90],\"recentFiles\":[\"keep.ass\"],\"futureFlag\":true}")]
    [InlineData("{\"theme\":2,\"gridColumnWidths\":\"bad\",\"recentFiles\":[\"keep.ass\"],\"futureFlag\":true}")]
    public void BadMemberTypeDoesNotDiscardOtherValidOrFutureSettings(string json)
    {
        var settings = Read(json);
        Assert.Equal(ThemePreference.Dark, settings.Theme);
        Assert.Equal(new[] { "keep.ass" }, settings.RecentFiles);
        Assert.True(settings.FutureSettings!["futureFlag"].GetBoolean());
        Assert.All(settings.GridColumnWidths, width => Assert.True(double.IsFinite(width) && width >= 24 && width <= 600));
        Assert.NotEmpty(_warnings);
    }
    public void Dispose() => Directory.Delete(_root, true);
}
