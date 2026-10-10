using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yoake.Core.Commands;
using Yoake.Core.Logging;
using Yoake.UI;
using Yoake.UI.Controls;
using Yoake.UI.ViewModels;

namespace Yoake.App;

// Exercises the shipping window, compiled bindings, native platform and layout.
// It does not substitute a headless/fake window or claim GPU/device/input QA.
internal sealed class UiStartupVerification(IClassicDesktopStyleApplicationLifetime desktop,
    MainWindow window, MainWindowViewModel model, StartupOptions options)
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private int _ticks, _layouts;
    private bool _opened, _loaded, _inserted, _editorVerified;
    private StylesWindow? _styles;
    private AuthoringUiVerification? _authoring;
    private AutomationUiVerification? _automation;
    private bool _authoringCompleted;

    public void Start()
    {
        window.Opened += (_, _) => _opened = true;
        window.Loaded += (_, _) => _loaded = true;
        window.LayoutUpdated += (_, _) => _layouts++;
        _timer.Tick += Verify;
        _timer.Start();
    }

    private void Verify(object? sender, EventArgs args)
    {
        if (!_opened || !_loaded || _layouts == 0) return;
        try
        {
            if (!_inserted)
            {
                Require(model.Draft is null&&StartupDiagnostics.BindingWarningCount==0,"Initial empty document must produce no binding warnings.");
                Invoke(CommandIds.GridInsertAfter);
                _inserted = true;
                return; // Let real row containers/bindings and selection initialize.
            }
            if (++_ticks < 10) return;
            Require(window.IsVisible && window.IsLoaded && window.ClientSize.Width > 0 && window.ClientSize.Height > 0, "Window must be visible, loaded and have a nonzero client area.");
            Require(window.TryGetPlatformHandle() is not null, "Native window platform must exist.");
            Require(ReferenceEquals(window.DataContext, model) && model.ActiveEditor is not null, "Real document model must be attached.");
            var header = window.FindControl<Grid>("ColumnHeader")!;
            Require(header.ColumnDefinitions.Count == 8 && header.ColumnDefinitions.Take(7).All(c => c.ActualWidth > 0 && double.IsFinite(c.ActualWidth)), "Column headers must finish valid layout.");
            var rows = window.FindControl<ListBox>("SubtitleRows")!;
            Require(ReferenceEquals(rows.ItemsSource, model.Events) && (_authoring is null?model.Events.Count==1:model.Events.Count>=1), "Real grid ItemsSource must be bound.");
            Require(rows.SelectedItems?.Contains(model.SelectedEvent!) == true, $"Grid selection must contain the current row (events {model.Events.Count}, model selected {model.SelectedEvents.Count}, view selected {rows.SelectedItems?.Count}, current index {model.Events.ToList().IndexOf(model.SelectedEvent!)}).");
            Require(rows.GetVisualDescendants().OfType<Grid>().Any(g => g.Tag as string == "SubtitleRow" && g.Bounds.Height > 0), "A real subtitle row template must be realized and laid out.");
            Require(ReferenceEquals(window.FindControl<AudioWaveformControl>("AudioDisplay")?.Model, model), "Waveform model binding must initialize.");
            Require(ReferenceEquals(window.FindControl<VisualOverlayControl>("VisualOverlay")?.Model, model), "Visual overlay model binding must initialize.");
            Require(window.GetVisualDescendants().OfType<Button>().Count(b => b.Command is not null) >= 8, "Toolbar command bindings must initialize.");
            var text = window.FindControl<AssTextBox>("SubtitleText")!;
            var presenter = text.GetVisualDescendants().OfType<AssTextPresenter>().Single();
            if (!_editorVerified)
            {
                const string source = "{\\bord2\\1c&H88FF00&\\distort(0,0,1,1)}日本語 မြန်မာ é 👩‍👩‍👧‍👦\\NHello";
                text.SetCurrentValue(TextBox.TextProperty, source);
                Require(model.Draft?.Text == source, "Real ASS TextBox must update the edit draft without rewriting source.");
                Invoke(CommandIds.EditCommit);
                text.CaretIndex = source.Length;
                text.SelectionStart = source.Length - 5;
                text.SelectionEnd = source.Length;
                Invoke(CommandIds.FormatBold);
                Require(model.SelectedEvent!.Text.EndsWith("{\\b1}Hello{\\b0}", StringComparison.Ordinal), "Real selection formatting must preserve text and restore bold state.");
                Require(text.Text![Math.Min(text.SelectionStart,text.SelectionEnd)..Math.Max(text.SelectionStart,text.SelectionEnd)]=="Hello", "Native text selection must stay on formatted visible text.");
                Require(presenter.TextLayout.TextLines.Count > 0, "Tagged Unicode must shape through the ASS text presenter.");
                var formatted = text.Text;
                presenter.PreeditText = "にほんご";
                Require(presenter.TextLayout.TextLines.Count > 0, "IME preedit must use valid native text layout.");
                presenter.PreeditText = null;
                Require(text.Text == formatted, "IME preedit presentation must not modify the source.");
                Invoke(CommandIds.EditUndo);
                Require(model.SelectedEvent.Text == source, "Formatting undo must restore the exact tagged Unicode source.");
                Invoke(CommandIds.EditUndo); // Undo text entry; the inserted row remains for style preview.
                _editorVerified = true;
                return;
            }
            if (!_authoringCompleted)
            {
            if (_styles is null)
            {
                _styles = new StylesWindow(model.ActiveEditor!, model.StyleLibraryPath, model.SelectedEvent);
                _styles.Show(window);
                return; // Exercise the shipping inline style editor and background Mangetsu preview.
            }
            Require(_styles.Preview.LastError is null, "Mangetsu style preview failed: " + _styles.Preview.LastError);
            if (!_styles.Preview.HasCurrentFrame) return;
            Require(_styles.GetVisualDescendants().OfType<FontPicker>().Any(), "Style font picker must be realized.");
            Require(_styles.GetVisualDescendants().OfType<AssColorField>().Any(), "Style color controls must be realized.");
            Require(_styles.GetVisualDescendants().OfType<AssAlignmentPicker>().Any(), "Style alignment control must be realized.");
            _authoring??=new(window,model,_styles,options.VerificationReport!,options.VerificationMedia);
            if(!_authoring.Tick())return;
            _styles.Close();
            _authoringCompleted = true;
            }
            _automation ??= new(window, model, options.VerificationReport!, options.VerificationMedia!);
            if (!_automation.Tick()) return;
            Require(StartupDiagnostics.FrameworkErrorCount == 0, "Avalonia logged startup errors; inspect startup.log.");
            Require(StartupDiagnostics.BindingWarningCount==0,"Authoring controls logged binding warnings; inspect startup.log.");
            while(model.ActiveEditor!.Undo.CanUndo)Invoke(CommandIds.EditUndo); // Undo the real editing probe before normal shutdown.
            Require(!model.ActiveEditor!.IsDirty, "Startup probe must leave no unsaved document.");
            _timer.Stop();
            StartupDiagnostics.Checkpoint($"UI verified: opened, loaded, {_layouts} layouts, 10 dispatcher ticks, real row/selection/custom-control/command bindings, ASS editor, inline style controls and Mangetsu style preview");
            options.FinishVerification($"PASS: real MainWindow opened/visible/loaded; native platform; {_layouts} layouts; dispatcher responsive; real model, grid row/selection, custom controls and command bindings; ASS syntax reaches shaped runs in dark/light themes; normal/narrow field bounds; preserved formatting selection; inline style draft/library switching and latest Mangetsu preview; 100/125/150/200% render captures; mixed-script fixed rows and 20,000-event virtualization; standard style fields with persistent preview; full color spectrum/recent area; font browser; independent audio controls and panner; hidden caption/measured tab clearance; actual keyframe ruler drawing/snapping; visual-tool toggles/features, all vector subtools, legacy-to-eight-slot distort, pointer/key gestures, one-step multi-line undo, live/cancelled Mangetsu pixels, color strip input, live desktop magnifier/latch/cancel, linked audio levels and real sash persistence; no binding warnings or Avalonia startup errors.\n");
            StartupDiagnostics.Complete();
            desktop.Shutdown(0);
        }
        catch (Exception exception)
        {
            _timer.Stop();
            Program.Fatal("Real MainWindow startup verification", exception);
            desktop.Shutdown(1);
        }
    }

    private void Invoke(string id)
    {
        var result = model.Registry.InvokeAsync(id, new());
        Require(result.IsCompletedSuccessfully && result.Result, $"Startup command failed: {id}");
    }
    private static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
}
