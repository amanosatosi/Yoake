using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Yoake.UI.ViewModels;
using System.ComponentModel;
using System.Globalization;

namespace Yoake.UI.Controls;

/// <summary>VideoBox's editable percentage/dropdown, shared by both hosts.</summary>
public sealed class VideoZoomControl : UserControl
{
    private readonly TextBox _text = new() { Width = 64, Padding = new(3, 0), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly ComboBox _choices = new() { Width = 28, ItemsSource = Enumerable.Range(1, 24).Select(i => (i * 12.5).ToString(CultureInfo.InvariantCulture) + "%").ToArray() };
    private MainWindowViewModel? _model;
    public VideoZoomControl()
    {
        ToolTip.SetTip(this, "Video window zoom (12.5%–1000%). Middle drag: pan; Ctrl-wheel: content zoom.");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 }; row.Children.Add(_text); row.Children.Add(_choices); Content = row;
        DataContextChanged += (_, _) => Attach();
        _choices.SelectionChanged += (_, _) => { if (_choices.SelectedItem is string text) { _model?.Actions["video/presentation/zoom"].Execute(text); _choices.SelectedIndex = -1; } };
        _text.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Escape) || _text.GetVisualDescendants().OfType<TextPresenter>().Any(p => !string.IsNullOrEmpty(p.PreeditText))) return;
            if (e.Key == Key.Enter) _model?.Actions["video/presentation/zoom"].Execute(_text.Text);
            else Update(); e.Handled = true;
        };
        _text.LostFocus += (_, _) => Update();
    }
    private void Attach()
    {
        if (_model is not null) _model.PropertyChanged -= Changed;
        _model = DataContext as MainWindowViewModel;
        if (_model is not null) _model.PropertyChanged += Changed;
        Update();
    }
    private void Changed(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName is nameof(MainWindowViewModel.VideoZoomText) or nameof(MainWindowViewModel.VideoSourcePath)) Update(); }
    private void Update() { _text.Text = _model?.VideoZoomText ?? "100%"; IsEnabled = _model?.CurrentVideoDetails is not null; }
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    { if (_model is not null) _model.PropertyChanged -= Changed; base.OnDetachedFromVisualTree(e); }
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); Attach(); }
}
