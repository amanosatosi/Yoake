using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Yoake.Core.Media;
using Yoake.Core.Subtitles;
using Yoake.UI.Controls;
using Yoake.UI.ViewModels;

namespace Yoake.UI;

internal static class VideoDialogInput
{
    public static void GuardIme(Window window)
    {
        window.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Escape)) return;
            if ((e.Source as Control)?.GetVisualAncestors().Prepend(e.Source as Control).OfType<TextBox>()
                .Any(input => input.GetVisualDescendants().OfType<TextPresenter>().Any(p => !string.IsNullOrEmpty(p.PreeditText))) == true) e.Handled = true;
        }, RoutingStrategies.Tunnel);
    }
    public static StackPanel Panel(Window window, string name, string title, double width)
    {
        window.Name = name; window.Title = title; window.Width = width; window.MinWidth = width;
        window.SizeToContent = SizeToContent.Height; window.CanResize = false; window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(12), Spacing = 8 }; window.Content = panel;
        NameScope.SetNameScope(window, new NameScope()); GuardIme(window); return panel;
    }
    public static void Name(Window window, Control control) => NameScope.GetNameScope(window)!.Register(control.Name!, control);
    public static void Buttons(Window window, StackPanel panel, string prefix, string confirm, Action accept)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Name = prefix + "Confirm", Content = confirm, IsDefault = true };
        var cancel = new Button { Name = prefix + "Cancel", Content = "Cancel", IsCancel = true };
        ok.Click += (_, _) => accept(); cancel.Click += (_, _) => window.Close(null);
        row.Children.Add(ok); row.Children.Add(cancel); panel.Children.Add(row); Name(window, ok); Name(window, cancel);
    }
}

public sealed class VideoAspectWindow : Window
{
    private readonly TextBox _value = new() { Name = "AspectValue" };
    private readonly TextBlock _error = new() { Name = "AspectError", TextWrapping = TextWrapping.Wrap };
    public VideoAspectWindow(double current)
    {
        var panel = VideoDialogInput.Panel(this, "VideoAspectDialog", "Custom aspect ratio", 390);
        panel.Children.Add(new TextBlock { Text = "Decimal (2.35), ratio (16:9 or 16/9),\nor resolution (853x480). Range: 0.5–5.0." });
        _value.Text = current.ToString("0.######", CultureInfo.InvariantCulture); panel.Children.Add(_value); panel.Children.Add(_error);
        VideoDialogInput.Name(this, _value); VideoDialogInput.Name(this, _error);
        void Validate() => _error.Text = VideoAspect.TryCustom(_value.Text, out _) ? "" : "Enter an aspect ratio between 0.5 and 5.0.";
        _value.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) Validate(); };
        VideoDialogInput.Buttons(this, panel, "Aspect", "Apply", () => { Validate(); if (VideoAspect.TryCustom(_value.Text, out _)) Close(_value.Text); });
        Opened += (_, _) => { _value.Focus(); _value.SelectAll(); };
    }
}

