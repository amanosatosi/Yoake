using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Yoake.UI.Services;

namespace Yoake.UI.Controls;

public sealed class AutomationProgressWindow : Window, IAutomationProgressSession
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly ProgressBar _bar = new() { Minimum = 0, Maximum = 100, IsIndeterminate = true, Height = 12 };
    private readonly TextBlock _task = new() { Text = "Running script…" };
    private readonly TextBox _output = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 140 };
    private readonly Button _button = new() { Content = "Cancel", MinWidth = 80, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly object _pendingGate = new();
    private double? _percent; private string? _pendingTask, _pendingTitle;
    private bool _posted, _completed; private int _outputLength;
    public CancellationToken CancellationToken => _cancellation.Token;
    public AutomationProgressWindow(string title)
    {
        Name = "AutomationProgress"; Title = title; Width = 500; Height = 280; MinWidth = 340; MinHeight = 220;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), Margin = new Thickness(10), RowSpacing = 7 };
        panel.Children.Add(_task); Grid.SetRow(_bar, 1); panel.Children.Add(_bar);
        Grid.SetRow(_output, 2); panel.Children.Add(_output); Grid.SetRow(_button, 3); panel.Children.Add(_button); Content = panel;
        _button.Click += (_, _) => { if (_completed) Close(); else Cancel(); };
        Closing += (_, e) => { if (!_completed) { e.Cancel = true; Cancel(); } };
        Closed += (_, _) => _cancellation.Dispose();
    }
    private void Cancel() { if (!_cancellation.IsCancellationRequested) _cancellation.Cancel(); _task.Text = "Cancelling…"; _button.IsEnabled = false; }
    public void Report(double? percent, string? task, string? title)
    {
        lock (_pendingGate)
        {
            if (percent.HasValue) _percent = percent;
            if (task is not null) _pendingTask = task;
            if (title is not null) _pendingTitle = title;
            if (_posted) return; _posted = true;
        }
        Dispatcher.UIThread.Post(() =>
        {
            lock (_pendingGate)
            {
                if (!_completed)
                {
                    if (_percent is { } value) { _bar.IsIndeterminate = false; _bar.Value = Math.Clamp(value, 0, 100); }
                    if (_pendingTask is { } text) _task.Text = text;
                    if (_pendingTitle is { } heading) Title = heading;
                }
                _posted = false;
            }
        });
    }
    public void Log(string message, int level)
    {
        if (level > 3) return;
        Dispatcher.UIThread.Post(() => Append(message));
    }
    private void Append(string message)
    {
        // Bound displayed output; the centralized log retains the full message.
        const int limit = 128 * 1024;
        if (_outputLength >= limit) return;
        var text = message[..Math.Min(message.Length, limit - _outputLength)];
        _output.Text = (_output.Text ?? "") + text; _outputLength += text.Length;
        _output.CaretIndex = _output.Text.Length;
    }
    public void Complete(Exception? failure)
    {
        Dispatcher.UIThread.VerifyAccess(); _completed = true;
        if (failure is not null && failure is not OperationCanceledException) Append(failure + "\n");
        if (_outputLength == 0) { Close(); return; }
        _bar.IsIndeterminate = false; _bar.Value = 100;
        _task.Text = failure is OperationCanceledException ? "Cancelled — changes discarded" : failure is null ? "Complete" : "Failed — changes discarded";
        _button.Content = "Close"; _button.IsEnabled = true;
    }
}
