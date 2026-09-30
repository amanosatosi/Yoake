using System.Windows.Input;
using Avalonia.Threading;
using Yoake.Core.Commands;

namespace Yoake.UI.Commands;

public sealed class RegistryCommand : ICommand
{
    private readonly CommandRegistry _registry;
    private readonly string _commandId;
    private readonly Func<CommandContext> _contextFactory;
    private readonly object? _fixedParameter;

    public RegistryCommand(
        CommandRegistry registry,
        string commandId,
        Func<CommandContext> contextFactory,
        object? fixedParameter = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _commandId = commandId ?? throw new ArgumentNullException(nameof(commandId));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _fixedParameter = fixedParameter;

        var weakCommand = new WeakReference<RegistryCommand>(this);
        var source = registry;
        EventHandler? stateHandler = null;
        stateHandler = (_, _) =>
        {
            if (weakCommand.TryGetTarget(out var target))
                target.Refresh();
            else
                source.StateChanged -= stateHandler;
        };
        source.StateChanged += stateHandler;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        try
        {
            return _registry.CanExecute(_commandId, _contextFactory());
        }
        catch (Exception exception)
        {
            _registry.ReportFailure(_commandId, exception);
            return false;
        }
    }

    public void Execute(object? parameter) => _ = ExecuteSafelyAsync(parameter);

    private async Task ExecuteSafelyAsync(object? parameter)
    {
        try
        {
            var effectiveParameter = _fixedParameter ?? parameter;
            await _registry.InvokeAsync(_commandId, _contextFactory(), effectiveParameter);
        }
        catch (Exception exception)
        {
            _registry.ReportFailure(_commandId, exception);
        }
    }

    public void Refresh()
    {
        if (Dispatcher.UIThread.CheckAccess())
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        else
            Dispatcher.UIThread.Post(() => CanExecuteChanged?.Invoke(this, EventArgs.Empty));
    }
}