public sealed class DummyVideoWindow : Window
{
    private readonly TextBox _width = new() { Name = "DummyWidth" }, _height = new() { Name = "DummyHeight" },
        _fps = new() { Name = "DummyFps" }, _frames = new() { Name = "DummyFrames" };
    private readonly CheckBox _pattern = new() { Name = "DummyPattern", Content = "Checkerboard pattern (8×8 pixels)" };
    private readonly TextBlock _duration = new() { Name = "DummyDuration" }, _error = new() { Name = "DummyError", TextWrapping = TextWrapping.Wrap };
    private readonly Button _colorButton = new() { Name = "DummyColor", Padding = new Thickness(5, 3) };
    private AssColor _color;
    private static readonly (string Label, int Width, int Height)[] Resolutions =
    [
        ("640×480 (SD fullscreen)",640,480), ("704×480 (SD anamorphic)",704,480),
        ("640×360 (SD widescreen)",640,360), ("704×396 (SD widescreen)",704,396),
        ("640×352 (SD widescreen MOD16)",640,352), ("704×400 (SD widescreen MOD16)",704,400),
        ("1280×720 (HD 720p)",1280,720), ("1920×1080 (HD 1080p)",1920,1080), ("1024×576 (SuperPAL widescreen)",1024,576)
    ];
    public DummyVideoWindow(DummyVideoOptions initial)
    {
        var panel = VideoDialogInput.Panel(this, "DummyVideoDialog", "Dummy video options", 430);
        var presets = new ComboBox { Name = "DummyResolution", ItemsSource = Resolutions.Select(r => r.Label).ToArray(), HorizontalAlignment = HorizontalAlignment.Stretch };
        panel.Children.Add(presets); VideoDialogInput.Name(this, presets);
        void Field(string label, Control control)
        { var row = new Grid { ColumnDefinitions = new("150,*"), ColumnSpacing = 8 }; row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }); Grid.SetColumn(control, 1); row.Children.Add(control); panel.Children.Add(row); }
        var size = new Grid { ColumnDefinitions = new("*,Auto,*"), ColumnSpacing = 5 };
        size.Children.Add(_width); var x = new TextBlock { Text = "×", VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(x, 1); size.Children.Add(x); Grid.SetColumn(_height, 2); size.Children.Add(_height);
        Field("Resolution (pixels)", size); Field("Color", _colorButton); panel.Children.Add(_pattern);
        Field("Frame rate (fps)", _fps); Field("Duration (frames)", _frames); panel.Children.Add(_duration); panel.Children.Add(_error);
        foreach (var control in new Control[] { _width, _height, _fps, _frames, _colorButton, _pattern, _duration, _error }) VideoDialogInput.Name(this, control);
        _width.Text = initial.Width.ToString(CultureInfo.InvariantCulture); _height.Text = initial.Height.ToString(CultureInfo.InvariantCulture);
        _fps.Text = initial.FramesPerSecond.ToString(CultureInfo.InvariantCulture); _frames.Text = initial.FrameCount.ToString(CultureInfo.InvariantCulture);
        _pattern.IsChecked = initial.Checkerboard; _color = initial.Color with { Transparency = 0 }; ColorLabel();
        foreach (var field in new[] { _width, _height, _fps, _frames }) field.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) Read(); };
        presets.SelectionChanged += (_, _) => { if (presets.SelectedIndex is >= 0 and < 9) { var preset = Resolutions[presets.SelectedIndex]; _width.Text = preset.Width.ToString(CultureInfo.InvariantCulture); _height.Text = preset.Height.ToString(CultureInfo.InvariantCulture); } };
        _colorButton.Click += async (_, _) => { if (await new AssColorDialog(_color).ShowDialog<AssColor?>(this) is { } color) { _color = color with { Transparency = 0 }; ColorLabel(); } };
        VideoDialogInput.Buttons(this, panel, "Dummy", "Open", () => { if (Read() is { } options) Close(options); });
        Read(); Opened += (_, _) => { _width.Focus(); _width.SelectAll(); };
    }
    private void ColorLabel()
    { _colorButton.Content = ColorSpace.Html(_color); _colorButton.Background = new SolidColorBrush(Color.FromRgb(_color.Red, _color.Green, _color.Blue)); _colorButton.Foreground = _color.Red + _color.Green + _color.Blue > 380 ? Brushes.Black : Brushes.White; }
    private DummyVideoOptions? Read()
    {
        if (!int.TryParse(_width.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var width) ||
            !int.TryParse(_height.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var height) ||
            !double.TryParse(_fps.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var fps) ||
            !int.TryParse(_frames.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var frames))
        { _error.Text = "Enter numeric resolution, frame rate and frame count."; _duration.Text = ""; return null; }
        var options = new DummyVideoOptions { Width = width, Height = height, FramesPerSecond = fps, FrameCount = frames, Color = _color, Checkerboard = _pattern.IsChecked == true };
        try { options.Validate(); _error.Text = ""; _duration.Text = "Resulting duration: " + AssTime.Format((long)Math.Round(options.DurationSeconds * 1000)); return options; }
        catch (ArgumentException e) { _error.Text = e.Message; _duration.Text = ""; return null; }
    }
}

public sealed class VideoDetailsWindow : Window
{
    public VideoDetailsWindow(VideoDetails details)
    {
        var panel = VideoDialogInput.Panel(this, "VideoDetailsDialog", "Video details", 560);
        void Field(string label, string value, string name)
        {
            var row = new Grid { ColumnDefinitions = new("100,*"), ColumnSpacing = 8 }; row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            var field = new TextBox { Name = name, Text = value, IsReadOnly = true }; Grid.SetColumn(field, 1); row.Children.Add(field); panel.Children.Add(row); VideoDialogInput.Name(this, field);
        }
        int Gcd(int a, int b) { while (b != 0) (a, b) = (b, a % b); return a; } var divisor = Gcd(details.Width, details.Height);
        Field("File name", details.Source, "DetailsSource"); Field("FPS (source)", details.FramesPerSecond.ToString("0.000", CultureInfo.InvariantCulture), "DetailsFps");
        Field("Resolution", $"{details.Width}×{details.Height} ({details.Width/divisor}:{details.Height/divisor})", "DetailsResolution");
        Field("Length", $"{details.FrameCount} frames ({AssTime.Format((long)Math.Round(details.DurationSeconds*1000))})", "DetailsLength");
        Field("Last frame", AssTime.Format((long)Math.Round(details.LastFrameSeconds*1000)), "DetailsLastFrame");
        Field("Decoder", details.Decoder, "DetailsDecoder"); Field("Timing", details.Timing, "DetailsTiming"); Field("Pixel format", details.PixelFormat, "DetailsPixelFormat");
        var ok = new Button { Name = "DetailsConfirm", Content = "OK", IsDefault = true, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right }; panel.Children.Add(ok); VideoDialogInput.Name(this, ok); ok.Click += (_, _) => Close();
    }
}
