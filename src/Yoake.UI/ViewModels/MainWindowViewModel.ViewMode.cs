using Yoake.Core.Commands;
using Yoake.Core.Settings;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed partial class MainWindowViewModel
{
    public WorkspaceViewMode ViewMode => _settings.ViewMode;
    public bool ShowAudio => ViewMode is WorkspaceViewMode.Audio or WorkspaceViewMode.AudioVideo;
    public bool ShowVideo => ViewMode is WorkspaceViewMode.Video or WorkspaceViewMode.AudioVideo;
    public bool IsDefaultView => ViewMode == WorkspaceViewMode.Default;
    public bool IsAudioView => ViewMode == WorkspaceViewMode.Audio;
    public bool IsVideoView => ViewMode == WorkspaceViewMode.Video;
    public bool IsAudioVideoView => ViewMode == WorkspaceViewMode.AudioVideo;

    private void RegisterViewCommands()
    {
        foreach (var (id, label, mode) in new[]
        {
            (CommandIds.ViewDefault, "Default", WorkspaceViewMode.Default),
            (CommandIds.ViewAudio, "Audio", WorkspaceViewMode.Audio),
            (CommandIds.ViewVideo, "Video", WorkspaceViewMode.Video),
            (CommandIds.ViewAudioVideo, "Audio + Video", WorkspaceViewMode.AudioVideo),
        })
        {
            _registry.Register(new AppCommand(new(id, label, "Select workspace view: " + label, "view"),
                (_, _) => { SelectViewMode(mode); return ValueTask.CompletedTask; }, isChecked: _ => ViewMode == mode));
            Actions[id] = new RegistryCommand(_registry, id, CurrentContext, hotkeys:()=>HotkeyProfile);
        }
    }

    private void SelectViewMode(WorkspaceViewMode mode)
    {
        if (ViewMode == mode) return;
        if(mode==WorkspaceViewMode.Video)FinishKaraokeGesture(false);
        // Presentation only: do not commit drafts, seek, or synchronize sessions.
        _settings = _settings with { ViewMode = mode };
        foreach (var name in new[] { nameof(ViewMode), nameof(ShowAudio), nameof(ShowVideo), nameof(ShowEmbeddedVideo),
            nameof(IsDefaultView), nameof(IsAudioView), nameof(IsVideoView), nameof(IsAudioVideoView) })
            OnPropertyChanged(name);
        _settingsStore.Save(_settings);
    }
}
