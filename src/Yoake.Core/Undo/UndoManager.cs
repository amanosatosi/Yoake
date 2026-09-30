namespace Yoake.Core.Undo;

public interface IUndoOperation
{
    string Name { get; }
    void Undo();
    void Redo();
}

public interface IUndoTransaction : IDisposable
{
    bool IsCompleted { get; }
    void Commit();
    void Cancel();
}

public sealed class DelegateUndoOperation(string name, Action undo, Action redo) : IUndoOperation
{
    public string Name { get; } = string.IsNullOrWhiteSpace(name) ? "Edit" : name;
    public void Undo() => undo();
    public void Redo() => redo();
}

public sealed class UndoManager
{
    private readonly Stack<IUndoOperation> _undo = new();
    private readonly Stack<IUndoOperation> _redo = new();
    private UndoTransaction? _transaction;

    public event EventHandler? Changed;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? NextUndoName => _undo.TryPeek(out var item) ? item.Name : null;
    public string? NextRedoName => _redo.TryPeek(out var item) ? item.Name : null;

    public IUndoTransaction BeginTransaction(string name)
    {
        if (_transaction is not null)
            throw new InvalidOperationException("Nested undo transactions are not supported.");
        _transaction = new UndoTransaction(this, string.IsNullOrWhiteSpace(name) ? "Edit" : name);
        return _transaction;
    }

    public void Execute(IUndoOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        operation.Redo();
        if (_transaction is not null)
        {
            _transaction.Add(operation);
            return;
        }
        PushUndo(operation);
    }

    public bool Undo()
    {
        if (_transaction is not null)
            throw new InvalidOperationException("Cannot undo while a transaction is active.");
        if (!_undo.TryPop(out var operation))
            return false;
        operation.Undo();
        _redo.Push(operation);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Redo()
    {
        if (_transaction is not null)
            throw new InvalidOperationException("Cannot redo while a transaction is active.");
        if (!_redo.TryPop(out var operation))
            return false;
        operation.Redo();
        _undo.Push(operation);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void PushUndo(IUndoOperation operation)
    {
        _undo.Push(operation);
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void CommitTransaction(UndoTransaction transaction)
    {
        EnsureTransactionOwner(transaction);
        _transaction = null;
        var operation = transaction.Build();
        if (operation is not null)
            PushUndo(operation);
        else
            Changed?.Invoke(this, EventArgs.Empty);
    }

    private void CancelTransaction(UndoTransaction transaction)
    {
        EnsureTransactionOwner(transaction);
        _transaction = null;
        transaction.Rollback();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void EnsureTransactionOwner(UndoTransaction transaction)
    {
        if (!ReferenceEquals(_transaction, transaction))
            throw new InvalidOperationException("Undo transaction ownership mismatch.");
    }

    private sealed class UndoTransaction(UndoManager owner, string name) : IUndoTransaction
    {
        private readonly List<IUndoOperation> _operations = [];
        private bool _completed;

        public bool IsCompleted => _completed;
        public void Add(IUndoOperation operation)
        {
            if (_completed) throw new InvalidOperationException("Undo transaction is already complete.");
            _operations.Add(operation);
        }

        public IUndoOperation? Build() => _operations.Count switch
        {
            0 => null,
            1 => new NamedUndoOperation(name, _operations[0]),
            _ => new CompositeUndoOperation(name, [.. _operations]),
        };

        public void Commit()
        {
            if (_completed) return;
            _completed = true;
            owner.CommitTransaction(this);
        }

        public void Cancel()
        {
            if (_completed) return;
            _completed = true;
            owner.CancelTransaction(this);
        }

        public void Dispose()
        {
            // Visual gestures must opt in to success. Pointer-capture loss, Esc,
            // exceptions, or an early return automatically restore pre-gesture state.
            if (!_completed) Cancel();
        }

        public void Rollback()
        {
            for (var i = _operations.Count - 1; i >= 0; i--)
                _operations[i].Undo();
        }
    }

    private sealed class NamedUndoOperation(string name, IUndoOperation inner) : IUndoOperation
    {
        public string Name { get; } = name;
        public void Undo() => inner.Undo();
        public void Redo() => inner.Redo();
    }

    private sealed class CompositeUndoOperation(string name, IReadOnlyList<IUndoOperation> operations) : IUndoOperation
    {
        public string Name { get; } = name;
        public void Undo()
        {
            for (var i = operations.Count - 1; i >= 0; i--)
                operations[i].Undo();
        }
        public void Redo()
        {
            foreach (var operation in operations)
                operation.Redo();
        }
    }
}
