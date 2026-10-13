using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using System.ComponentModel;
using System.Globalization;
using Yoake.UI.ViewModels;

namespace Yoake.UI.Controls;

public sealed class KaraokeSlotsControl : UserControl
{
    public static readonly StyledProperty<MainWindowViewModel?> ModelProperty = AvaloniaProperty.Register<KaraokeSlotsControl, MainWindowViewModel?>(nameof(Model));
    public MainWindowViewModel? Model { get => GetValue(ModelProperty); set => SetValue(ModelProperty, value); }
    private readonly Button _commit = new() { Name = "KaraokeCommit", Content = "Commit", Padding = new Thickness(4, 0), MinHeight = 22 };
    private readonly Button _cancel = new() { Name = "KaraokeCancel", Content = "Cancel", Padding = new Thickness(4, 0), MinHeight = 22 };
    private readonly ToggleButton _auto = new() { Name = "KaraokeAutoCut", Content = "Auto Cut", Padding = new Thickness(4, 0), MinHeight = 22 };
    private readonly ComboBox _tags = new() { Name = "KaraokeTags", ItemsSource = MainWindowViewModel.KaraokeTags, Width = 58, MinHeight = 22, Padding = new Thickness(4, 0) };
    private readonly TextBlock _status = new() { FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
    public KaraokeTextCutsControl Cuts { get; } = new() { Name = "KaraokeTextCuts" };
    private bool _updating;
    public KaraokeSlotsControl()
    {
        var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 3 };
        buttons.Children.Add(_commit); buttons.Children.Add(_cancel); buttons.Children.Add(_tags); buttons.Children.Add(_auto);
        var rest = new Button { Name = "KaraokeRest", Content = "+ Rest", Padding = new Thickness(3, 0), MinHeight = 22 };
        ToolTip.SetTip(rest, "Insert an empty timing slot before the current group");
        rest.Click += (_, _) => { if (Model?.KaraokePlan is {} plan) Model.Actions["karaoke/split"].Execute(new KaraokeCutRequest(plan.Current, plan.Slots[plan.Current].TextStart)); };
        buttons.Children.Add(rest);
        var top = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 5 }; top.Children.Add(buttons); top.Children.Add(_status); Grid.SetColumn(_status, 1);
        var scroll = new ScrollViewer { Content = Cuts, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, MaxHeight = 34 };
        var root = new Grid { RowDefinitions = new("24,Auto"), RowSpacing = 2 }; root.Children.Add(top); root.Children.Add(scroll); Grid.SetRow(scroll, 1); Content = root;
        _tags.SelectionChanged += (_, _) => { if (!_updating && _tags.SelectedItem is string tag && Model is {} model) model.Actions["karaoke/tag"].Execute(new KaraokeTagRequest(tag, false)); };
        ToolTip.SetTip(_tags, "Karaoke tag type for all groups; Ctrl+right-click audio for one group");
        ToolTip.SetTip(Cuts, "Click between graphemes to cut; click a split to join. Right-click a group to select it. Audio numpad +/− adjusts its end; X/Z navigate groups, C/V add lead, Enter/G commit, S auditions.");
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ModelProperty) return;
        if (change.OldValue is MainWindowViewModel old) { old.PropertyChanged -= Changed; old.Registry.StateChanged -= StateChanged; }
        if (Model is {} model) { model.PropertyChanged += Changed; model.Registry.StateChanged += StateChanged; }
        Refresh();
    }
    private void StateChanged(object? sender, EventArgs e) => Refresh();
    private void Changed(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName is nameof(MainWindowViewModel.KaraokeSlots) or nameof(MainWindowViewModel.KaraokeStatus) or nameof(MainWindowViewModel.ShowKaraoke)) Refresh(); }
    private void Refresh()
    {
        _updating = true;
        try
        {
            Cuts.Model = Model; Cuts.InvalidateMeasure(); Cuts.InvalidateVisual();
            if (Model is not {} model) return;
            _commit.Command = model.Actions["karaoke/commit"]; _cancel.Command = model.Actions["karaoke/cancel"]; _auto.Command = model.Actions["karaoke/auto-cut"];
            _auto.IsChecked = model.KaraokeAutoCut; _tags.SelectedItem = model.KaraokeTag; _tags.IsEnabled = model.Actions["karaoke/tag"].CanExecute(null);
            _status.Text = model.KaraokeStatus; ToolTip.SetTip(_status, model.KaraokeStatus);
        }
        finally { _updating = false; }
    }
}

