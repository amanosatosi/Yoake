using Yoake.Core.Logging;

namespace Yoake.Core.Commands;

public sealed class CommandFailureEventArgs(string commandId, Exception exception) : EventArgs
{
    public string CommandId { get; } = commandId;
    public Exception Exception { get; } = exception;
}

public sealed class CommandRegistry(IAppLog? log = null)
{
    private readonly Dictionary<string, AppCommand> _commands = new(StringComparer.Ordinal);

    public event EventHandler? StateChanged;
    public event EventHandler<CommandFailureEventArgs>? CommandFailed;

    public IEnumerable<AppCommand> Commands => _commands.Values;

    public void Register(AppCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!_commands.TryAdd(command.Metadata.Id, command))
            throw new InvalidOperationException($"Command '{command.Metadata.Id}' is already registered.");
    }

    public bool TryGet(string id, out AppCommand? command) => _commands.TryGetValue(id, out command);

    public AppCommand GetRequired(string id)
        => _commands.TryGetValue(id, out var command)
            ? command
            : throw new KeyNotFoundException($"Unknown command '{id}'.");

    public bool CanExecute(string id, CommandContext context) => GetRequired(id).CanExecute(context);

    public void NotifyStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    public void ReportFailure(string commandId, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentNullException.ThrowIfNull(exception);
        log?.Error($"Command '{commandId}' failed.", exception);
        CommandFailed?.Invoke(this, new CommandFailureEventArgs(commandId, exception));
    }

    public async ValueTask<bool> InvokeAsync(
        string id,
        CommandContext context,
        object? parameter = null,
        CancellationToken cancellationToken = default)
    {
        var command = GetRequired(id);

        try
        {
            if (!command.CanExecute(context))
                return false;

            await command.ExecuteAsync(new CommandInvocation(context, parameter), cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            ReportFailure(id, exception);
            return false;
        }
        finally
        {
            NotifyStateChanged();
        }
    }
}
