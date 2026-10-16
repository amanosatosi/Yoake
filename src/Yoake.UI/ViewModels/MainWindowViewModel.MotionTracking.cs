using Yoake.Core.Commands;
using Yoake.Core.Subtitles;
using Yoake.Native;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed partial class MainWindowViewModel
{
    public MotionTrackingSession? MotionTracking { get; private set; }
    private Guid? _motionOwner;
    private IVideoProvider? _motionProvider;
    private AssEvent? _motionLine;
    private string _motionSource = "";
    private long _motionRevision;
    public event EventHandler? MotionApplied;
    private bool MotionOwnerCurrent => !_disposed && _motionOwner == _activeId && ReferenceEquals(_motionProvider, _media?.VideoProvider) && ReferenceEquals(_motionLine, SelectedEvent);
    private void RegisterMotionTrackingCommands()
    {
        void Register(string id, string label, Func<CommandInvocation, CancellationToken, ValueTask> execute, Func<bool> enabled)
        {
            _registry.Register(new(new(id, label, label, "Video"), execute,
                context => (context.DocumentId is null || context.DocumentId == _activeId) && enabled()));
            Actions[id] = new RegistryCommand(_registry, id, CurrentContext, hotkeys: () => HotkeyProfile);
        }
        Register("video/motion-track", "Track motion…", async (_, token) =>
        {
            if (!CommitDraft() || Dialogs is null || _media is null || SelectedEvent is not {} line) return;
            StopPlayback();
            _motionOwner = _activeId; _motionProvider = _media.VideoProvider; _motionLine = line;
            _motionSource = line.Text; _motionRevision = ActiveEditor!.Document.Revision;
            var session = new MotionTrackingSession(_media, FrameTimes, CurrentFrame, line.StartMilliseconds!.Value, line.EndMilliseconds!.Value, token);
            MotionTracking = session;
            session.PropertyChanged += MotionStateChanged; _registry.NotifyStateChanged();
            try
            {
                await session.NavigateAsync(session.CurrentFrame);
                token.ThrowIfCancellationRequested();
                if (MotionOwnerCurrent) await Dialogs.ShowMotionTrackingAsync(this, token);
            }
            finally
            {
                session.PropertyChanged -= MotionStateChanged;
                try { await session.CloseAsync(); }
                finally { MotionTracking = null; _motionOwner = null; _motionLine = null; _motionProvider = null; _registry.NotifyStateChanged(); }
            }
        }, () => MotionTracking is null && Dialogs is not null && !HasGesture && !IsKaraokeMode && _media?.VideoProvider is { HasVideo: true } && FrameTimes.Count > 1 && SelectedEvent is { IsComment: false, StartMilliseconds: {} start, EndMilliseconds: {} end } && end > start);
        bool Idle() => MotionOwnerCurrent && MotionTracking is { Busy: false };
        Register("motion-track/frame", "Inspect tracking frame", async (invocation, _) => { if (invocation.Parameter is int frame) await MotionTracking!.NavigateAsync(frame); }, Idle);
        Register("motion-track/pattern", "Adjust tracking pattern and search area", (invocation, _) =>
        {
            if (invocation.Parameter is Yoake.Core.Media.TrackingRegion region) MotionTracking!.SetRegion(region);
            return ValueTask.CompletedTask;
        }, Idle);
        Register("motion-track/reference", "Use current frame as reference", (_, _) => { MotionTracking!.Reset(); return ValueTask.CompletedTask; }, Idle);
        Register("motion-track/reset", "Reset tracking", (_, _) => { MotionTracking!.Reset(); return ValueTask.CompletedTask; }, Idle);
        Register("motion-track/forward", "Track forward", async (_, _) => await MotionTracking!.TrackAsync(1), Idle);
        Register("motion-track/backward", "Track backward", async (_, _) => await MotionTracking!.TrackAsync(-1), Idle);
        Register("motion-track/stop", "Stop tracking", (_, _) => { MotionTracking!.Stop(); return ValueTask.CompletedTask; }, () => MotionTracking is { Busy: true });
        Register("motion-track/viewable", "Viewable preview", (invocation, _) => { MotionTracking!.Viewable = invocation.Parameter is true; return ValueTask.CompletedTask; }, Idle);
        Register("motion-track/apply", "Apply position tracking", (_, token) =>
        {
            if (ActiveEditor!.Document.Revision != _motionRevision || Draft?.IsChanged == true) throw new InvalidOperationException("The subtitle document changed during tracking. Reopen motion tracking before applying.");
            var session = MotionTracking!; var size = ScriptSize;
            ActiveEditor.ApplyMotion(_motionLine!, _motionSource, session.Points, session.FrameTimes, session.ReferenceFrame,
                size.Width / session.Width, size.Height / session.Height, token);
            ReloadDraft(); MotionApplied?.Invoke(this, EventArgs.Empty); return ValueTask.CompletedTask;
        }, () => Idle() && MotionTracking!.CanApply);
    }
    private void MotionStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (!MotionOwnerCurrent) MotionTracking?.Stop();
        _registry.NotifyStateChanged();
    }
}
