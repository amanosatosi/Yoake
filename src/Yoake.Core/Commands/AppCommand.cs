namespace Yoake.Core.Commands;

public sealed record AppCommandMetadata(
    string Id,
    string Label,
    string Description,
    string Category,
    string? IconKey = null);

public sealed record CommandContext(
    Guid? DocumentId = null,
    string? FocusContext = null,
    object? Payload = null);

public sealed record CommandInvocation(CommandContext Context, object? Parameter = null);

public sealed class AppCommand
{
    private readonly Func<CommandInvocation, CancellationToken, ValueTask> _execute;
    private readonly Func<CommandContext, bool> _canExecute;
    private readonly Func<CommandContext, bool>? _isChecked;

    public AppCommand(
        AppCommandMetadata metadata,
        Func<CommandInvocation, CancellationToken, ValueTask> execute,
        Func<CommandContext, bool>? canExecute = null,
        Func<CommandContext, bool>? isChecked = null)
    {
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute ?? (static _ => true);
        _isChecked = isChecked;
    }

    public AppCommandMetadata Metadata { get; }
    public bool CanExecute(CommandContext context) => _canExecute(context);
    public bool IsChecked(CommandContext context) => _isChecked?.Invoke(context) ?? false;
    public ValueTask ExecuteAsync(CommandInvocation invocation, CancellationToken cancellationToken = default)
        => _execute(invocation, cancellationToken);
}
