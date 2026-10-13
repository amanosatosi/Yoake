using System.Text.Json;
using System.Text.Json.Serialization;
using Yoake.Core.Hotkeys;
using Yoake.Core.Subtitles;

namespace Yoake.Core.Settings;

public enum ThemePreference
{
    System,
    Light,
    Dark,
}

public enum WorkspaceViewMode
{
    Default,
    Audio,
    Video,
    AudioVideo,
}

public sealed record AppSettings
{
    public double[] GridColumnWidths { get; init; } = [40,65,48,92,92,110,100,90];
    public string[] RecentFiles { get; init; } = [];
    public string[]? RecentKeyframes {get;init;}
    public string[]? RecentTimecodes {get;init;}
    public string[]? RecentVideo {get;init;}
    public string[]? RecentAudio {get;init;}
    public int SchemaVersion { get; init; } = 1;
    public ThemePreference Theme { get; init; } = ThemePreference.System;
    public WorkspaceViewMode ViewMode { get; init; } = WorkspaceViewMode.Default;
    public HotkeyOverride[]? HotkeyOverrides { get; init; }
    public bool AudioStopHotkeysMigrated {get;init;}
    public bool GlobalTimingHotkeys { get; init; }
    public bool? ToolbarVisible { get; init; }
    public GridTagDisplayMode? GridTagDisplay { get; init; }
    public SubtitleSearchPreferences? SearchPreferences {get;init;}
    public SubtitleSelectionOptions? SelectionPreferences {get;init;}
    public bool? VideoAutoSeek {get;init;}
    public bool VideoOverscan { get; init; }
    public bool ShowOriginal { get; init; }
    public bool? AutosaveEnabled { get; init; }
    public int? AutosaveIntervalSeconds { get; init; }
    public string? RecoveryDirectory { get; init; }
    public PasteOverFields? PasteOverFields { get; init; }
    public Yoake.Core.Media.DummyVideoOptions? LastDummyVideo { get; init; }
    public int? VideoFastJumpStep {get;init;}
    public SubtitleShiftPreferences? ShiftPreferences {get;init;}
    public SubtitleShiftHistoryEntry[]? ShiftHistory {get;init;}
    public double MainSplitRatio { get; init; } = 0.5;
    public double GridHeight { get; init; } = 230;
    public double[]? CompactGridColumnWidths {get;init;}
    public double? PlaybackVolume {get;init;}
    public bool PlaybackMuted {get;init;}
    public double? AudioDisplayHeight {get;init;}
    public double? AudioIntensity {get;init;}
    public double? AudioWindowSeconds {get;init;}
    public bool AudioVolumeLinked {get;init;}
    public bool? AudioSpectrum {get;init;}
    public bool? AudioAutoScroll {get;init;}
    public bool? AudioAutoCommit {get;init;}
    public bool? AudioAutoNext {get;init;}
    public int? AudioDefaultDuration {get;init;}
    public int? AudioLeadIn {get;init;}
    public int? AudioLeadOut {get;init;}
    public double[]? StyleSplitWeights {get;init;}
    public string[]? AutomationAutoloadDirectories { get; init; }
    public string[]? AutomationIncludeDirectories { get; init; }

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
        var viewMode = ViewMode is >= WorkspaceViewMode.Default and <= WorkspaceViewMode.AudioVideo ? ViewMode : WorkspaceViewMode.Default;
        if (viewMode != ViewMode) report?.Invoke("Unknown workspace view mode normalized to Default.");
        var ratio = double.IsFinite(MainSplitRatio) && MainSplitRatio > 0 && MainSplitRatio < 1 ? Math.Clamp(MainSplitRatio, 0.1, 0.9) : defaults.MainSplitRatio;
        var height = double.IsFinite(GridHeight) && GridHeight > 0 ? Math.Clamp(GridHeight, 100, 2000) : defaults.GridHeight;
        if (theme != Theme || ratio != MainSplitRatio || height != GridHeight)
            report?.Invoke("Invalid theme or workspace dimensions normalized.");
        return this with { GridColumnWidths = widths, RecentFiles = recent, RecentKeyframes=(NormalizePaths(RecentKeyframes)??[]).Take(12).ToArray(), RecentTimecodes=(NormalizePaths(RecentTimecodes)??[]).Take(12).ToArray(), RecentVideo=(NormalizePaths(RecentVideo)??[]).Take(12).ToArray(), RecentAudio=(NormalizePaths(RecentAudio)??[]).Take(12).ToArray(), Theme = theme, ViewMode = viewMode,
            HotkeyOverrides=HotkeyProfile.Normalize(AudioStopHotkeysMigrated?HotkeyOverrides:AegisubHotkeyDefaults.MigrateAudioStopOverrides(HotkeyOverrides), report),
            AudioStopHotkeysMigrated=true,
            ToolbarVisible=ToolbarVisible??true,
            AutosaveEnabled=AutosaveEnabled??true,
            AutosaveIntervalSeconds=AutosaveIntervalSeconds is >=1 and <=86400?AutosaveIntervalSeconds:60,
            RecoveryDirectory=string.IsNullOrWhiteSpace(RecoveryDirectory)?null:RecoveryDirectory,
            GridTagDisplay=GridTagDisplay is {} tags&&Enum.IsDefined(tags)?tags:GridTagDisplayMode.Simplified,
            SearchPreferences=SearchPreferences?.Normalize()??new SubtitleSearchPreferences(),
            SelectionPreferences=SelectionPreferences?.Normalize()??new SubtitleSelectionOptions(),
            VideoAutoSeek=VideoAutoSeek??true,VideoFastJumpStep=VideoFastJumpStep is >=1 and <=1000000?VideoFastJumpStep:10,
            ShiftPreferences=ShiftPreferences?.Normalize()??new SubtitleShiftPreferences(),
            ShiftHistory=(ShiftHistory??[]).Where(entry=>entry is not null&&entry.Preferences is not null&&entry.Document is {Length:<=512}&&entry.Rows is {Length:<=4096}).Take(50).Select(entry=>entry with{Preferences=entry.Preferences.Normalize()}).ToArray(),
            AutomationAutoloadDirectories=NormalizePaths(AutomationAutoloadDirectories), AutomationIncludeDirectories=NormalizePaths(AutomationIncludeDirectories),
            CompactGridColumnWidths=CompactGridColumnWidths is {Length:7} compact&&compact.All(w=>double.IsFinite(w)&&w>=24&&w<=600)?compact:null,
            StyleSplitWeights=StyleSplitWeights is {Length:3} split&&split.All(w=>double.IsFinite(w)&&w>0)?split.Select(w=>Math.Clamp(w,0.1,10)).ToArray():null,
            PlaybackVolume=PlaybackVolume is {} volume&&double.IsFinite(volume)?Math.Clamp(volume,0,1):0.8,
            AudioDisplayHeight=AudioDisplayHeight is {} audioHeight&&double.IsFinite(audioHeight)?Math.Clamp(audioHeight,160,1000):180,
            AudioIntensity=AudioIntensity is {} intensity&&double.IsFinite(intensity)?Math.Clamp(intensity,0.008,8):1,
            AudioWindowSeconds=AudioWindowSeconds is {} audioSpan&&double.IsFinite(audioSpan)?Math.Clamp(audioSpan,0.02,3600):20,
            AudioSpectrum=AudioSpectrum??false,AudioAutoScroll=AudioAutoScroll??true,
            AudioAutoCommit=AudioAutoCommit??false,AudioAutoNext=AudioAutoNext??true,
            AudioDefaultDuration=AudioDefaultDuration is >=10 and <=600000?AudioDefaultDuration:3000,
            AudioLeadIn=AudioLeadIn is >=0 and <=60000?AudioLeadIn:100,
            AudioLeadOut=AudioLeadOut is >=0 and <=60000?AudioLeadOut:350,
            MainSplitRatio = ratio, GridHeight = height, SchemaVersion = SchemaVersion > 0 ? SchemaVersion : defaults.SchemaVersion };
    }
    private static string[]? NormalizePaths(string[]? paths) => paths?.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(Yoake.Core.Media.DummyVideoOptions))]
[JsonSerializable(typeof(HotkeyOverride))]
[JsonSerializable(typeof(SubtitleSearchPreferences))]
[JsonSerializable(typeof(SubtitleSelectionOptions))]
[JsonSerializable(typeof(SubtitleShiftPreferences))]
[JsonSerializable(typeof(SubtitleShiftHistoryEntry))]
internal partial class SettingsJsonContext : JsonSerializerContext
{
}
