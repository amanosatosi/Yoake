using Yoake.Core.Audio;
using Yoake.Core.Commands;
using Yoake.Core.Subtitles;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed record KaraokeCutRequest(int Slot, int Position);
public sealed record KaraokePointerRequest(long Milliseconds, long Sensitivity, bool Right, bool Control);
public sealed record KaraokeTagRequest(string Tag, bool CurrentOnly);
public sealed partial class MainWindowViewModel
{
    private sealed record KaraokeEntry(ToshikiTimingPlan Plan, string StoredText, string StoredStart, string StoredEnd);
    private DocumentState? KaraokeState => _activeId is {} id ? _documents.GetValueOrDefault(id) : null;
    public bool IsKaraokeMode => KaraokeState?.KaraokeEnabled == true;
    public bool ShowKaraoke => IsKaraokeMode && HasAudio;
    public ToshikiTimingPlan? KaraokePlan => SelectedEvent is {} line && KaraokeState?.Karaoke.TryGetValue(line, out var entry) == true ? entry.Plan : null;
    public ToshikiSlot[] KaraokeSlots => ShowKaraoke ? KaraokePlan?.Slots ?? [] : [];
    public string KaraokeStatus => KaraokeState?.KaraokeStatus ?? "";
    public bool KaraokeAutoCut => _settings.KaraokeAutoCut ?? true;
    public string KaraokeTag { get => KaraokePlan?.Slots.FirstOrDefault()?.Tag ?? "k"; set { if (value is not null && value != KaraokeTag) Actions["karaoke/tag"].Execute(new KaraokeTagRequest(value, false)); } }
    public static readonly string[] KaraokeTags = ["k", "K", "kf", "ko", "kO"];
    private bool _publishingKaraoke;
    private ToshikiRecovery? _karaokeGesture;
    private int[] _karaokeDragMarkers = [];
    private int _karaokeDragMarker = -1;
    private bool KaraokeMatchesDraft => KaraokePlan is {} plan && (Draft?.Text ?? SelectedEvent?.Text) == plan.Source;
    private void EnsureKaraoke()
    {
        if (!IsKaraokeMode || SelectedEvent is not {} line || KaraokeState is not {} state || state.Karaoke.ContainsKey(line)) return;
        if (!ValidAudioTimingDraft()) { state.KaraokeStatus = "Correct the line's draft values before creating timing slots."; return; }
        AssTime.TryParse(Draft?.Start ?? line.Start, out var start); AssTime.TryParse(Draft?.End ?? line.End, out var end);
        var range = AudioTiming?.Get(line) ?? new AudioClipInterval(start, end);
        var plan = new ToshikiTimingPlan(Draft?.Text ?? line.Text, range.StartMilliseconds, range.EndMilliseconds, KaraokeAutoCut);
        state.Karaoke[line] = new(plan, line.Text, line.Start, line.End); state.KaraokeStatus = "Click the next boundary; click a timed slot to listen. Split or join text below.";
        if(plan.Changed){state.DraftRevision++;if(_workspace.ActiveDocument is {} session)session.IsDirty=true;}
    }
    private void EnsureKaraokeSafe()
    {try{EnsureKaraoke();}catch(ArgumentException error){if(KaraokeState is {} state)state.KaraokeStatus=error.Message;}}
    private void NotifyKaraoke(bool preview = true)
    {
        foreach (var name in new[] { nameof(IsKaraokeMode), nameof(ShowKaraoke), nameof(KaraokeSlots), nameof(KaraokeStatus), nameof(KaraokeTag), nameof(HasGesture) }) OnPropertyChanged(name);
        if (preview) { try { SelectedEvent?.ShowDraft(AudioPreviewValues()); } catch (ArgumentException) { } InvalidateSubtitlePreview(true); OnPropertyChanged(nameof(PreviewRevision)); }
        UpdateAudioPlaybackEnd(); _registry.NotifyStateChanged();
    }
    private void KaraokeChanged()
    {
        if (KaraokeState is {} state) { state.DraftRevision++; state.KaraokeStatus = "Uncommitted Toshiki K-Timing — Commit or Cancel."; }
        if (_workspace.ActiveDocument is {} session) session.IsDirty = true;
        _editBurstDelay?.Cancel(); _editBurstDelay?.Dispose(); _editBurstDelay = null; NotifyKaraoke();
    }
    private void KaraokeDraftChanged()
    {
        if(!IsKaraokeMode||KaraokeState is not {} state)return;
        if(KaraokePlan is {} plan&&!KaraokeMatchesDraft)
            state.KaraokeStatus="Text changed after cutting. Cancel the timing plan to cut the new draft; existing boundaries remain recoverable.";
        else if(KaraokePlan is null) { try { EnsureKaraoke(); } catch(ArgumentException error){state.KaraokeStatus=error.Message;} }
        NotifyKaraoke();
    }
    private IReadOnlyDictionary<string, string>? KaraokePreviewValues(IReadOnlyDictionary<string, string>? values)
    {
        if (!IsKaraokeMode || !KaraokeMatchesDraft || KaraokePlan is not {} plan || SelectedEvent is not {} line) return values;
        var merged = values is null ? line.FieldNames.ToDictionary(n => n, line.Get, StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        merged["Text"] = plan.PreviewText; merged["Start"] = AssTime.Format(plan.Start); merged["End"] = AssTime.Format(plan.End); return merged;
    }
    private void ReconcileKaraoke(DocumentState state)
    {
        if (_publishingKaraoke) return;
        foreach (var pair in state.Karaoke.ToArray())
        {
            var line = pair.Key; var entry = pair.Value;
            if (!state.Editor.Document.Events.Contains(line)) state.Karaoke.Remove(line);
            else if (line.Text != entry.StoredText || line.Start != entry.StoredStart || line.End != entry.StoredEnd)
            {
                // An ordinary draft publication rebases a matching slot plan.
                // Foreign text/timing edits (including undo) invalidate it.
                if (line.Text == entry.Plan.Source && line.StartMilliseconds == entry.Plan.Start && line.EndMilliseconds == entry.Plan.End)
                    state.Karaoke[line] = entry with { StoredText = line.Text, StoredStart = line.Start, StoredEnd = line.End };
                else state.Karaoke.Remove(line);
            }
        }
        if (_activeId is {} id && _documents.GetValueOrDefault(id) == state) NotifyKaraoke(false);
    }
    private void ResetKaraoke()
    {
        FinishKaraokeGesture(false); if (SelectedEvent is {} line) KaraokeState?.Karaoke.Remove(line);
        EnsureKaraoke(); if (KaraokePlan is {} plan) { var saved = plan.Capture(0) with { Changed = false }; ReplaceKaraoke(ToshikiTimingPlan.Restore(saved)); }
        if (KaraokeState is {} state) { state.DraftRevision++; state.KaraokeStatus = "Timing edits canceled; the editor draft is retained."; }
        if(_workspace.ActiveDocument is {} session)session.IsDirty=(ActiveEditor?.IsDirty??false)||(Draft?.IsChanged??false)||KaraokeState?.RecoveredUnsaved==true||KaraokeState?.Karaoke.Values.Any(e=>e.Plan.Changed)==true;
        NotifyKaraoke();
    }
    private void ReplaceKaraoke(ToshikiTimingPlan plan)
    { if (SelectedEvent is {} line && KaraokeState is {} state) state.Karaoke[line] = new(plan, line.Text, line.Start, line.End); }
    private void CommitKaraoke(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (KaraokePlan is not {} plan || SelectedEvent is not {} line || ActiveEditor is not {} editor || !KaraokeMatchesDraft) throw new ArgumentException("The text changed after timing began. Cancel the slot plan and cut the new text before committing.");
        if (!ValidAudioTimingDraft()) throw new ArgumentException("Correct invalid draft values before committing karaoke. Nothing was changed.");
        var values = new Dictionary<string, string>(Draft?.Values ?? line.FieldNames.ToDictionary(n => n, line.Get), StringComparer.OrdinalIgnoreCase);
        values["Text"] = plan.PreviewText; values["Start"] = AssTime.Format(plan.Start); values["End"] = AssTime.Format(plan.End);
        _publishingKaraoke = true;
        try { editor.CommitKaraoke(line, values, token); AudioTiming?.Clear(line); KaraokeState!.Karaoke.Remove(line); ReloadDraft(); EnsureKaraoke(); }
        finally { _publishingKaraoke = false; }
        KaraokeState!.KaraokeStatus = "Toshiki K-Timing committed. One Undo restores the whole operation."; NotifyKaraoke();
    }
    private void NavigateKaraoke(int delta)
    {
        if (KaraokePlan?.Navigate(delta) != true) { NavigateAudioTiming(delta); EnsureKaraoke(); }
        NotifyKaraoke(); PlayKaraoke(KaraokePlan?.PrimaryRange);
    }
    private void PlayKaraoke(AudioClipInterval? range)
    {
        if (range is not {} interval || interval.EndMilliseconds <= interval.StartMilliseconds || _media?.HasAudio != true) return;
        var info = _media.Info; var playback = AudioPlaybackPlan.Create(AudioPlaybackKind.Current, info.SampleRate, info.AudioSamples, interval, CurrentTimeSeconds);
        if (!playback.IsEmpty) _ = RunPlaybackAsync(null, playback, "audio/karaoke");
    }
    public bool BeginKaraokePointer(KaraokePointerRequest request)
    { InvokeGesture("karaoke/pointer/begin", request); return _karaokeGesture is not null; }
    public void MoveKaraokePointer(long milliseconds) => InvokeGesture("karaoke/pointer/move", milliseconds);
    public void EndKaraokePointer() => InvokeGesture("karaoke/pointer/end");
    private void BeginKaraokePointerCore(KaraokePointerRequest request)
    {
        var plan = KaraokePlan!;
        if (request.Right) { var slot = plan.AssignedAt(request.Milliseconds); if (request.Control && slot >= 0) { plan.Select(slot); NotifyKaraoke(); } else if (slot >= 0) PlayKaraoke(new(plan.Slots[slot].Start, plan.Slots[slot].End)); return; }
        var before = plan.Capture(0); var marker = plan.Nearby(request.Milliseconds, request.Sensitivity);
        if (marker < 0)
        {
            var recut = plan.RecutAt(request.Milliseconds);
            if (recut >= 0) { plan.MoveBoundary(recut, request.Milliseconds); KaraokeChanged(); return; }
            var slot = plan.AssignedAt(request.Milliseconds);
            if (slot >= 0) { PlayKaraoke(new(plan.Slots[slot].Start, plan.Slots[slot].End)); return; }
            if (!plan.Append(request.Milliseconds)) return; marker = plan.Boundaries.Count - 1;
        }
        var position = plan.Boundaries[marker]; _karaokeDragMarkers = Enumerable.Range(0, plan.Boundaries.Count).Where(i => plan.Boundaries[i] == position).ToArray();
        plan.Select(_karaokeDragMarkers[0] + 1); _karaokeDragMarker = _karaokeDragMarkers.Length == 1 ? marker : -1; _karaokeGesture = before; KaraokeChanged();
    }
    private void MoveKaraokePointerCore(long ms)
    {
        var plan = KaraokePlan!;
        if (_karaokeDragMarker < 0)
        {
            if (ms < plan.Boundaries[_karaokeDragMarkers[0]]) _karaokeDragMarker = _karaokeDragMarkers[0];
            else if (ms > plan.Boundaries[_karaokeDragMarkers[^1]]) _karaokeDragMarker = _karaokeDragMarkers[^1];
            else return;
        }
        plan.MoveBoundary(_karaokeDragMarker, ms); KaraokeChanged();
    }
    private void FinishKaraokeGesture(bool commit)
    {
        var before = _karaokeGesture; if (before is null) return; _karaokeGesture = null; _karaokeDragMarkers = []; _karaokeDragMarker = -1;
        if (!commit) ReplaceKaraoke(ToshikiTimingPlan.Restore(before));
        if(_workspace.ActiveDocument is {} session)session.IsDirty=(ActiveEditor?.IsDirty??false)||(Draft?.IsChanged??false)||KaraokeState?.RecoveredUnsaved==true||KaraokeState?.Karaoke.Values.Any(e=>e.Plan.Changed)==true;
        NotifyKaraoke(); GestureEnded?.Invoke(this, EventArgs.Empty);
    }
    private void RegisterKaraokeCommands()
    {
        bool Owner(CommandContext context) => _activeId is not null && (context.DocumentId is null || context.DocumentId == _activeId) && !HasEditOperation;
        bool Available() => HasAudio && IsKaraokeMode && KaraokePlan is not null && !HasGesture;
        void Register(string id, string label, Action<CommandInvocation, CancellationToken> action, Func<bool> available, Func<bool>? check = null, bool gesture = false)
        {
            _registry.Register(new(new(id, label, label, "Audio karaoke"), (invocation, token) => { token.ThrowIfCancellationRequested(); action(invocation, token); return ValueTask.CompletedTask; }, c => Owner(c) && (gesture || !HasGesture) && available(), check is null ? null : c => Owner(c) && check()));
            Actions[id] = new RegistryCommand(_registry, id, CurrentContext, hotkeys: () => HotkeyProfile);
        }
        Register("audio/karaoke", "Toshiki K-Timing", (_, _) =>
        {
            var old=IsKaraokeMode;KaraokeState!.KaraokeEnabled=!old;
            try{EnsureKaraoke();}catch{KaraokeState.KaraokeEnabled=old;NotifyKaraoke();throw;}
            _editBurstDelay?.Cancel();NotifyKaraoke();
            if(!IsKaraokeMode&&Draft?.IsChanged==true){var delay=new CancellationTokenSource();_editBurstDelay=delay;_=FinalizeBurstAsync(delay.Token);}
        }, () => HasAudio && SelectedEvent is not null, () => IsKaraokeMode);
        Register("karaoke/commit", "Commit karaoke", (_, token) => CommitKaraoke(token), () => Available() && KaraokeMatchesDraft && ValidAudioTimingDraft());
        Register("karaoke/cancel", "Cancel timing edits", (_, _) => ResetKaraoke(), Available);
        Register("karaoke/auto-cut", "Auto Cut", (_, _) => { _settings = _settings with { KaraokeAutoCut = !KaraokeAutoCut }; _settingsStore.Save(_settings); if (KaraokeAutoCut) KaraokePlan?.AutoCut(); OnPropertyChanged(nameof(KaraokeAutoCut)); KaraokeChanged(); }, Available, () => KaraokeAutoCut);
        Register("karaoke/split", "Split timing text", (i, _) => { if (i.Parameter is not KaraokeCutRequest p) throw new ArgumentException("Choose a text split."); KaraokePlan!.Split(p.Slot, p.Position); KaraokeChanged(); }, () => Available() && KaraokeMatchesDraft);
        Register("karaoke/join", "Join timing groups", (i, _) => { if (i.Parameter is not int index) throw new ArgumentException("Choose a timing boundary."); KaraokePlan!.Join(index); KaraokeChanged(); }, () => Available() && KaraokeMatchesDraft);
        Register("karaoke/select", "Select timing group", (i, _) => { if (i.Parameter is int index) KaraokePlan!.Select(index); NotifyKaraoke(); }, Available);
        Register("karaoke/tag", "Karaoke tag type", (i, _) => { if (i.Parameter is not KaraokeTagRequest p) throw new ArgumentException("Choose a karaoke tag type."); KaraokePlan!.SetTag(p.Tag, p.CurrentOnly); KaraokeChanged(); }, () => Available() && KaraokeMatchesDraft);
        Register("karaoke/pointer/begin", "Place or audition a boundary", (i, _) => { if (i.Parameter is KaraokePointerRequest p && p.Sensitivity >= 0) BeginKaraokePointerCore(p); }, () => Available() && KaraokeMatchesDraft);
        Register("karaoke/pointer/move", "Drag karaoke boundary", (i, _) => { try { if (i.Parameter is long ms) MoveKaraokePointerCore(ms); } catch { FinishKaraokeGesture(false); throw; } }, () => _karaokeGesture is not null, gesture: true);
        Register("karaoke/pointer/end", "Finish karaoke boundary", (_, _) => FinishKaraokeGesture(true), () => _karaokeGesture is not null, gesture: true);
    }
}
