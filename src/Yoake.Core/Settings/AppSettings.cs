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
    public string[] RecentFiles { get; init; } = [];
    public int SchemaVersion { get; init; } = 1;
    public ThemePreference Theme { get; init; } = ThemePreference.System;
    public double MainSplitRatio { get; init; } = 0.5;
    public double GridHeight { get; init; } = 230;
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppSettings))]
internal partial class SettingsJsonContext : JsonSerializerContext
{
}
