# Command system

Every semantic action has a stable slash-separated ID and metadata. The `CommandRegistry` owns registration, availability, checked state and invocation. UI adapters are deliberately thin and contain no business action logic.

Command failures are contained at the registry boundary, logged through `IAppLog`, and surfaced via `CommandFailed`; they must not escape through `ICommand.Execute`. `RegistryCommand` treats Avalonia as a UI adapter: command-state notifications are marshalled onto `Dispatcher.UIThread` before raising `CanExecuteChanged`. Core command execution stays Avalonia-free and may be invoked from non-UI contexts.

Menus, toolbar buttons, context menus, hotkeys, future command palette, and extension bridges should converge on the registry. IDs are compatibility surface: rename them only with migration intent.

`AppCommand.GetLabel(context)` and `GetDescription(context)` optionally resolve document-contextual display/help text while keeping immutable metadata as the catalog fallback. UI controls must query the command rather than duplicate history names or other semantic state. `RegistryCommand` exposes observable `Label`, `Description`, and `IsChecked`, and refreshes them with availability on the UI dispatcher. Undo/redo menus use these labels; visual-tool and vector-mode commands own their radio state. Direct vector-mode invocation also selects the existing vector-clip tool, so command palette/hotkey invocation has the same useful result as its toolbar.

Native window close requests and the File/keyboard Exit action invoke `app/exit`. Its semantic action runs the existing per-document unsaved/cancel/save workflow and requests presentation closure only after success. The window retains native decorations and performs no document mutation in its close event. Toolbar visibility is a persisted checked presentation command; one toolbar region collapses its command-bar row while retaining the workspace. The log command opens a bounded, asynchronous UTF-8 tail viewer with refresh/copy and cancellation on close; errors return to the main registry/log boundary.
