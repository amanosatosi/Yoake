using Yoake.Core.Commands;
using Yoake.Core.Settings;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed partial class MainWindowViewModel
{
    // Media is document-owned; view selection is transient. Restoring a saved
    // Audio/Video choice without an open provider would expose empty tool panels.
    private WorkspaceViewMode _viewMode = WorkspaceViewMode.Default;
    public WorkspaceViewMode ViewMode => _viewMode;
    public bool ShowAudio => ViewMode is WorkspaceViewMode.Audio or WorkspaceViewMode.AudioVideo;
    public bool ShowVideo => ViewMode is WorkspaceViewMode.Video or WorkspaceViewMode.AudioVideo;
    public bool IsDefaultView => ViewMode == WorkspaceViewMode.Default;
    public bool IsAudioView => ViewMode == WorkspaceViewMode.Audio;
    public bool IsVideoView => ViewMode == WorkspaceViewMode.Video;
    public bool IsAudioVideoView => ViewMode == WorkspaceViewMode.AudioVideo;

    private bool CanSelectViewMode(WorkspaceViewMode mode) => mode switch
    {
        WorkspaceViewMode.Default => true,
        WorkspaceViewMode.Audio => _media?.HasAudio == true,
        WorkspaceViewMode.Video => _media?.HasVideo == true,
        WorkspaceViewMode.AudioVideo => _media?.HasAudio == true && _media?.HasVideo == true,
        _ => false
    };

    private WorkspaceViewMode AutomaticViewMode() =>
        (_media?.HasVideo == true, _media?.HasAudio == true) switch
        {
            (true, true) => WorkspaceViewMode.AudioVideo,
            (true, false) => WorkspaceViewMode.Video,
            (false, true) => WorkspaceViewMode.Audio,
            _ => WorkspaceViewMode.Default
        };

    // Call when a document or published provider changes. Failed, canceled or
    // superseded media opens must NOT move the view.
    private void ReconcileWorkspaceView() => ApplyViewMode(AutomaticViewMode());

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
                (_, _) => { SelectViewMode(mode); return ValueTask.CompletedTask; },
                _ => CanSelectViewMode(mode), isChecked: _ => ViewMode == mode));
            Actions[id] = new RegistryCommand(_registry, id, CurrentContext, hotkeys: () => HotkeyProfile);
        }
    }

    private void SelectViewMode(WorkspaceViewMode mode)
    {
        // Enforce the provider gate even for commands invoked without a menu.
        if (!CanSelectViewMode(mode)) return;
        ApplyViewMode(mode);
    }

    private void ApplyViewMode(WorkspaceViewMode mode)
    {
        if (ViewMode == mode) { _registry.NotifyStateChanged(); return; }
        if (mode == WorkspaceViewMode.Video) FinishKaraokeGesture(false);
        // Presentation only: retain edits, selection, timeline and media owners.
        // Do not persist a view that would be invalid after a fresh launch.
        _viewMode = mode;
        foreach (var name in new[] { nameof(ViewMode), nameof(ShowAudio), nameof(ShowVideo), nameof(ShowEmbeddedVideo),
            nameof(IsDefaultView), nameof(IsAudioView), nameof(IsVideoView), nameof(IsAudioVideoView) })
            OnPropertyChanged(name);
        _registry.NotifyStateChanged();
    }
}
