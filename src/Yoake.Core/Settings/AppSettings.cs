using System.Text.Json;
using System.Text.Json.Serialization;

namespace Yoake.Core.Settings;

public enum ThemePreference
{
    System,
    Light,
    Dark,
}

public sealed record AppSettings
{
    public double[] GridColumnWidths { get; init; } = [40,65,48,92,92,110,100,90];
    public string[] RecentFiles { get; init; } = [];
    public int SchemaVersion { get; init; } = 1;
    public ThemePreference Theme { get; init; } = ThemePreference.System;
    public double MainSplitRatio { get; init; } = 0.5;
    public double GridHeight { get; init; } = 230;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? FutureSettings { get; set; }

    // Source-generated construction of init-only properties can supply null/zero
    // for absent JSON members instead of retaining their C# field initializers.
    // Validate at the persistence boundary, and also for in-memory callers.
    public AppSettings Normalize(Action<string>? report = null)
    {
        var defaults = new AppSettings();
        var widths = (double[])defaults.GridColumnWidths.Clone();
        if (GridColumnWidths is { } input)
            for (var i = 0; i < Math.Min(input.Length, widths.Length); i++)
                if (double.IsFinite(input[i]) && input[i] > 0) widths[i] = Math.Clamp(input[i], 24, 600);
        if (GridColumnWidths is null || !GridColumnWidths.SequenceEqual(widths))
            report?.Invoke("GridColumnWidths normalized: expected eight finite widths between 24 and 600.");
        var recent = (RecentFiles ?? []).Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray();
        if (RecentFiles is null || !RecentFiles.SequenceEqual(recent))
            report?.Invoke("RecentFiles normalized: removed null/empty, duplicate or excess entries.");
        var theme = Theme is >= ThemePreference.System and <= ThemePreference.Dark ? Theme : defaults.Theme;
        var ratio = double.IsFinite(MainSplitRatio) && MainSplitRatio > 0 && MainSplitRatio < 1 ? Math.Clamp(MainSplitRatio, 0.1, 0.9) : defaults.MainSplitRatio;
        var height = double.IsFinite(GridHeight) && GridHeight > 0 ? Math.Clamp(GridHeight, 100, 2000) : defaults.GridHeight;
        if (theme != Theme || ratio != MainSplitRatio || height != GridHeight)
            report?.Invoke("Invalid theme or workspace dimensions normalized.");
        return this with { GridColumnWidths = widths, RecentFiles = recent, Theme = theme,
            MainSplitRatio = ratio, GridHeight = height, SchemaVersion = SchemaVersion > 0 ? SchemaVersion : defaults.SchemaVersion };
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals)]
[JsonSerializable(typeof(AppSettings))]
internal partial class SettingsJsonContext : JsonSerializerContext
{
}
