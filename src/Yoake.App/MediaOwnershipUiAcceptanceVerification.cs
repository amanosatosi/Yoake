using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using Yoake.Core.Commands;
using Yoake.Core.Logging;
using Yoake.Core.Settings;
using Yoake.Core.Subtitles;
using Yoake.Native;
using Yoake.UI;
using Yoake.UI.Controls;
using Yoake.UI.ViewModels;

namespace Yoake.App;

internal sealed class MediaOwnershipUiAcceptanceVerification(IClassicDesktopStyleApplicationLifetime desktop,
    MainWindow window, MainWindowViewModel model, StartupOptions options)
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private bool _ready, _ticking; private int _stage, _waiting;
    private string _video = "", _audio = "", _source = "", _undo = "";
    private object? _samples, _draft, _selected, _editor;
    private Yoake.Core.Audio.AudioClipInterval? _pending;
    private Task<bool>? _opening, _racingVideo;
    public void Start() { window.LayoutUpdated += (_, _) => _ready = window.IsVisible && window.Bounds.Width > 0; _timer.Tick += Tick; _timer.Start(); }
    private async void Tick(object? sender, EventArgs args)
    {
        if (!_ready || _ticking) return; _ticking = true;
        try
        {
            switch (_stage++)
            {
                case 0:
                    Require(!model.Actions["video/close"].CanExecute(null), "Close requires video.");
                    var root = options.ProfileDirectory!;
                    _video = Path.Combine(root, "日本語 မြန်မာ video.avi"); _audio = Path.Combine(root, "独立 audio.avi");
                    var fixture = Path.Combine(options.VerificationMedia!, "av.avi"); File.Copy(fixture, _video); File.Copy(fixture, _audio);
                    VerifyNativeOwnership(fixture);
                    var subtitles = Path.Combine(root, "media-ownership.ass");
                    File.WriteAllText(subtitles, "[Script Info]\nPlayResX: 384\nPlayResY: 288\n[Events]\nFormat: " + string.Join(',', AssDocument.EventFormat) + "\nDialogue: 0,0:00:00.30,0:00:01.40,Default,A,0001,0002,0003,fx,{\\future(x)\\distort(1,2,3,4,5,6,7,8)}日本語 é\n");
                    Require(model.OpenSubtitle(subtitles), "Open lossless ownership fixture.");
                    System.ComponentModel.PropertyChangedEventHandler? published = null;
                    published = (_, change) =>
                    {
                        if(change.PropertyName != nameof(MainWindowViewModel.AudioSourcePath) || model.AudioSourcePath is null) return;
                        model.PropertyChanged -= published;
                        Require(model.WaveformSamples is null, "The video replacement starts before combined-open audio analysis finishes.");
                        _racingVideo = model.OpenVideoAsync(_video);
                    };
                    model.PropertyChanged += published;
                    _opening = model.OpenMediaAsync(_audio); return;
                case 1:
                    if (_opening?.IsCompleted != true) { _stage--; return; } Require(await _opening, "Real combined baseline opens once.");
                    Require(_racingVideo is not null && await _racingVideo && model.WaveformSamples is not null, "Video replacement retains in-flight audio peak publication.");
                    await Invoke(CommandIds.ViewAudioVideo); model.Draft!.Text += " stored checkpoint"; await Invoke(CommandIds.EditCommit); await Invoke("time/lead/out");
                    _editor = model.ActiveEditor; _source = model.ActiveEditor!.Document.Serialize(); _undo = model.ActiveEditor.Undo.NextUndoName ?? "";
                    model.Draft!.Layer = "unfinished"; model.Draft.Text += " retained draft";
                    _draft = model.Draft; _selected = model.SelectedEvent; _pending = model.PrimaryAudioRange; _samples = model.WaveformSamples;
                    await Invoke("video/open", _video); Require(model.VideoSourcePath == _video && model.AudioSourcePath == _audio, "Video Open preserves independent audio path.");
                    Unchanged();
                    var map = Path.Combine(options.ProfileDirectory!, "external.txt"); File.WriteAllText(map, "# timecode format v2\n" + string.Join('\n', Enumerable.Range(0, model.FrameTimes.Count).Select(i => i * 150)) + "\n");
                    await Invoke("timecode/open", map);
                    var cuts = Path.Combine(options.ProfileDirectory!, "cuts.txt"); File.WriteAllText(cuts, "# keyframe format v1\nfps 0\n0\n2\n"); await Invoke("keyframe/open", cuts);
                    Require(!await model.OpenVideoAsync(Path.Combine(options.ProfileDirectory!, "missing.mkv")), "Failed replacement does not publish.");
                    Require(model.VideoSourcePath == _video && model.AudioSourcePath == _audio, "Failed open retains both current providers."); Unchanged();
                    window.FindControl<MenuItem>("VideoMenu")!.IsSubMenuOpen = true; return;
                case 2:
                    Press(window.FindControl<MenuItem>("CloseVideoMenu")!, Key.Enter); return;
                case 3:
                    Require(model.VideoSourcePath is null && model.VideoFrame is null && model.AudioSourcePath == _audio, "Native Close releases only video.");
                    Require(!model.Actions["video/play"].CanExecute(null) && model.Actions["audio/play/current"].CanExecute(null), "Audio remains usable and Video-only commands disable."); Unchanged();
                    Press(window.FindControl<AudioWaveformControl>("AudioDisplay")!, Key.S); Require(model.IsPlaying && model.ActiveAudioPlayback is not null, "Actual focused Audio playback works after Video Close.");
                    Press(window.FindControl<AudioWaveformControl>("AudioDisplay")!, Key.H);
                    window.FindControl<MenuItem>("VideoMenu")!.IsSubMenuOpen = true; return;
                case 4: window.FindControl<MenuItem>("RecentVideoMenu")!.IsSubMenuOpen = true; return;
                case 5:
                    var recent = (MenuItem)window.FindControl<MenuItem>("RecentVideoMenu")!.Items[0]!;
                    Press(recent, Key.Enter); return;
                case 6:
                    if (model.VideoSourcePath != _video || model.VideoFrame is null) { Require(++_waiting < 100, "Recent Video must complete through real semantic open."); _stage--; return; }
                    Require(model.ExternalTimecodePath is not null && model.ExternalKeyframePath is not null && model.Keyframes.SequenceEqual(new[] { 0, 2 }), "External frame metadata survives close/reopen."); Unchanged();
                    var times = model.FrameTimes;
                    Require(await model.OpenAudioAsync(_video), "Audio replacement succeeds independently.");
                    Require(model.VideoSourcePath == _video && model.AudioSourcePath == _video && ReferenceEquals(times, model.FrameTimes), "Audio replacement retains the video/frame map.");
                    Require(!await model.OpenAudioAsync(Path.Combine(options.ProfileDirectory!, "missing-audio.wav")) && model.VideoSourcePath == _video && model.AudioSourcePath == _video && ReferenceEquals(times, model.FrameTimes), "Failed Audio replacement also retains both providers.");
                    Require(ReferenceEquals(_editor, model.ActiveEditor) && ReferenceEquals(_draft, model.Draft) && ReferenceEquals(_selected, model.SelectedEvent) && _pending == model.PrimaryAudioRange && _source == model.ActiveEditor!.Document.Serialize() && _undo == (model.ActiveEditor.Undo.NextUndoName ?? ""), "Audio replacement retains document state.");
                    var saved = new SettingsStore(Path.Combine(options.ProfileDirectory!, "settings.json")).Load();
                    Require(saved.RecentVideo!.SequenceEqual(new[] { _video, _audio }) && saved.RecentAudio!.SequenceEqual(new[] { _video, _audio }), "Independent Unicode MRUs reorder and persist.");
                    using (var canceled = new CancellationTokenSource())
                    { canceled.Cancel(); Require(!await model.Registry.InvokeAsync("video/open", new(model.ActiveDocumentId), _audio, canceled.Token), "Canceled request cannot replace providers."); }
                    var obsolete = model.OpenVideoAsync(_audio); var latest = model.OpenVideoAsync(_video);
                    Require(!await obsolete && await latest && model.VideoSourcePath == _video, "Superseded indexing cannot publish old video.");
                    Require(StartupDiagnostics.BindingWarningCount == 0 && StartupDiagnostics.FrameworkErrorCount == 0, "Ownership menus/layout must have no binding errors.");
                    _timer.Stop(); options.FinishVerification("PASS: separate real-window independent media ownership; native track transfer and release; Unicode Video Open; transactional failed and superseded replacement; real Close menu Enter; focused Audio S/H after Video Close; real Recent Video menu Enter through shared backend; Audio replacement retains Video; independent persisted MRUs; external timecodes/keyframes and document/draft/pending/selection/undo retention. Existing Video playback/EOF and visual acceptance run separately. Audible output remains manual QA.\n"); StartupDiagnostics.Complete(); desktop.Shutdown(0); return;
            }
        }
        catch (Exception exception) { _timer.Stop(); Program.Fatal("Packaged media ownership at stage " + (_stage - 1), exception); desktop.Shutdown(1); }
        finally { _ticking = false; }
    }
    private void Unchanged() => Require(ReferenceEquals(_editor, model.ActiveEditor) && ReferenceEquals(_samples, model.WaveformSamples) && ReferenceEquals(_draft, model.Draft) && ReferenceEquals(_selected, model.SelectedEvent) && _pending == model.PrimaryAudioRange && _source == model.ActiveEditor!.Document.Serialize() && _undo == (model.ActiveEditor.Undo.NextUndoName ?? ""), "Video changes preserve exact audio/document/draft/pending/selection/history.");
    private async Task Invoke(string id, object? parameter = null) => Require(await model.Registry.InvokeAsync(id, new(model.ActiveDocumentId), parameter), "Ownership preparation: " + id);
    private static void Press(Control control, Key key)
    {
        (TopLevel.GetTopLevel(control) as Window)?.Activate(); Require(control.Focus() && control.IsKeyboardFocusWithin, "Real ownership input owns focus: " + control.Name);
        control.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Source = control, Key = key });
    }
    private static void VerifyNativeOwnership(string path)
    {
        using var original = DocumentMedia.Open(path); var video = original.VideoProvider!; var audio = original.AudioProvider!;
        Require(video.HasVideo && !video.HasAudio && audio.HasAudio && !audio.HasVideo, "One index transfers native tracks into separate owners.");
        using var candidate = DocumentMedia.Open(path, tracks: MediaTracks.Video);
        var replacement = original.WithVideo(candidate); original.Dispose(); candidate.Dispose();
        Require(!video.HasVideo && ReferenceEquals(audio, replacement.AudioProvider) && audio.ReadAudioFrames(0, 32).FrameCount == 32, "Replacing video releases its native source and retains usable audio.");
        var closed = replacement.WithVideo(null); var replacedVideo = replacement.VideoProvider!; replacement.Dispose();
        Require(!replacedVideo.HasVideo && audio.ReadAudioFrames(0, 32).FrameCount == 32, "Closing video releases video and retains native audio.");
        closed.Dispose(); Require(!audio.HasAudio, "Final audio owner releases its native source.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
