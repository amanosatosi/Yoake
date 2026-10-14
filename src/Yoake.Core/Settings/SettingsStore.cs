using System.Text.Json;
using System.Globalization;
using Yoake.Core.Hotkeys;
using Yoake.Core.Subtitles;

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
        var viewMode = WorkspaceViewMode.Default;
        GridTagDisplayMode? tagDisplay=null;
        SubtitleSearchPreferences? search=null;
        SubtitleSelectionOptions? selection=null;
        bool? videoAutoSeek=null;
        var videoOverscan=false;Yoake.Core.Media.DummyVideoOptions? lastDummy=null;
        var showOriginal=false;PasteOverFields? pasteOverFields=null;
        bool? autosaveEnabled=null;int? autosaveInterval=null;string? recoveryDirectory=null;
        SubtitleShiftPreferences? shift=null;List<SubtitleShiftHistoryEntry> shiftHistory=[];
        var globalTiming=false;bool? toolbar=null; List<HotkeyOverride> hotkeys=[];
        var audioStopMigrated=false;
        var ratio = defaults.MainSplitRatio; var height = defaults.GridHeight;
        double? volume=null,audioHeight=null,intensity=null,audioSpan=null;var muted=false;var linked=false;double[]? compact=null,styleSplit=null;
        string[]? autoload=null,includes=null;string? assDrawExecutable=null;
        bool? audioSpectrum=null,audioAutoScroll=null;
        bool? audioAutoCommit=null,audioAutoNext=null;
        int? audioDefaultDuration=null,audioLeadIn=null,audioLeadOut=null;
        double[]? widths = null; string[]? recent = null;string[]? recentKeyframes=null;string[]? recentTimecodes=null;string[]? recentVideo=null,recentAudio=null;
        Dictionary<string, JsonElement> future = [];
        foreach (var member in root.EnumerateObject())
        {
            switch (member.Name)
            {
                case "assDrawExecutable":if(member.Value.ValueKind==JsonValueKind.String)assDrawExecutable=member.Value.GetString();break;
                case "autosaveEnabled":if(member.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)autosaveEnabled=member.Value.GetBoolean();break;
                case "autosaveIntervalSeconds":if(TryReadInt(member.Value,out var interval))autosaveInterval=interval;break;
                case "recoveryDirectory":if(member.Value.ValueKind==JsonValueKind.String)recoveryDirectory=member.Value.GetString();break;
                case "toolbarVisible":if(member.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)toolbar=member.Value.GetBoolean();break;
                case "gridTagDisplay":if(TryReadInt(member.Value,out var tags))tagDisplay=(GridTagDisplayMode)tags;break;
                case "searchPreferences":try{search=JsonSerializer.Deserialize(member.Value,SettingsJsonContext.Default.SubtitleSearchPreferences);}catch(JsonException){}break;
                case "selectionPreferences":try{selection=JsonSerializer.Deserialize(member.Value,SettingsJsonContext.Default.SubtitleSelectionOptions);}catch(JsonException){}break;
                case "videoAutoSeek":if(member.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)videoAutoSeek=member.Value.GetBoolean();break;
                case "videoOverscan":if(member.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)videoOverscan=member.Value.GetBoolean();break;
                case "showOriginal":if(member.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)showOriginal=member.Value.GetBoolean();break;
                case "pasteOverFields":if(TryReadInt(member.Value,out var pasteFields)&&pasteFields>=0&&pasteFields<=(int)PasteOverFields.All)pasteOverFields=(PasteOverFields)pasteFields;break;
                case "lastDummyVideo":try{lastDummy=JsonSerializer.Deserialize(member.Value,SettingsJsonContext.Default.DummyVideoOptions);}catch(JsonException){}break;
                case "shiftPreferences":try{shift=JsonSerializer.Deserialize(member.Value,SettingsJsonContext.Default.SubtitleShiftPreferences);}catch(JsonException){}break;
                case "shiftHistory":if(member.Value.ValueKind==JsonValueKind.Array)foreach(var item in member.Value.EnumerateArray().Take(50))try{if(JsonSerializer.Deserialize(item,SettingsJsonContext.Default.SubtitleShiftHistoryEntry) is {} entry)shiftHistory.Add(entry);}catch(JsonException){}break;
                case "schemaVersion": if (TryReadInt(member.Value, out var version)) schema = version; break;
                case "audioStopHotkeysMigrated":if(member.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)audioStopMigrated=member.Value.GetBoolean();break;
                case "theme": if (TryReadInt(member.Value, out var value)) theme = (ThemePreference)value; break;
                case "viewMode": if (TryReadInt(member.Value, out var mode)) viewMode = (WorkspaceViewMode)mode; break;
                case "globalTimingHotkeys":if(member.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)globalTiming=member.Value.GetBoolean();break;
                case "hotkeyOverrides":
                    if(member.Value.ValueKind==JsonValueKind.Array)
                        foreach(var item in member.Value.EnumerateArray())
                            try { if(JsonSerializer.Deserialize(item,SettingsJsonContext.Default.HotkeyOverride) is {} binding)hotkeys.Add(binding); }
                            catch(JsonException){ /* Keep other recoverable settings/bindings. */ }
                    break;
                case "mainSplitRatio": ratio = ReadNumber(member.Value); break;
                case "gridHeight": height = ReadNumber(member.Value); break;
                case "playbackVolume":volume=ReadNumber(member.Value);break;
                case "playbackMuted":if(member.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)muted=member.Value.GetBoolean();break;
                case "audioDisplayHeight":audioHeight=ReadNumber(member.Value);break;
                case "audioIntensity":intensity=ReadNumber(member.Value);break;
                case "audioWindowSeconds":audioSpan=ReadNumber(member.Value);break;
                case "audioVolumeLinked":if(member.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)linked=member.Value.GetBoolean();break;
                case "audioSpectrum":if(member.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)audioSpectrum=member.Value.GetBoolean();break;
                case "audioAutoScroll":if(member.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)audioAutoScroll=member.Value.GetBoolean();break;
                case "audioAutoCommit":if(member.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)audioAutoCommit=member.Value.GetBoolean();break;
                case "audioAutoNext":if(member.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)audioAutoNext=member.Value.GetBoolean();break;
                case "audioDefaultDuration":if(TryReadInt(member.Value,out var duration))audioDefaultDuration=duration;break;
                case "audioLeadIn":if(TryReadInt(member.Value,out var leadIn))audioLeadIn=leadIn;break;
                case "audioLeadOut":if(TryReadInt(member.Value,out var leadOut))audioLeadOut=leadOut;break;
                case "automationAutoloadDirectories":autoload=ReadPaths(member.Value);break;
                case "automationIncludeDirectories":includes=ReadPaths(member.Value);break;
                case "styleSplitWeights":if(member.Value.ValueKind==JsonValueKind.Array)styleSplit=member.Value.EnumerateArray().Take(4).Select(ReadNumber).ToArray();break;
                case "compactGridColumnWidths":if(member.Value.ValueKind==JsonValueKind.Array)compact=member.Value.EnumerateArray().Take(8).Select(ReadNumber).ToArray();break;
                case "gridColumnWidths":
                    if (member.Value.ValueKind == JsonValueKind.Array) widths = member.Value.EnumerateArray().Take(9).Select(ReadNumber).ToArray();
                    break;
                case "recentKeyframes":recentKeyframes=ReadPaths(member.Value);break;
                case "recentVideo":recentVideo=ReadPaths(member.Value);break;
                case "recentAudio":recentAudio=ReadPaths(member.Value);break;
                case "recentTimecodes":recentTimecodes=ReadPaths(member.Value);break;
                case "recentFiles":
                    if (member.Value.ValueKind == JsonValueKind.Array) recent = member.Value.EnumerateArray().Take(24).Select(v => v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "").ToArray();
                    break;
                default: future[member.Name] = member.Value.Clone(); break;
            }
        }
        return new AppSettings { AssDrawExecutable=assDrawExecutable,Theme = theme, ViewMode = viewMode, SchemaVersion = schema, MainSplitRatio = ratio, GridHeight = height,
            AutosaveEnabled=autosaveEnabled,AutosaveIntervalSeconds=autosaveInterval,RecoveryDirectory=recoveryDirectory,
            HotkeyOverrides=hotkeys.ToArray(),AudioStopHotkeysMigrated=audioStopMigrated,GlobalTimingHotkeys=globalTiming,ToolbarVisible=toolbar,GridTagDisplay=tagDisplay,SearchPreferences=search,SelectionPreferences=selection,VideoAutoSeek=videoAutoSeek,VideoOverscan=videoOverscan,LastDummyVideo=lastDummy,ShiftPreferences=shift,ShiftHistory=shiftHistory.ToArray(),
            GridColumnWidths = widths!, RecentFiles = recent!, RecentKeyframes=recentKeyframes, RecentTimecodes=recentTimecodes, RecentVideo=recentVideo, RecentAudio=recentAudio, FutureSettings = future,ShowOriginal=showOriginal,PasteOverFields=pasteOverFields,
            PlaybackVolume=volume,PlaybackMuted=muted,AudioDisplayHeight=audioHeight,AudioIntensity=intensity,AudioWindowSeconds=audioSpan,AudioVolumeLinked=linked,AudioSpectrum=audioSpectrum,AudioAutoScroll=audioAutoScroll,
            AudioAutoCommit=audioAutoCommit,AudioAutoNext=audioAutoNext,AudioDefaultDuration=audioDefaultDuration,AudioLeadIn=audioLeadIn,AudioLeadOut=audioLeadOut,
            AutomationAutoloadDirectories=autoload,AutomationIncludeDirectories=includes,CompactGridColumnWidths=compact,StyleSplitWeights=styleSplit };
    }

    private static string[]? ReadPaths(JsonElement value) => value.ValueKind==JsonValueKind.Array
        ? value.EnumerateArray().Where(item=>item.ValueKind==JsonValueKind.String).Select(item=>item.GetString()!).ToArray():null;

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
