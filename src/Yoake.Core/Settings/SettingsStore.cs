using System.Text.Json;

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
            var settings = JsonSerializer.Deserialize(stream, SettingsJsonContext.Default.AppSettings);
            if (settings is null) report?.Invoke("Settings JSON contained null; using defaults without changing the file.");
            return (settings ?? new AppSettings()).Normalize(report);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            report?.Invoke($"Settings could not be read ({exception.GetType().Name}: {exception.Message}); using defaults without changing the file.");
            return new AppSettings();
        }
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
}
