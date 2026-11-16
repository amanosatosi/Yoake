using Yoake.Core.Settings;
using Yoake.Core.Hotkeys;

namespace Yoake.UI.ViewModels;

// The dialog owns its draft. Only changed preferences are merged: MRUs, layout,
// document state and preferences changed elsewhere while it is open survive.
public sealed partial class MainWindowViewModel
{
    private bool _publishingOptions;
    public AppSettings CaptureOptions() => _settings.Normalize();

    public async Task<string?> ApplyOptionsAsync(AppSettings baseline, AppSettings edited, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (_disposed) throw new ObjectDisposedException(nameof(MainWindowViewModel));
        static T Pick<T>(T before, T after, T current) => EqualityComparer<T>.Default.Equals(before, after) ? current : after;
        if(baseline.RecoveryDirectory!=edited.RecoveryDirectory&&!string.IsNullOrWhiteSpace(edited.RecoveryDirectory))
            _=Path.GetFullPath(edited.RecoveryDirectory,Path.GetDirectoryName(Path.GetFullPath(_settingsStore.Path))!);
        if((edited.AutomationAutoloadDirectories??[]).Concat(edited.AutomationIncludeDirectories??[]).Any(path=>path is null||path.Contains('\0')))
            throw new ArgumentException("Automation paths must not contain null characters.");
        var candidate = (_settings with
        {
            UiFontFamily=Pick(baseline.UiFontFamily, edited.UiFontFamily, _settings.UiFontFamily),
            UiFontSize=Pick(baseline.UiFontSize, edited.UiFontSize, _settings.UiFontSize),
            Theme=Pick(baseline.Theme, edited.Theme, _settings.Theme),
            ToolbarVisible=Pick(baseline.ToolbarVisible, edited.ToolbarVisible, _settings.ToolbarVisible),
            BetterView=Pick(baseline.BetterView, edited.BetterView, _settings.BetterView),
            ShowOriginal=Pick(baseline.ShowOriginal, edited.ShowOriginal, _settings.ShowOriginal),
            GridTagDisplay=Pick(baseline.GridTagDisplay, edited.GridTagDisplay, _settings.GridTagDisplay),
            AudioSpectrum=Pick(baseline.AudioSpectrum, edited.AudioSpectrum, _settings.AudioSpectrum),
            AudioAutoScroll=Pick(baseline.AudioAutoScroll, edited.AudioAutoScroll, _settings.AudioAutoScroll),
            AudioAutoCommit=Pick(baseline.AudioAutoCommit, edited.AudioAutoCommit, _settings.AudioAutoCommit),
            AudioAutoNext=Pick(baseline.AudioAutoNext, edited.AudioAutoNext, _settings.AudioAutoNext),
            AudioDefaultDuration=Pick(baseline.AudioDefaultDuration, edited.AudioDefaultDuration, _settings.AudioDefaultDuration),
            AudioLeadIn=Pick(baseline.AudioLeadIn, edited.AudioLeadIn, _settings.AudioLeadIn),
            AudioLeadOut=Pick(baseline.AudioLeadOut, edited.AudioLeadOut, _settings.AudioLeadOut),
            VideoAutoSeek=Pick(baseline.VideoAutoSeek, edited.VideoAutoSeek, _settings.VideoAutoSeek),
            VideoOverscan=Pick(baseline.VideoOverscan, edited.VideoOverscan, _settings.VideoOverscan),
            VideoFastJumpStep=Pick(baseline.VideoFastJumpStep, edited.VideoFastJumpStep, _settings.VideoFastJumpStep),
            AutosaveEnabled=Pick(baseline.AutosaveEnabled, edited.AutosaveEnabled, _settings.AutosaveEnabled),
            AutosaveIntervalSeconds=Pick(baseline.AutosaveIntervalSeconds, edited.AutosaveIntervalSeconds, _settings.AutosaveIntervalSeconds),
            RecoveryDirectory=Pick(baseline.RecoveryDirectory, edited.RecoveryDirectory, _settings.RecoveryDirectory),
            AssDrawExecutable=Pick(baseline.AssDrawExecutable, edited.AssDrawExecutable, _settings.AssDrawExecutable),
            GlobalTimingHotkeys=Pick(baseline.GlobalTimingHotkeys, edited.GlobalTimingHotkeys, _settings.GlobalTimingHotkeys),
            HotkeyOverrides=Pick(baseline.HotkeyOverrides, edited.HotkeyOverrides, _settings.HotkeyOverrides),
            AutomationAutoloadDirectories=Pick(baseline.AutomationAutoloadDirectories, edited.AutomationAutoloadDirectories, _settings.AutomationAutoloadDirectories),
            AutomationIncludeDirectories=Pick(baseline.AutomationIncludeDirectories, edited.AutomationIncludeDirectories, _settings.AutomationIncludeDirectories)
        }).Normalize();
        // Validate before writing or publishing any preference.
        var profile = new HotkeyProfile(AegisubHotkeyDefaults.ForYoake(), candidate.HotkeyOverrides);
        var recoveryDirectory = Path.GetFullPath(candidate.RecoveryDirectory ?? "recovery", Path.GetDirectoryName(Path.GetFullPath(_settingsStore.Path))!);
        var pathsChanged = !AutomationAutoloadDirectories.SequenceEqual(candidate.AutomationAutoloadDirectories ?? DefaultAutomationAutoloadDirectories)
            || !AutomationIncludeDirectories.SequenceEqual(candidate.AutomationIncludeDirectories ?? DefaultAutomationIncludeDirectories);
        var recoveryChanged = AutosaveEnabled != candidate.AutosaveEnabled || AutosaveIntervalSeconds != candidate.AutosaveIntervalSeconds || RecoveryDirectory != recoveryDirectory;
        _settingsStore.Save(candidate); // Atomic disk replace succeeds before any live state changes.
        _settings = candidate;
        _publishingOptions = true;
        try { HotkeyProfile.ReplaceOverrides(profile.Overrides); }
        finally { _publishingOptions = false; }
        _theme.Apply(candidate.Theme);
        UiFontResources.Apply(candidate.UiFontFamily, candidate.UiFontSize);
        if (recoveryChanged)
        {
            foreach (var id in _documents.Keys) InvalidateRecovery(id);
            _recoveryStore = new(recoveryDirectory);
            ConfigureRecoveryTimer();
        }
        foreach (var name in new[] { nameof(UiFontFamily), nameof(UiFontSize), nameof(ToolbarVisible), nameof(ToolbarRowHeight),
            nameof(BetterView), nameof(ShowOriginal), nameof(GridTagDisplay), nameof(IsAudioSpectrum), nameof(AudioDisplayModeIndex),
            nameof(AudioAutoScroll), nameof(AudioAutoCommit), nameof(AudioAutoNext), nameof(AudioTimingDefaultDuration),
            nameof(AudioTimingLeadIn), nameof(AudioTimingLeadOut), nameof(VideoAutoSeek), nameof(ShowVideoOverscan), nameof(VideoFastJumpStep),
            nameof(AutosaveEnabled), nameof(AutosaveIntervalSeconds), nameof(RecoveryDirectory), nameof(AssDrawExecutable),
            nameof(GlobalTimingHotkeys), nameof(AutomationMacros), nameof(AutomationAutoloadDirectories), nameof(AutomationIncludeDirectories) }) OnPropertyChanged(name);
        _registry.NotifyStateChanged();
        if (pathsChanged)
        {
            try
            {
                await RescanAutomationAsync(token);
                foreach (var item in _automationEntries.Where(e => e.DocumentId is not null).ToArray()) await ReloadAutomationScriptAsync(item, token);
            }
            catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException or OperationCanceledException)
            {
                return "Settings saved. Automation reload could not finish: "+error.Message;
            }
        }
        return null;
    }
}
