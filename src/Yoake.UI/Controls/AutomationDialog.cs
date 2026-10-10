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

public sealed class AutomationDialog : Window
{
    private readonly AutomationDialogPanel _panel;
    private object _escape = false;
    private AutomationDialogResult? _result;
    public AutomationDialog(AutomationDialogRequest request, Action<Exception>? reportFailure = null)
    {
        Name = "AutomationConfigDialog"; Title = "Automation"; FontSize = 12;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        MinWidth = 360; MaxWidth = 1100; MaxHeight = 720; SizeToContent = SizeToContent.WidthAndHeight;
        _panel = new(request.Controls) { ReportFailure = reportFailure };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 5, Margin = new Thickness(8, 0, 8, 8) };
        var labels = request.Buttons is { Count: > 0 } ? request.Buttons : new[] { "", "" };
        var ids = request.ButtonIds ?? new Dictionary<string, string>();
        foreach (var mapped in ids.Values) if (!labels.Contains(mapped)) throw new ArgumentException($"Invalid Automation button '{mapped}' in button-ID map.");
        for (var i = 0; i < labels.Count; i++)
        {
            var label = labels[i]; var defaultButtons = request.Buttons is not { Count: > 0 };
            var role = defaultButtons ? (i == 0 ? "ok" : "cancel") : ids.FirstOrDefault(p => p.Value == label).Key;
            object returned = role == "cancel" ? false : label;
            var button = new Button { Name = "AutomationButton_" + i, Content = defaultButtons ? (i == 0 ? "OK" : "Cancel") : label,
                IsDefault = role is "ok" or "yes" or "save", IsCancel = role is "cancel" or "close" or "no", MinWidth = 70 };
            if (button.IsCancel) _escape = returned;
            button.Click += (_, _) => { if (_panel.TryRead(returned, role == "cancel", out var result)) { _result = result; Close(result); } };
            buttons.Children.Add(button);
        }
        var root = new DockPanel(); DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        root.Children.Add(new ScrollViewer { Content = _panel, MaxHeight = 600, MaxWidth = 1080, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto });
        Content = root;
        Closing += (_, _) => { if (_result is null && _panel.TryRead(_escape, true, out var result)) _result = result; };
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) { if (_panel.TryRead(_escape, true, out var result)) { _result = result; Close(result); } e.Handled = true; } }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    public async Task<AutomationDialogResult> DisplayAsync(Window owner, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var registration = token.Register(() => Dispatcher.UIThread.Post(() => Close()));
        var result = await ShowDialog<AutomationDialogResult?>(owner);
        token.ThrowIfCancellationRequested();
        return result ?? _result ?? new(false, new Dictionary<string, object?>());
    }
}
