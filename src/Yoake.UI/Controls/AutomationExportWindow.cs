using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Yoake.Core.Automation;
using Yoake.UI.Services;

namespace Yoake.UI.Controls;

public sealed class AutomationExportWindow : Window
{
    private sealed record Row(AutomationExportOption Option, CheckBox Check, AutomationDialogPanel? Panel, StackPanel Settings, Control Item);
    private readonly List<Row> _rows = [];

    public AutomationExportWindow(IReadOnlyList<AutomationExportOption> options, IReadOnlyList<string> selected, string encoding, Action<Exception> reportFailure)
    {
        Name = "AutomationExportDialog"; Title = "Export subtitles"; Width = 880; Height = 560;
        MinWidth = 680; MinHeight = 400; FontSize = 12; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,3*"), RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(10), ColumnSpacing = 10, RowSpacing = 8 };
        var left = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), RowSpacing = 6 };
        left.Children.Add(new TextBlock { Text = "Apply checked filters in the listed order" });
        var list = new ListBox { Name = "AutomationExportFilters" };
        Grid.SetRow(list, 1); left.Children.Add(list);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var up = new Button { Content = "Move up" }; var down = new Button { Content = "Move down" };
        var all = new Button { Content = "All" }; var none = new Button { Content = "None" };
        foreach (var button in new[] { up, down, all, none }) tools.Children.Add(button);
        Grid.SetRow(tools, 2); left.Children.Add(tools);
        var description = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 115 };
        Grid.SetRow(description, 3); left.Children.Add(description); root.Children.Add(left);
        var settings = new StackPanel { Spacing = 8 };
        var settingsScroll = new ScrollViewer { Content = settings, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        Grid.SetColumn(settingsScroll, 1); root.Children.Add(settingsScroll);
        var order = selected.Select(name => options.FirstOrDefault(o => o.Binding.Name == name)).Where(o => o is not null).Cast<AutomationExportOption>()
            .Concat(options.Where(o => !selected.Contains(o.Binding.Name))).Distinct().ToArray();
        foreach (var option in order)
        {
            var error = option.Error; AutomationDialogPanel? panel = null;
            try { if (error is null && option.Controls.Count > 0) panel = new(option.Controls) { ReportFailure = reportFailure }; }
            catch (Exception e) { error = e.Message; reportFailure(e); }
            var check = new CheckBox { Content = option.Binding.Name, IsChecked = error is null && selected.Contains(option.Binding.Name), IsEnabled = error is null };
            var item = new StackPanel { Margin = new Thickness(2) }; item.Children.Add(check);
            ToolTip.SetTip(item, error ?? option.Binding.Filter.Description);
            var group = new StackPanel { Spacing = 3, IsVisible = check.IsChecked == true && panel is not null };
            group.Children.Add(new TextBlock { Text = option.Binding.Name, FontWeight = FontWeight.SemiBold });
            if (panel is not null) group.Children.Add(panel);
            settings.Children.Add(group);
            var row = new Row(option with { Error = error }, check, panel, group, item); _rows.Add(row);
            check.IsCheckedChanged += (_, _) => group.IsVisible = check.IsChecked == true && panel is not null;
        }
        void Refresh(int index)
        {
            list.ItemsSource = _rows.Select(r => r.Item).ToArray(); list.SelectedIndex = index;
            up.IsEnabled = index > 0; down.IsEnabled = index >= 0 && index < _rows.Count - 1;
        }
        void Move(int delta)
        {
            var index = list.SelectedIndex; var target = index + delta;
            if (index < 0 || target < 0 || target >= _rows.Count) return;
            var row = _rows[index]; _rows.RemoveAt(index); _rows.Insert(target, row);
            settings.Children.Clear(); foreach (var entry in _rows) settings.Children.Add(entry.Settings);
            Refresh(target);
        }
        list.SelectionChanged += (_, _) =>
        {
            var index = list.SelectedIndex;
            up.IsEnabled = index > 0; down.IsEnabled = index >= 0 && index < _rows.Count - 1;
            description.Text = index >= 0 ? _rows[index].Option.Error ?? _rows[index].Option.Binding.Filter.Description : "";
        };
        up.Click += (_, _) => Move(-1); down.Click += (_, _) => Move(1);
        all.Click += (_, _) => { foreach (var row in _rows) if (row.Check.IsEnabled) row.Check.IsChecked = true; };
        none.Click += (_, _) => { foreach (var row in _rows) row.Check.IsChecked = false; };
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 6 };
        footer.Children.Add(new TextBlock { Text = "Text encoding", VerticalAlignment = VerticalAlignment.Center });
        var encodings = new[] { "UTF-8", "UTF-8 BOM", "UTF-16 LE", "UTF-16 BE" };
        var choice = new ComboBox { Name = "AutomationExportEncoding", ItemsSource = encodings, SelectedItem = encodings.Contains(encoding) ? encoding : "UTF-8", Width = 140, HorizontalAlignment = HorizontalAlignment.Left };
        Grid.SetColumn(choice, 1); footer.Children.Add(choice);
        var export = new Button { Name = "AutomationExportAccept", Content = "Export…", IsDefault = true, MinWidth = 85 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 75 };
        Grid.SetColumn(export, 2); footer.Children.Add(export); Grid.SetColumn(cancel, 3); footer.Children.Add(cancel);
        cancel.Click += (_, _) => Close();
        export.Click += (_, _) =>
        {
            List<AutomationFilterSettings> filters = [];
            foreach (var row in _rows.Where(r => r.Check.IsChecked == true))
            {
                IReadOnlyDictionary<string, object?> values = new Dictionary<string, object?>();
                if (row.Panel is not null)
                {
                    if (!row.Panel.TryRead("", false, out var result)) return;
                    values = result.Values;
                }
                filters.Add(new(row.Option.Binding, values));
            }
            Close(new AutomationExportChoice(filters, (string)choice.SelectedItem!));
        };
        Grid.SetRow(footer, 1); Grid.SetColumnSpan(footer, 2); root.Children.Add(footer);
        Content = root; Refresh(_rows.Count > 0 ? 0 : -1);
    }

    public async Task<AutomationExportChoice?> DisplayAsync(Window owner, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var registration = token.Register(() => Dispatcher.UIThread.Post(() => Close()));
        var result = await ShowDialog<AutomationExportChoice?>(owner);
        token.ThrowIfCancellationRequested(); return result;
    }
}
