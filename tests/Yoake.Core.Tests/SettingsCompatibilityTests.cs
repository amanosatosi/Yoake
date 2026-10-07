using Yoake.Core.Settings;

namespace Yoake.Core.Tests;

public sealed class SettingsCompatibilityTests
{
    // Exact shape of the profile involved in the reported desktop crash.
    private const string LegacyProfile = """
        { "schemaVersion": 1, "theme": 2, "mainSplitRatio": 0.5, "gridHeight": 230 }
        """;

    [Fact]
    public void LegacyProfileRetainsNewCollectionDefaults()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Yoake-settings-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path, LegacyProfile);
            var settings = new SettingsStore(path).Load();
            Assert.Equal(ThemePreference.Dark, settings.Theme);
            Assert.NotNull(settings.GridColumnWidths);
            Assert.Equal(new AppSettings().GridColumnWidths, settings.GridColumnWidths);
            Assert.NotNull(settings.RecentFiles);
            Assert.Empty(settings.RecentFiles);
        }
        finally { Directory.Delete(directory, true); }
    }
}
