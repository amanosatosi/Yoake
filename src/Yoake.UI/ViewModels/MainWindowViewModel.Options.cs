using Yoake.Core.Settings;
using Yoake.Core.Hotkeys;

namespace Yoake.UI.ViewModels;

// The dialog owns its draft. Only changed preferences are merged: MRUs, layout,
// document state and preferences changed elsewhere while it is open survive.
public sealed partial class MainWindowViewModel
{
    private bool _publishingOptions;
    public bool AudioSnap=>_settings.AudioSnap??false;
    public int AudioSnapDistance=>_settings.AudioSnapDistance??8;
    public string AudioLabelFont=>_settings.AudioLabelFont??"Verdana";
    public SpectrumQualityMode SpectrumQuality=>_settings.SpectrumQuality??SpectrumQualityMode.Better;
    public SpectrumFrequencyMode SpectrumFrequency=>_settings.SpectrumFrequency??SpectrumFrequencyMode.Linear;
    public int SpectrumCacheMiB=>_settings.SpectrumCacheMiB??128;
    public bool VideoSliderKeyframes=>_settings.VideoSliderKeyframes??true;
    public bool VideoVisualAutohide=>_settings.VideoVisualAutohide??false;
    public VideoScrollAction VideoScroll=>_settings.VideoScroll??VideoScrollAction.Resize;
    public VideoScrollAction VideoCtrlScroll=>_settings.VideoCtrlScroll??VideoScrollAction.Zoom;
    public VideoScrollAction VideoShiftScroll=>_settings.VideoShiftScroll??VideoScrollAction.Resize;
    public bool AudioWheelZoom=>_settings.AudioWheelZoom??false;
    public bool AudioLockScroll=>_settings.AudioLockScroll??false;
    public bool AudioAutoFocus=>_settings.AudioAutoFocus??false;
    public int AudioMarkerSensitivity=>_settings.AudioMarkerSensitivity??8;
    public int AudioBoundaryThickness=>_settings.AudioBoundaryThickness??2;
    public AudioInactiveLineMode AudioInactiveLines=>_settings.AudioInactiveLines??AudioInactiveLineMode.All;
    public bool AudioInactiveComments=>_settings.AudioInactiveComments??false;
    public bool AudioDialogueKeyframes=>_settings.AudioDialogueKeyframes??true;
    public bool AudioKaraokeKeyframes=>_settings.AudioKaraokeKeyframes??true;
    public bool AudioCursorTime=>_settings.AudioCursorTime??true;
    public bool AudioVideoPosition=>_settings.AudioVideoPosition??true;
    public bool AudioSecondsBoundaries=>_settings.AudioSecondsBoundaries??true;
    public int AudioLabelSize=>_settings.AudioLabelSize??9;
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
            AudioSnap=Pick(baseline.AudioSnap,edited.AudioSnap,_settings.AudioSnap),
            AudioSnapDistance=Pick(baseline.AudioSnapDistance,edited.AudioSnapDistance,_settings.AudioSnapDistance),
            AudioLabelFont=Pick(baseline.AudioLabelFont,edited.AudioLabelFont,_settings.AudioLabelFont),
            SpectrumQuality=Pick(baseline.SpectrumQuality,edited.SpectrumQuality,_settings.SpectrumQuality),
            SpectrumFrequency=Pick(baseline.SpectrumFrequency,edited.SpectrumFrequency,_settings.SpectrumFrequency),
            SpectrumCacheMiB=Pick(baseline.SpectrumCacheMiB,edited.SpectrumCacheMiB,_settings.SpectrumCacheMiB),
            VideoSliderKeyframes=Pick(baseline.VideoSliderKeyframes,edited.VideoSliderKeyframes,_settings.VideoSliderKeyframes),
            VideoVisualAutohide=Pick(baseline.VideoVisualAutohide,edited.VideoVisualAutohide,_settings.VideoVisualAutohide),
            VideoScroll=Pick(baseline.VideoScroll,edited.VideoScroll,_settings.VideoScroll),
            VideoCtrlScroll=Pick(baseline.VideoCtrlScroll,edited.VideoCtrlScroll,_settings.VideoCtrlScroll),
            VideoShiftScroll=Pick(baseline.VideoShiftScroll,edited.VideoShiftScroll,_settings.VideoShiftScroll),
            AudioWheelZoom=Pick(baseline.AudioWheelZoom,edited.AudioWheelZoom,_settings.AudioWheelZoom),
            AudioLockScroll=Pick(baseline.AudioLockScroll,edited.AudioLockScroll,_settings.AudioLockScroll),
            AudioAutoFocus=Pick(baseline.AudioAutoFocus,edited.AudioAutoFocus,_settings.AudioAutoFocus),
            AudioMarkerSensitivity=Pick(baseline.AudioMarkerSensitivity,edited.AudioMarkerSensitivity,_settings.AudioMarkerSensitivity),
            AudioBoundaryThickness=Pick(baseline.AudioBoundaryThickness,edited.AudioBoundaryThickness,_settings.AudioBoundaryThickness),
            AudioInactiveLines=Pick(baseline.AudioInactiveLines,edited.AudioInactiveLines,_settings.AudioInactiveLines),
            AudioInactiveComments=Pick(baseline.AudioInactiveComments,edited.AudioInactiveComments,_settings.AudioInactiveComments),
            AudioDialogueKeyframes=Pick(baseline.AudioDialogueKeyframes,edited.AudioDialogueKeyframes,_settings.AudioDialogueKeyframes),
            AudioKaraokeKeyframes=Pick(baseline.AudioKaraokeKeyframes,edited.AudioKaraokeKeyframes,_settings.AudioKaraokeKeyframes),
            AudioCursorTime=Pick(baseline.AudioCursorTime,edited.AudioCursorTime,_settings.AudioCursorTime),
            AudioVideoPosition=Pick(baseline.AudioVideoPosition,edited.AudioVideoPosition,_settings.AudioVideoPosition),
            AudioSecondsBoundaries=Pick(baseline.AudioSecondsBoundaries,edited.AudioSecondsBoundaries,_settings.AudioSecondsBoundaries),
            AudioLabelSize=Pick(baseline.AudioLabelSize,edited.AudioLabelSize,_settings.AudioLabelSize),
            AudioAutoScroll=Pick(baseline.AudioAutoScroll, edited.AudioAutoScroll, _settings.AudioAutoScroll),
            AudioVolumeLinked=Pick(baseline.AudioVolumeLinked, edited.AudioVolumeLinked, _settings.AudioVolumeLinked),
            PlaybackMuted=Pick(baseline.PlaybackMuted, edited.PlaybackMuted, _settings.PlaybackMuted),
            KaraokeAutoCut=Pick(baseline.KaraokeAutoCut, edited.KaraokeAutoCut, _settings.KaraokeAutoCut),
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
            GlobalTimingHotkeys=Pick(baseline.GlobalTimingHotkeys, edited.GlobalTimingHotkeys, _settings.GlobalTimingHotkeys),
            HotkeyOverrides=Pick(baseline.HotkeyOverrides, edited.HotkeyOverrides, _settings.HotkeyOverrides),
            AutomationAutoloadDirectories=Pick(baseline.AutomationAutoloadDirectories, edited.AutomationAutoloadDirectories, _settings.AutomationAutoloadDirectories),
            AutomationIncludeDirectories=Pick(baseline.AutomationIncludeDirectories, edited.AutomationIncludeDirectories, _settings.AutomationIncludeDirectories)
        }).Normalize();
        if(candidate.AudioVolumeLinked)candidate=candidate with {PlaybackVolume=Yoake.Core.Audio.AudioViewportMath.LinkedVolume(candidate.AudioIntensity??1)};
        // Validate before writing or publishing any preference.
        var profile = new HotkeyProfile(AegisubHotkeyDefaults.ForYoake(), candidate.HotkeyOverrides);
        var recoveryDirectory = Path.GetFullPath(candidate.RecoveryDirectory ?? "recovery", Path.GetDirectoryName(Path.GetFullPath(_settingsStore.Path))!);
        var pathsChanged = !AutomationAutoloadDirectories.SequenceEqual(candidate.AutomationAutoloadDirectories ?? DefaultAutomationAutoloadDirectories)
            || !AutomationIncludeDirectories.SequenceEqual(candidate.AutomationIncludeDirectories ?? DefaultAutomationIncludeDirectories);
        var recoveryChanged = AutosaveEnabled != candidate.AutosaveEnabled || AutosaveIntervalSeconds != candidate.AutosaveIntervalSeconds || RecoveryDirectory != recoveryDirectory;
        _settingsStore.Save(candidate); // Atomic disk replace succeeds before any live state changes.
        _settings = candidate;
        _audioPlayer?.SetVolume(PlaybackVolume,PlaybackMuted);
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
            nameof(AudioVolumeLinked),nameof(IsVolumeIndependent),nameof(PlaybackVolume),nameof(PlaybackMuted),nameof(KaraokeAutoCut),
            nameof(AudioSnap),nameof(AudioSnapDistance),nameof(AudioLabelFont),nameof(SpectrumQuality),nameof(SpectrumFrequency),nameof(SpectrumCacheMiB),nameof(VideoSliderKeyframes),nameof(VideoVisualAutohide),nameof(VideoScroll),nameof(VideoCtrlScroll),nameof(VideoShiftScroll),
            nameof(AudioWheelZoom),nameof(AudioLockScroll),nameof(AudioAutoFocus),nameof(AudioMarkerSensitivity),nameof(AudioBoundaryThickness),nameof(AudioInactiveLines),nameof(AudioInactiveComments),nameof(AudioDialogueKeyframes),nameof(AudioKaraokeKeyframes),nameof(AudioCursorTime),nameof(AudioVideoPosition),nameof(AudioSecondsBoundaries),nameof(AudioLabelSize),
            nameof(AudioTimingLeadIn), nameof(AudioTimingLeadOut), nameof(VideoAutoSeek), nameof(ShowVideoOverscan), nameof(VideoFastJumpStep),
            nameof(AutosaveEnabled), nameof(AutosaveIntervalSeconds), nameof(RecoveryDirectory),
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
