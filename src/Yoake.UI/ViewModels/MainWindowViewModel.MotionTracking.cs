using Yoake.Core.Commands;
using Yoake.Core.Subtitles;
using Yoake.Native;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public readonly record struct MotionApplyOptions(bool X, bool Y, bool Scale, bool Rotation, bool Optimize);

public sealed partial class MainWindowViewModel
{
    public MotionTrackingSession? MotionTracking { get; private set; }
    private Guid? _motionOwner;
    private IVideoProvider? _motionProvider;
    private IReadOnlyList<double>? _motionFrameTimes;
    private AssEvent? _motionLine;
    private (AssEvent Line, string Source, long Start, long End)[] _motionTargets = [];
    private long _motionRevision;
    public event EventHandler? MotionApplied;
    private bool MotionOwnerCurrent => !_disposed && _motionOwner == _activeId && ReferenceEquals(_motionProvider, _media?.VideoProvider) && ReferenceEquals(_motionFrameTimes, FrameTimes) && ReferenceEquals(_motionLine, SelectedEvent);
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
            var targets = SelectedEvents.Where(item => !item.IsComment).ToArray();
            if (!targets.Contains(line)) targets = [line];
            if (targets.Any(item => item.StartMilliseconds is not {} start || item.EndMilliseconds is not {} end || end <= start))
                throw new InvalidOperationException("Every selected dialogue needs a valid start and end time before tracking.");
            if (_media.VideoDurationSeconds > 0 && targets.Any(item => item.StartMilliseconds < 0 || item.EndMilliseconds > _media.VideoDurationSeconds * 1000 + 1))
                throw new InvalidOperationException("Selected dialogue extends outside the video. Adjust its timing before tracking.");
            StopPlayback();
            _motionOwner = _activeId; _motionProvider = _media.VideoProvider; _motionFrameTimes = FrameTimes; _motionLine = line;
            _motionRevision = ActiveEditor!.Document.Revision;
            _motionTargets = targets.Select(item => (item, item.Text, item.StartMilliseconds!.Value, item.EndMilliseconds!.Value)).ToArray();
            var session = new MotionTrackingSession(_media, FrameTimes, CurrentFrame, _motionTargets.Min(item => item.Start), _motionTargets.Max(item => item.End), token);
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
                finally { MotionTracking = null; _motionOwner = null; _motionLine = null; _motionTargets = []; _motionProvider = null; _motionFrameTimes = null; _registry.NotifyStateChanged(); }
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
        Register("motion-track/backward-frame", "Track one frame backward", async (_, _) => await MotionTracking!.TrackAsync(-1, singleFrame: true), Idle);
        Register("motion-track/forward-frame", "Track one frame forward", async (_, _) => await MotionTracking!.TrackAsync(1, singleFrame: true), Idle);
        Register("motion-track/stop", "Stop tracking", (_, _) => { MotionTracking!.Stop(); return ValueTask.CompletedTask; }, () => MotionTracking is { Busy: true });
        Register("motion-track/viewable", "Viewable preview", (invocation, _) => { MotionTracking!.Viewable = invocation.Parameter is true; return ValueTask.CompletedTask; }, Idle);
        Register("motion-track/model", "Tracking model", (invocation, _) => { if (invocation.Parameter is MotionModel model && MotionTracking!.Model != model) { MotionTracking.Model = model; MotionTracking.Reset(); } return ValueTask.CompletedTask; }, Idle);
        Register("motion-track/channel", "Tracking channel", (invocation, _) => { if (invocation.Parameter is bool clip && MotionTracking!.TrackClip != clip) { MotionTracking.TrackClip = clip; MotionTracking.Reset(); } return ValueTask.CompletedTask; }, Idle);
        Register("motion-track/options", "Advanced tracking apply options", (invocation, _) =>
        {
            if (invocation.Parameter is MotionApplyOptions options)
            {
                MotionTracking!.ApplyX = options.X; MotionTracking.ApplyY = options.Y; MotionTracking.ApplyScale = options.Scale;
                MotionTracking.ApplyRotation = options.Rotation; MotionTracking.Optimize = options.Optimize;
            }
            return ValueTask.CompletedTask;
        }, Idle);
        Register("motion-track/apply", "Apply motion tracking", async (_, token) =>
        {
            if (ActiveEditor!.Document.Revision != _motionRevision || Draft?.IsChanged == true) throw new InvalidOperationException("The subtitle document changed during tracking. Reopen motion tracking before applying.");
            var session = MotionTracking!; var size = ScriptSize; var editor = ActiveEditor;
            var targets = _motionTargets; var points = session.Points; var reference = session.ReferenceFrame;
            var model = session.Model; var clip = session.TrackClip;
            var options = new MotionApplyOptions(session.ApplyX, session.ApplyY, session.ApplyScale, session.ApplyRotation, session.Optimize);
            await session.ApplyAsync(async lifetime =>
            {
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime);
                var plans = await Task.Run(() => targets.Select(target => new SubtitleEditor.MotionPlan(target.Line, target.Source,
                    MotionTrackApply.Plan(target.Source, points, session.FrameTimes, reference, target.Start, target.End,
                        size.Width / session.Width, size.Height / session.Height, cancellation.Token,
                        applyScale: options.Scale && (model is MotionModel.PositionScale or MotionModel.PositionScaleRotation),
                        applyRotation: options.Rotation && (model is MotionModel.PositionRotation or MotionModel.PositionScaleRotation),
                        applyClip: clip, applyX: options.X, applyY: options.Y, optimize: options.Optimize))).ToArray(), cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (!MotionOwnerCurrent || !ReferenceEquals(ActiveEditor, editor) || editor.Document.Revision != _motionRevision || Draft?.IsChanged == true ||
                    !SelectedEvents.Where(item => !item.IsComment).SequenceEqual(targets.Select(item => item.Line)))
                    throw new InvalidOperationException("The subtitle document or video changed while preparing motion. Reopen motion tracking before applying.");
                editor.ApplyMotionPlans(plans, cancellation.Token);
                ReloadDraft(); MotionApplied?.Invoke(this, EventArgs.Empty);
            });
        }, () => Idle() && MotionTracking!.CanApply);
    }
    private void MotionStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (!MotionOwnerCurrent) MotionTracking?.Stop();
        _registry.NotifyStateChanged();
    }
}
