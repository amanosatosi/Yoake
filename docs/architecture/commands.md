# Command system

Every semantic action has a stable slash-separated ID and metadata. The `CommandRegistry` owns registration, availability, checked state and invocation. UI adapters are deliberately thin and contain no business action logic.

Command failures are contained at the registry boundary, logged through `IAppLog`, and surfaced via `CommandFailed`; they must not escape through `ICommand.Execute`. `RegistryCommand` treats Avalonia as a UI adapter: command-state notifications are marshalled onto `Dispatcher.UIThread` before raising `CanExecuteChanged`. Core command execution stays Avalonia-free and may be invoked from non-UI contexts.

Menus, toolbar buttons, context menus, hotkeys, future command palette, and extension bridges should converge on the registry. IDs are compatibility surface: rename them only with migration intent.
