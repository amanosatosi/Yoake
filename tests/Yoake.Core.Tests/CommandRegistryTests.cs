using Yoake.Core.Commands;
using Yoake.Core.Logging;

namespace Yoake.Core.Tests;

public sealed class CommandRegistryTests
{
    [Fact]
    public async Task RegisteredCommandInvokesThroughStableId()
    {
        var registry = new CommandRegistry();
        var invoked = false;
        registry.Register(new AppCommand(new("test/run", "Run", "", "Tests"), (_, _) => { invoked = true; return ValueTask.CompletedTask; }));
        var result = await registry.InvokeAsync("test/run", new CommandContext());
        Assert.True(result);
        Assert.True(invoked);
    }

    [Fact]
    public async Task InvocationRaisesStateChanged()
    {
        var registry = new CommandRegistry();
        var stateChanges = 0;
        registry.StateChanged += (_, _) => stateChanges++;
        registry.Register(new AppCommand(new("test/change", "Change", "", "Tests"), (_, _) => ValueTask.CompletedTask));
        await registry.InvokeAsync("test/change", new CommandContext());
        Assert.Equal(1, stateChanges);
    }

    [Fact]
    public async Task CommandFailureIsContainedAndLogged()
    {
        var log = new RecordingLog();
        var registry = new CommandRegistry(log);
        Exception? observed = null;
        registry.CommandFailed += (_, args) => observed = args.Exception;
        registry.Register(new AppCommand(new("test/fail", "Fail", "", "Tests"), (_, _) => throw new InvalidOperationException("boom")));
        var result = await registry.InvokeAsync("test/fail", new CommandContext());
        Assert.False(result);
        Assert.IsType<InvalidOperationException>(observed);
        Assert.Single(log.Errors);
    }

    [Fact]
    public async Task CanExecuteFailureIsContainedAndLogged()
    {
        var log = new RecordingLog();
        var registry = new CommandRegistry(log);
        registry.Register(new AppCommand(new("test/can-fail", "Can fail", "", "Tests"), (_, _) => ValueTask.CompletedTask, _ => throw new InvalidOperationException("can-execute boom")));
        var result = await registry.InvokeAsync("test/can-fail", new CommandContext());
        Assert.False(result);
        Assert.Single(log.Errors);
        Assert.IsType<InvalidOperationException>(log.Errors[0]);
    }

    [Fact]
    public void DuplicateCommandIdIsRejected()
    {
        var registry = new CommandRegistry();
        AppCommand Make() => new(new("same/id", "Same", "", "Tests"), (_, _) => ValueTask.CompletedTask);
        registry.Register(Make());
        Assert.Throws<InvalidOperationException>(() => registry.Register(Make()));
    }

    private sealed class RecordingLog : IAppLog
    {
        public List<Exception?> Errors { get; } = [];
        public void Info(string message) { }
        public void Error(string message, Exception? exception = null) => Errors.Add(exception);
    }
}
