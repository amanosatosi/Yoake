using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Yoake.Core.Commands;
using Yoake.Core.Media;
using Yoake.UI.ViewModels;

namespace Yoake.UI;

public sealed class MotionTrackingWindow : Window
{
    private readonly MotionTrackingSession _session;
    private readonly TrackingPreview _preview;
    private readonly Slider _frames;
    private readonly TextBlock _status = new(), _frameLabel = new();
    private readonly NumericUpDown _patternWidth, _patternHeight, _searchWidth, _searchHeight;
    private readonly CheckBox _viewable = new() { Content = "Viewable (show each frame)", IsChecked = false };
    private bool _updating;
    public MotionTrackingWindow(MainWindowViewModel model)
    {
        _session = model.MotionTracking ?? throw new InvalidOperationException("No tracking session.");
        Name = "MotionTrackingWindow"; Title = "Motion tracking · Position"; Width = 900; Height = 650; MinWidth = 620; MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var names = new NameScope();
        NameScope.SetNameScope(this, names);
        var root = new Grid { RowDefinitions = new("Auto,*,Auto,Auto,Auto"), Margin = new(10), RowSpacing = 6 };
        root.Children.Add(new TextBlock { Text = "Drag a rectangle around the pattern. Click to correct its center. Blue = pattern · Orange = search area", TextWrapping = TextWrapping.Wrap });
        _preview = new TrackingPreview(_session, region => model.Actions["motion-track/pattern"].Execute(region));
        _preview.Name = "MotionTrackPreview";
        names.Register(_preview.Name!, _preview);
        Grid.SetRow(_preview, 1); root.Children.Add(_preview);
        var navigation = new Grid { ColumnDefinitions = new("Auto,*,Auto,Auto"), ColumnSpacing = 6 };
        var previous = new Button { Content = "Previous" };
        previous.Click += (_, _) => model.Actions["motion-track/frame"].Execute(_session.CurrentFrame - 1);
        // These navigation buttons supply a relative frame as input plumbing;
        // frame inspection itself is owned by the same registered command.
        navigation.Children.Add(previous);
        _frames = new Slider { Minimum = _session.FirstFrame, Maximum = _session.LastFrame, TickFrequency = 1, IsSnapToTickEnabled = true };
        _frames.PropertyChanged += (_, e) => { if (!_updating && e.Property == Slider.ValueProperty) model.Actions["motion-track/frame"].Execute((int)Math.Round(_frames.Value)); };
        Grid.SetColumn(_frames, 1); navigation.Children.Add(_frames);
        var next = new Button { Content = "Next" }; next.Click += (_, _) => model.Actions["motion-track/frame"].Execute(_session.CurrentFrame + 1);
        Grid.SetColumn(next, 2); navigation.Children.Add(next); Grid.SetColumn(_frameLabel, 3); navigation.Children.Add(_frameLabel);
        Grid.SetRow(navigation, 2); root.Children.Add(navigation);
        var settings = new WrapPanel { Orientation = Orientation.Horizontal };
        _patternWidth = Number("Pattern W", _session.Region.Width, 8, 256, settings);
        _patternHeight = Number("H", _session.Region.Height, 8, 256, settings);
        _searchWidth = Number("Search W", _session.Region.SearchWidth, 12, 512, settings);
        _searchHeight = Number("H", _session.Region.SearchHeight, 12, 512, settings);
        settings.Children.Add(new TextBlock { Text = "Fast by default · ", VerticalAlignment = VerticalAlignment.Center, Margin = new(8, 0, 0, 0) });
        settings.Children.Add(_viewable);
        foreach (var number in new[] { _patternWidth, _patternHeight, _searchWidth, _searchHeight })
            number.ValueChanged += (_, _) =>
            {
                if (_updating || _session.Busy) return;
                var point = _session.CurrentPoint;
                var region = _session.Region with { X = point?.X ?? _session.Region.X, Y = point?.Y ?? _session.Region.Y,
                    Width = (int)(_patternWidth.Value ?? 32), Height = (int)(_patternHeight.Value ?? 32), SearchWidth = (int)(_searchWidth.Value ?? 96), SearchHeight = (int)(_searchHeight.Value ?? 96) };
                model.Actions["motion-track/pattern"].Execute(region);
            };
        _viewable.IsCheckedChanged += (_, _) => { if (!_updating) model.Actions["motion-track/viewable"].Execute(_viewable.IsChecked == true); };
        Grid.SetRow(settings, 3); root.Children.Add(settings);
        var footer = new StackPanel { Spacing = 6 };
        _status.TextWrapping = TextWrapping.Wrap; footer.Children.Add(_status);
        var actions = new WrapPanel();
        foreach (var (label, id) in new[] { ("Set reference", "reference"), ("Track backward", "backward"), ("Back 1 frame", "backward-frame"), ("Forward 1 frame", "forward-frame"), ("Track forward", "forward"), ("Stop", "stop"), ("Reset", "reset"), ("Apply position", "apply") })
        {
            var button = new Button { Name = "MotionTrack_" + id, Content = label, Command = model.Actions["motion-track/" + id], Margin = new(0, 0, 6, 0), Padding = new(8, 3) };
            names.Register(button.Name!, button);
            actions.Children.Add(button);
        }
        var close = new Button { Content = "Close", IsCancel = true }; close.Click += (_, _) => Close(); actions.Children.Add(close);
        footer.Children.Add(actions); Grid.SetRow(footer, 4); root.Children.Add(footer); Content = root;
        names.Complete();
        _session.PropertyChanged += SessionChanged; model.MotionApplied += Applied; model.Registry.CommandFailed += CommandFailed;
        Closed += (_, _) => { _session.Stop(); _session.PropertyChanged -= SessionChanged; model.MotionApplied -= Applied; model.Registry.CommandFailed -= CommandFailed; };
        Refresh();
    }
    private static NumericUpDown Number(string label, int value, int minimum, int maximum, Panel panel)
    {
        panel.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new(4, 0) });
        var number = new NumericUpDown { Value = value, Minimum = minimum, Maximum = maximum, Increment = 2, Width = 70, FormatString = "0", Padding = new(3, 0) };
        panel.Children.Add(number); return number;
    }
    private void SessionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Refresh();
    private void CommandFailed(object? sender, CommandFailureEventArgs e)
    {
        if (e.CommandId.StartsWith("motion-track/", StringComparison.Ordinal)) _status.Text = e.Exception.Message;
    }
    private void Applied(object? sender, EventArgs args) => Close();
    private void Refresh()
    {
        _updating = true;
        try
        {
            _frames.Value = _session.CurrentFrame; _frames.IsEnabled = !_session.Busy;
            _frameLabel.Text = $"Frame {_session.CurrentFrame} / {_session.LastFrame} · ref {_session.ReferenceFrame}";
            _status.Text = _session.Status;
            _patternWidth.Value = _session.Region.Width; _patternHeight.Value = _session.Region.Height;
            _searchWidth.Value = _session.Region.SearchWidth; _searchHeight.Value = _session.Region.SearchHeight;
            foreach (var control in new Control[] { _patternWidth, _patternHeight, _searchWidth, _searchHeight, _viewable }) control.IsEnabled = !_session.Busy;
            _preview.InvalidateVisual();
        }
        finally { _updating = false; }
    }

    private sealed class TrackingPreview(MotionTrackingSession session, Action<TrackingRegion> commit) : Control
    {
        private Point? _start;
        private TrackingRegion? _drag;
        private Rect ImageRect
        {
            get
            {
                var height = Math.Min(Bounds.Height, Bounds.Width / session.AspectRatio);
                var width = height * session.AspectRatio;
                return new((Bounds.Width - width) / 2, (Bounds.Height - height) / 2, width, height);
            }
        }
        private Point ImagePoint(Point point)
        {
            var rect = ImageRect;
            return new(Math.Clamp((point.X - rect.X) * session.Width / Math.Max(1, rect.Width), 0, session.Width),
                Math.Clamp((point.Y - rect.Y) * session.Height / Math.Max(1, rect.Height), 0, session.Height));
        }
        public override void Render(DrawingContext context)
        {
            base.Render(context); context.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
            var rect = ImageRect;
            if (session.Preview is {} image) context.DrawImage(image, new Rect(image.Size), rect);
            var current = session.CurrentPoint;
            var region = _drag ?? session.Region with { X = current?.X ?? session.Region.X, Y = current?.Y ?? session.Region.Y };
            Point Screen(double x, double y) => new(rect.X + x * rect.Width / session.Width, rect.Y + y * rect.Height / session.Height);
            using (context.PushClip(rect))
            {
                Point? last = null;
                foreach (var p in session.Points)
                {
                    var next = Screen(p.X, p.Y);
                    if (last is {} a) context.DrawLine(new Pen(Brushes.LimeGreen, 1), a, next);
                    last = next;
                }
                var center = Screen(region.X, region.Y);
                var search = new Rect(Screen(region.X - region.SearchWidth / 2d, region.Y - region.SearchHeight / 2d), Screen(region.X + region.SearchWidth / 2d, region.Y + region.SearchHeight / 2d));
                var pattern = new Rect(Screen(region.X - region.Width / 2d, region.Y - region.Height / 2d), Screen(region.X + region.Width / 2d, region.Y + region.Height / 2d));
                context.DrawRectangle(null, new Pen(Brushes.Orange, 2, DashStyle.Dash), search);
                context.DrawRectangle(null, new Pen(current is { Confidence: < .65 } ? Brushes.Red : Brushes.DeepSkyBlue, 2), pattern);
                context.DrawEllipse(Brushes.White, null, center, 3, 3);
            }
        }
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (session.Busy || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || !ImageRect.Contains(e.GetPosition(this))) return;
            _start = ImagePoint(e.GetPosition(this)); e.Pointer.Capture(this); e.Handled = true;
        }
        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e); if (_start is not {} start) return;
            var end = ImagePoint(e.GetPosition(this));
            var width = Math.Clamp((int)Math.Abs(end.X - start.X), 8, 256); var height = Math.Clamp((int)Math.Abs(end.Y - start.Y), 8, 256);
            _drag = session.Region with { X = Math.Clamp((start.X + end.X) / 2, width / 2d, session.Width - width / 2d), Y = Math.Clamp((start.Y + end.Y) / 2, height / 2d, session.Height - height / 2d),
                Width = width, Height = height, SearchWidth = Math.Max(width + 4, session.Region.SearchWidth), SearchHeight = Math.Max(height + 4, session.Region.SearchHeight) };
            InvalidateVisual();
        }
        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e); if (_start is not {} start) return;
            var region = _drag ?? session.Region with { X = Math.Clamp(start.X, session.Region.Width / 2d, session.Width - session.Region.Width / 2d), Y = Math.Clamp(start.Y, session.Region.Height / 2d, session.Height - session.Region.Height / 2d) };
            _start = null; _drag = null; e.Pointer.Capture(null); commit(region); InvalidateVisual(); e.Handled = true;
        }
        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { base.OnPointerCaptureLost(e); _start = null; _drag = null; InvalidateVisual(); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _start is not null) { _start = null; _drag = null; InvalidateVisual(); e.Handled = true; return; }
            base.OnKeyDown(e);
        }
    }
}
