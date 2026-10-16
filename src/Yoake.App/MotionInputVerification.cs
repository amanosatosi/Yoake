using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Yoake.Core.Commands;
using Yoake.Core.Media;
using Yoake.UI;
using Yoake.UI.ViewModels;

namespace Yoake.App;

// One short check in the existing packaged authoring launch: real FFMS2 video,
// shipping window/buttons, mode equivalence, Apply and exact one-step history.
internal sealed class MotionInputVerification(MainWindow window, MainWindowViewModel model, string fixtures, Action<Control, string, double> capture)
{
    private int _stage;
    private Task<bool>? _loading, _opening, _tracking, _closing, _navigating, _seeking;
    private MotionTrackingWindow? _dialog;
    private string _original = "", _applied = "";
    private TrackingPoint[] _fast = [];
    private (int X, int Y) _renderedStart;
    public bool Tick()
    {
        switch (_stage++)
        {
            case 0:
                Invoke(CommandIds.SubtitleNew); Invoke(CommandIds.GridInsertAfter);
                model.Draft!.Text = @"{\pos(400,300)\an7\fs120\1c&HFF00FF&\future(opaque)}Track\Nမြန်မာ";
                model.Draft.Start = "0:00:00.00"; model.Draft.End = "0:00:01.60"; Invoke(CommandIds.EditCommit);
                model.ActiveEditor!.MarkSaved(); _original = model.ActiveEditor.Document.Serialize();
                _loading = model.OpenVideoAsync(Path.Combine(fixtures, "motion.avi")); return false;
            case 1:
                if (!_loading!.IsCompleted) { _stage--; return false; }
                Require(_loading.Result, "Synthetic tracking video must decode with the real provider.");
                _opening = model.Registry.InvokeAsync("video/motion-track", new(model.ActiveDocumentId)).AsTask(); return false;
            case 2:
                Require(!_opening!.IsCompleted || _opening.Result, "The tracking command failed before opening: " + model.SubtitleStatus);
                if (model.MotionTracking is null || model.MotionTracking.Busy) { _stage--; return false; }
                _dialog = window.OwnedWindows.OfType<MotionTrackingWindow>().Single();
                Require(_dialog.IsVisible && Named<Control>("MotionTrackPreview") is { Bounds.Width: > 100, Bounds.Height: > 80 }, "Tracking must show a usable actual preview window.");
                Invoke("motion-track/pattern", new TrackingRegion(40, 45, 20, 20, 64, 64));
                capture(_dialog, "motion-pattern-and-search", 1);
                _tracking = model.Registry.InvokeAsync("motion-track/forward", new(model.ActiveDocumentId)).AsTask(); return false;
            case 3:
                if (!_tracking!.IsCompleted) { _stage--; return false; }
                Require(_tracking.Result && model.MotionTracking!.CanApply, "Fast tracking must cover the actual video range.");
                _fast = model.MotionTracking.Points.ToArray();
                Require(Math.Abs(_fast[^1].X - 58) < .5 && Math.Abs(_fast[^1].Y - 54) < .5, "Measured native frames must track the known 18px,9px displacement.");
                _navigating=model.Registry.InvokeAsync("motion-track/frame",new(model.ActiveDocumentId),0).AsTask();return false;
            case 4:
                if (!_navigating!.IsCompleted) { _stage--; return false; }
                Require(_navigating.Result,"Frame inspection must finish before restarting the track.");
                Invoke("motion-track/reset"); Invoke("motion-track/viewable", true);
                _tracking = model.Registry.InvokeAsync("motion-track/forward", new(model.ActiveDocumentId)).AsTask(); return false;
            case 5:
                if (!_tracking!.IsCompleted) { _stage--; return false; }
                Require(_tracking.Result && _fast.SequenceEqual(model.MotionTracking!.Points), "Viewable and Fast must produce exactly the same measured results.");
                capture(_dialog!, "motion-tracked-path", 1);
                var apply = Named<Button>("MotionTrack_apply");
                Require(apply.IsEnabled, "The actual Apply button must be available after tracking.");
                apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); return false;
            case 6:
                if (!_opening!.IsCompleted) { _stage--; return false; }
                Require(_opening.Result && model.MotionTracking is null, "Apply must close and release the transient tracking session.");
                _applied = model.ActiveEditor!.Document.Serialize();
                Require(_applied != _original && model.SelectedEvent!.Text.Contains(@"\pos(~+", StringComparison.Ordinal) && !model.SelectedEvent.Text.Contains(@"\frz", StringComparison.Ordinal), "GUI Apply must generate position-only relative motion.");
                _seeking = model.Registry.InvokeAsync("video/seek/frame", new(model.ActiveDocumentId), 0).AsTask(); return false;
            case 7:
                if (!_seeking!.IsCompleted || model.DisplayedPreviewRevision != model.PreviewRevision) { _stage--; return false; }
                Require(_seeking.Result, "The main timeline must seek the applied reference frame.");
                _renderedStart = ColoredPosition();
                _seeking = model.Registry.InvokeAsync("video/seek/frame", new(model.ActiveDocumentId), 7).AsTask(); return false;
            case 8:
                if (!_seeking!.IsCompleted || model.DisplayedPreviewRevision != model.PreviewRevision) { _stage--; return false; }
                Require(_seeking.Result, "The main timeline must seek the applied moving frame.");
                var end = ColoredPosition();
                Require(Math.Abs(end.X - _renderedStart.X - 14) <= 1 && Math.Abs(end.Y - _renderedStart.Y - 7) <= 1,
                    $"Mangetsu must render the measured 14px-right/7px-down movement; observed {end.X - _renderedStart.X},{end.Y - _renderedStart.Y}.");
                capture(window, "motion-applied-mangetsu", 1);
                Invoke(CommandIds.EditUndo); Require(model.ActiveEditor.Document.Serialize() == _original, "One GUI Undo must restore exact original ASS.");
                Invoke(CommandIds.EditRedo); Require(model.ActiveEditor.Document.Serialize() == _applied, "One GUI Redo must restore applied motion.");
                Invoke(CommandIds.EditUndo); model.ActiveEditor.MarkSaved();
                _closing = model.CloseDocumentAsync(model.ActiveDocumentId!.Value); return false;
            default:
                if (!_closing!.IsCompleted) { _stage--; return false; }
                Require(_closing.Result, "The tracking fixture tab must close without changing another document."); return true;
        }
    }
    private (int X, int Y) ColoredPosition()
    {
        using var buffer = model.VideoFrame!.Lock();
        var pixels = new byte[buffer.RowBytes * buffer.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, pixels, 0, pixels.Length);
        var left = buffer.Size.Width; var top = buffer.Size.Height;
        for (var y = 0; y < buffer.Size.Height; y++)
            for (var x = 0; x < buffer.Size.Width; x++)
            {
                var offset = y * buffer.RowBytes + x * 4;
                if (pixels[offset] <= pixels[offset + 1] + 30 || pixels[offset + 2] <= pixels[offset + 1] + 30) continue;
                left = Math.Min(left, x); top = Math.Min(top, y);
            }
        Require(left < buffer.Size.Width && top < buffer.Size.Height, "The actual Mangetsu video preview must contain the colored subtitle.");
        return (left, top);
    }
    // MotionTrackingWindow creates controls in code, not XAML. FindControl()
    // requires an Avalonia name scope, so inspect the actual visual tree instead.
    private T Named<T>(string name) where T : Control =>
        _dialog!.GetVisualDescendants().OfType<T>().First(control => control.Name == name);

    private void Invoke(string command, object? parameter = null)
    {
        var action = model.Registry.InvokeAsync(command, new(model.ActiveDocumentId), parameter);
        Require(action.IsCompletedSuccessfully && action.Result, "Tracking verification command: " + command);
    }
    private static void Require(bool success, string message) { if (!success) throw new InvalidOperationException(message); }
}
