# Undo model

Mutations are represented as semantic `IUndoOperation`s. `UndoManager` owns undo/redo stacks and named transactions. A continuous visual gesture begins one transaction, may execute many atomic changes, and commits as a single user-visible operation when the gesture ends successfully.

Transactions are **rollback by default**. `BeginTransaction()` returns `IUndoTransaction`; callers must call `Commit()` on success. `Cancel()` restores all operations in reverse order immediately, and disposing an uncommitted transaction also rolls back. This is the required path for Esc, pointer-capture loss, exceptions, cancelled drags, or early returns.

A feature is not complete if it mutates editor state outside this model without a documented reason.
