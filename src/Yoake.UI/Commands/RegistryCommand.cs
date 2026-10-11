using System.Windows.Input;
using System.ComponentModel;
using Avalonia.Threading;
using Yoake.Core.Commands;
using Yoake.Core.Hotkeys;

namespace Yoake.UI.Commands;

public sealed class RegistryCommand : ICommand, INotifyPropertyChanged
{
    private readonly CommandRegistry _registry;
    private readonly string _commandId;
    private readonly Func<CommandContext> _contextFactory;
    private readonly object? _fixedParameter;
    private readonly Func<HotkeyProfile>? _hotkeys;

    public RegistryCommand(
        CommandRegistry registry,
        string commandId,
        Func<CommandContext> contextFactory,
        object? fixedParameter = null,
        Func<HotkeyProfile>? hotkeys = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _commandId = commandId ?? throw new ArgumentNullException(nameof(commandId));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _fixedParameter = fixedParameter;
        _hotkeys = hotkeys;

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
    public event PropertyChangedEventHandler? PropertyChanged;

    public string CommandId => _commandId;
    public string Label => ReadState(command => command.GetLabel(_contextFactory()), "");
    public string Description => ReadState(command => command.GetDescription(_contextFactory()), "");
    public bool IsChecked => ReadState(command => command.IsChecked(_contextFactory()), false);
    private HotkeyBinding? DisplayBinding
    {
        get
        {
            var bindings=_hotkeys?.Invoke().Resolver.Bindings;
            return bindings?.FirstOrDefault(binding => binding.CommandId==_commandId && binding.Context==HotkeyContext.Default)
                ?? bindings?.FirstOrDefault(binding => binding.CommandId==_commandId && binding.Context!=HotkeyContext.Always);
        }
    }
    public string Shortcut => DisplayBinding?.Gesture.Format() ?? "";
    public string Tooltip => Label+(Shortcut is {Length:>0} shortcut?" · "+shortcut:"");
    // InputGesture is display-only in Avalonia. HotKey would bypass contextual
    // resolution, text/IME guards and exclusive modes, so never bind it.
    public Avalonia.Input.KeyGesture? InputGesture => DisplayBinding is {} binding ? EditorKeyNames.ToGesture(binding.Gesture) : null;

    private T ReadState<T>(Func<AppCommand, T> read, T fallback)
    {
        try { return _registry.TryGet(_commandId, out var command) && command is not null ? read(command) : fallback; }
        catch (Exception exception) { _registry.ReportFailure(_commandId, exception); return fallback; }
    }

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
        void Notify()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Shortcut)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InputGesture)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Tooltip)));
        }
        if (Dispatcher.UIThread.CheckAccess())
            Notify();
        else
            Dispatcher.UIThread.Post(Notify);
    }
}