public sealed class KaraokeTextCutsControl : Control
{
    public MainWindowViewModel? Model { get; set; }
    private readonly List<(double X, int Position)> _positions = [];
    private readonly List<(double X, int Right)> _boundaries = [];
    private readonly List<(Rect Area, int Slot)> _groups = [];
    public KaraokeTextCutsControl() { Focusable = true; Height = 22; }
    private FormattedText Label(string text, IBrush brush) => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 12, brush);
    protected override Size MeasureOverride(Size availableSize) => new(Math.Max(80, MeasureText()), 22);
    private double MeasureText() => Model?.KaraokePlan is {} plan ? plan.Slots.Sum(s => Math.Max(18, Label(s.Text.Length == 0 ? "∅" : s.Text, Brushes.White).Width + 12)) + 8 : 80;
    public override void Render(DrawingContext context)
    {
        base.Render(context); _positions.Clear(); _boundaries.Clear(); _groups.Clear();
        context.FillRectangle(new SolidColorBrush(Color.FromRgb(20,23,29)),new Rect(Bounds.Size));
        if (Model?.KaraokePlan is not {} plan) return;
        var safe = plan.SafeCuts(); var x = 4d;
        foreach (var slot in plan.Slots)
        {
            var start = x; var brush = slot.Current ? Brushes.Gold : slot.Assigned ? Brushes.LightGreen : Brushes.LightGray;
            if (slot.Index > 0) _boundaries.Add((x, slot.Index));
            var begin = x + 6; var endPosition = slot.TextStart + slot.Text.Length;
            _positions.Add((begin, slot.TextStart));
            if (slot.Text.Length == 0) { context.DrawText(Label("∅", brush), new(begin, 2)); x = begin + 12; }
            else
            {
                context.DrawText(Label(slot.Text, brush), new(begin, 2));
                foreach (var cut in safe.Where(i => i > slot.TextStart && i <= endPosition))
                    _positions.Add((begin + Label(plan.Text[slot.TextStart..cut], brush).Width, cut));
                x = begin + Label(slot.Text, brush).Width;
            }
            x += 6; var area = new Rect(start, 0, Math.Max(18, x - start), 22); _groups.Add((area, slot.Index));
            if (slot.Current) context.DrawRectangle(null, new Pen(Brushes.Gold, 1), area);
            if (slot.Index > 0) context.DrawLine(new Pen(Brushes.SlateGray, 1), new(start, 0), new(start, 22));
        }
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e); Focus(); if (Model?.KaraokePlan is not {} plan || _positions.Count == 0) return;
        var point = e.GetCurrentPoint(this); var group = _groups.FirstOrDefault(g => g.Area.Contains(point.Position));
        if (point.Properties.IsRightButtonPressed) { Model.Actions["karaoke/select"].Execute(group.Slot); e.Handled = true; return; }
        if (!point.Properties.IsLeftButtonPressed) return;
        var near = _boundaries.OrderBy(b => Math.Abs(b.X - point.Position.X)).FirstOrDefault();
        if (near.Right > 0 && Math.Abs(near.X - point.Position.X) <= 3) Model.Actions["karaoke/join"].Execute(near.Right);
        else { var cut = _positions.OrderBy(p => Math.Abs(p.X - point.Position.X)).First(); Model.Actions["karaoke/split"].Execute(new KaraokeCutRequest(group.Slot, cut.Position)); }
        e.Handled = true;
    }
}
