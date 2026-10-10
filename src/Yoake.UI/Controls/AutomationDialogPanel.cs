using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Yoake.Core.Automation;
using Yoake.Core.Subtitles;

namespace Yoake.UI.Controls;

// Shared controls for modal macro dialogs and embedded export settings.
public sealed class AutomationDialogPanel : UserControl
{
    private readonly List<(string Name, Func<object?> Read, object? Initial)> _values = [];
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap };
    public Action<Exception>? ReportFailure { get; set; }
    public AutomationDialogPanel(IReadOnlyList<AutomationLine> controls)
    {
        var grid = new Grid { ColumnSpacing = 4, RowSpacing = 4, Margin = new Thickness(8) };
        foreach (var definition in controls)
        {
            var kind = Text(definition, "class").ToLowerInvariant();
            var name = Text(definition, "name");
            var x = Integer(definition, "x", 0); var y = Integer(definition, "y", 0);
            var width = Math.Max(1, Integer(definition, "width", 1)); var height = Math.Max(1, Integer(definition, "height", 1));
            if (x < 0 || y < 0 || width < 1 || height < 1 || (long)x + width > 256 || (long)y + height > 256)
                throw new ArgumentException("Automation dialog grid coordinates/spans are invalid or exceed 256 cells.");
            while (grid.ColumnDefinitions.Count < x + width) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto) { MinWidth = 40 });
            while (grid.RowDefinitions.Count < y + height) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto) { MinHeight = 24 });
            Control control; Func<object?>? read = null; object? initial = null;
            switch (kind)
            {
                case "label": control = new TextBlock { Text = Text(definition, "label"), VerticalAlignment = VerticalAlignment.Center }; break;
                case "edit": case "textbox": case "alpha":
                    var text = new TextBox { Text = Text(definition, "text", Text(definition, "value")), AcceptsReturn = kind == "textbox", MinWidth = 100, MinHeight = kind == "textbox" ? 60 : 26 };
                    control = text; read = () => text.Text ?? ""; initial = text.Text; break;
                case "intedit":
                    var min = Integer(definition, "min", int.MinValue); var max = Integer(definition, "max", int.MaxValue);
                    if (min >= max) { min = int.MinValue; max = int.MaxValue; }
                    var number = new NumericUpDown { Minimum = min, Maximum = max, Increment = 1, FormatString = "0", Value = Math.Clamp(Integer(definition, "value", 0), min, max), MinWidth = 90 };
                    control = number; read = () => (int)(number.Value ?? 0); initial = read(); break;
                case "floatedit":
                    var low = Number(definition, "min", -double.MaxValue); var high = Number(definition, "max", double.MaxValue);
                    if (low >= high) { low = -double.MaxValue; high = double.MaxValue; }
                    var value = Math.Clamp(Number(definition, "value", 0), low, high);
                    var entry = new TextBox { Text = value.ToString("G17", CultureInfo.InvariantCulture), MinWidth = 100 };
                    object ReadNumber()
                    {
                        if (!double.TryParse(entry.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n) || n < low || n > high)
                            throw new ArgumentException($"'{name}' must be a finite number between {low} and {high}.");
                        return n;
                    }
                    read = ReadNumber; initial = value; control = entry;
                    var step = Number(definition, "step", 0);
                    if (step > 0)
                    {
                        var panel = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
                        panel.Children.Add(entry);
                        for (var direction = -1; direction <= 1; direction += 2)
                        {
                            var delta = direction * step; var button = new Button { Content = direction < 0 ? "−" : "+", Padding = new Thickness(5, 1) };
                            button.Click += (_, _) => { try { entry.Text = Math.Clamp((double)ReadNumber() + delta, low, high).ToString("G17", CultureInfo.InvariantCulture); } catch (ArgumentException e) { _error.Text = e.Message; } };
                            Grid.SetColumn(button, direction < 0 ? 1 : 2); panel.Children.Add(button);
                        }
                        control = panel;
                    }
                    break;
                case "dropdown":
                    var items = definition["items"] is IReadOnlyDictionary<string, object?> entries
                        ? entries.Values.Where(v => v is string or double or int).Select(v => Convert.ToString(v, CultureInfo.InvariantCulture)!).ToArray() : [];
                    var selected = Text(definition, "value");
                    var dropdown = new ComboBox { ItemsSource = items, SelectedItem = items.Contains(selected) ? selected : null, MinWidth = 100 };
                    control = dropdown; read = () => dropdown.SelectedItem as string ?? selected; initial = selected; break;
                case "checkbox":
                    var check = new CheckBox { Content = Text(definition, "label"), IsChecked = definition["value"] is true };
                    control = check; read = () => check.IsChecked == true; initial = read(); break;
                case "color": case "coloralpha":
                    var color = ParseColor(Text(definition, "value")); var alpha = kind == "coloralpha";
                    var picker = new Button { MinWidth = 80 };
                    void RefreshColor() { picker.Content = Hex(color, alpha); picker.Background = new SolidColorBrush(Color.FromRgb(color.Red, color.Green, color.Blue)); }
                    RefreshColor();
                    picker.Click += async (_, _) =>
                    {
                        try
                        {
                            var chosen = await new AssColorDialog(color).ShowDialog<AssColor?>((Window)(TopLevel.GetTopLevel(this) ?? throw new InvalidOperationException("A color picker requires a window.")));
                            if (chosen is { } next) { color = alpha ? next : next with { Transparency = color.Transparency }; RefreshColor(); }
                        }
                        catch (Exception e) { _error.Text = e.Message; ReportFailure?.Invoke(e); }
                    };
                    control = picker; read = () => Hex(color, alpha); initial = read(); break;
                default: throw new ArgumentException($"Bad Automation control class '{kind}'.");
            }
            control.Name = "AutomationControl_" + name;
            control.HorizontalAlignment = HorizontalAlignment.Stretch;
            ToolTip.SetTip(control, Text(definition, "hint"));
            Grid.SetColumn(control, x); Grid.SetRow(control, y); Grid.SetColumnSpan(control, width); Grid.SetRowSpan(control, height);
            grid.Children.Add(control);
            if (read is not null) _values.Add((name, read, initial));
        }
        var root = new DockPanel();
        DockPanel.SetDock(_error, Dock.Bottom); _error.Margin = new Thickness(8, 0); root.Children.Add(_error);
        root.Children.Add(grid); Content = root;
    }

    public bool TryRead(object button, bool cancellation, out AutomationDialogResult result)
    {
        Dictionary<string, object?> values = [];
        try
        {
            foreach (var item in _values)
            {
                try { values[item.Name] = item.Read(); }
                catch (ArgumentException) when (cancellation) { values[item.Name] = item.Initial; }
            }
            result = new(button, values); return true;
        }
        catch (ArgumentException e) { _error.Text = e.Message; result = new(false, values); return false; }
    }
    private static string Text(AutomationLine line, string key, string fallback = "") => line[key] is string or int or double ? Convert.ToString(line[key], CultureInfo.InvariantCulture)! : fallback;
    private static double Number(AutomationLine line, string key, double fallback) { try { return line.Number(key); } catch (ArgumentException) { return fallback; } }
    private static int Integer(AutomationLine line, string key, int fallback) { try { return line.Integer(key); } catch (ArgumentException) { return fallback; } }
    private static string Hex(AssColor color, bool alpha) => $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}" + (alpha ? $"{color.Transparency:X2}" : "");
    private static AssColor ParseColor(string value)
    {
        if (value.StartsWith('#') && value.Length is 7 or 9 && uint.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed))
            return value.Length == 7 ? new((byte)(packed >> 16), (byte)(packed >> 8), (byte)packed, 0)
                : new((byte)(packed >> 24), (byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);
        return AssColor.TryParse(value, out var color) ? color : new(0, 0, 0, 0);
    }
}
