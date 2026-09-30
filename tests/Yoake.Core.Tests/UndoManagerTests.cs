using Yoake.Core.Undo;

namespace Yoake.Core.Tests;

public sealed class UndoManagerTests
{
    [Fact]
    public void TransactionGroupsContinuousChangesAfterExplicitCommit()
    {
        var manager = new UndoManager();
        var value = 0;
        using (var transaction = manager.BeginTransaction("Drag subtitle"))
        {
            manager.Execute(new DelegateUndoOperation("step", () => value -= 1, () => value += 1));
            manager.Execute(new DelegateUndoOperation("step", () => value -= 2, () => value += 2));
            transaction.Commit();
        }
        Assert.Equal(3, value);
        Assert.Equal("Drag subtitle", manager.NextUndoName);
        Assert.True(manager.Undo());
        Assert.Equal(0, value);
        Assert.True(manager.Redo());
        Assert.Equal(3, value);
    }

    [Fact]
    public void DisposingUncommittedTransactionRollsBackGesture()
    {
        var manager = new UndoManager();
        var value = 0;
        using (manager.BeginTransaction("Cancelled drag"))
        {
            manager.Execute(new DelegateUndoOperation("step", () => value -= 4, () => value += 4));
            Assert.Equal(4, value);
        }

        Assert.Equal(0, value);
        Assert.False(manager.CanUndo);
        Assert.False(manager.CanRedo);
    }

    [Fact]
    public void ExplicitCancelRollsBackImmediately()
    {
        var manager = new UndoManager();
        var value = 0;
        using var transaction = manager.BeginTransaction("Cancelled drag");
        manager.Execute(new DelegateUndoOperation("step", () => value -= 2, () => value += 2));

        transaction.Cancel();

        Assert.Equal(0, value);
        Assert.True(transaction.IsCompleted);
        Assert.False(manager.CanUndo);
    }
}
