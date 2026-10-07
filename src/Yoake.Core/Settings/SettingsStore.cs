using System.Text.Json;
using System.Globalization;

namespace Yoake.Core.Settings;

public static class SettingsPathResolver
{
    public static string Resolve(string executableDirectory, string roamingAppDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(roamingAppDataDirectory);
        var portableMarker = Path.Combine(executableDirectory, "yoake.portable");
        return File.Exists(portableMarker)
            ? Path.Combine(executableDirectory, "config", "settings.json")
            : Path.Combine(roamingAppDataDirectory, "Yoake", "settings.json");
    }
}

public sealed class SettingsStore(string path)
{
    public string Path { get; } = path;

    public AppSettings Load(Action<string>? report = null)
    {
        if (!File.Exists(Path))
            return new AppSettings();
        try
        {
            using var stream = File.OpenRead(Path);
            using var document = JsonDocument.Parse(stream);
            AppSettings? settings;
            try { settings = JsonSerializer.Deserialize(document.RootElement, SettingsJsonContext.Default.AppSettings); }
            catch (JsonException exception) when (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                // Syntactically valid JSON with one damaged member is recoverable.
                // Retain valid members and unknown future data rather than throwing
                // away the whole profile because a scalar is null or has a bad type.
                report?.Invoke($"Invalid settings member ({exception.Message}); recovering members independently.");
                settings = RecoverMembers(document.RootElement);
            }
            if (settings is null) report?.Invoke("Settings JSON contained null; using defaults without changing the file.");
            return (settings ?? new AppSettings()).Normalize(report);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            report?.Invoke($"Settings could not be read ({exception.GetType().Name}: {exception.Message}); using defaults without changing the file.");
            return new AppSettings();
        }
    }

    private static AppSettings RecoverMembers(JsonElement root)
    {
        var defaults = new AppSettings();
        var theme = defaults.Theme; var schema = defaults.SchemaVersion;
        var ratio = defaults.MainSplitRatio; var height = defaults.GridHeight;
        double? volume=null,audioHeight=null,intensity=null;var muted=false;double[]? compact=null;
        double[]? widths = null; string[]? recent = null;
        Dictionary<string, JsonElement> future = [];
        foreach (var member in root.EnumerateObject())
        {
            switch (member.Name)
            {
                case "schemaVersion": if (TryReadInt(member.Value, out var version)) schema = version; break;
                case "theme": if (TryReadInt(member.Value, out var value)) theme = (ThemePreference)value; break;
                case "mainSplitRatio": ratio = ReadNumber(member.Value); break;
                case "gridHeight": height = ReadNumber(member.Value); break;
                case "playbackVolume":volume=ReadNumber(member.Value);break;
                case "playbackMuted":if(member.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)muted=member.Value.GetBoolean();break;
                case "audioDisplayHeight":audioHeight=ReadNumber(member.Value);break;
                case "audioIntensity":intensity=ReadNumber(member.Value);break;
                case "compactGridColumnWidths":if(member.Value.ValueKind==JsonValueKind.Array)compact=member.Value.EnumerateArray().Take(8).Select(ReadNumber).ToArray();break;
                case "gridColumnWidths":
                    if (member.Value.ValueKind == JsonValueKind.Array) widths = member.Value.EnumerateArray().Take(9).Select(ReadNumber).ToArray();
                    break;
                case "recentFiles":
                    if (member.Value.ValueKind == JsonValueKind.Array) recent = member.Value.EnumerateArray().Take(24).Select(v => v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "").ToArray();
                    break;
                default: future[member.Name] = member.Value.Clone(); break;
            }
        }
        return new AppSettings { Theme = theme, SchemaVersion = schema, MainSplitRatio = ratio, GridHeight = height,
            GridColumnWidths = widths!, RecentFiles = recent!, FutureSettings = future,
            PlaybackVolume=volume,PlaybackMuted=muted,AudioDisplayHeight=audioHeight,AudioIntensity=intensity,CompactGridColumnWidths=compact };
    }

    private static double ReadNumber(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return number;
        return double.NaN;
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var directory = System.IO.Path.GetDirectoryName(Path)
            ?? throw new InvalidOperationException("Settings path has no directory.");
        Directory.CreateDirectory(directory);
        var temporary = Path + ".tmp";
        using (var stream = File.Create(temporary))
            JsonSerializer.Serialize(stream, settings.Normalize(), SettingsJsonContext.Default.AppSettings);
        File.Move(temporary, Path, true);
    }

    private static bool TryReadInt(JsonElement value, out int result)
    {
        result = 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out result);
    }
}
