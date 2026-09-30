# Document state observation

`DocumentSession` is the observable source of truth for shell-visible document metadata such as `Title`, `Path`, and `IsDirty`. It implements `INotifyPropertyChanged`.

`DocumentTabViewModel` holds a live `DocumentSession` reference and forwards title/dirty notifications instead of snapshotting those values. `MainWindowViewModel` synchronizes only tab lifetime/order/active state when the workspace changes; existing tab VMs are retained. Saving, Save As, renaming, and editor dirty-state changes must update the session rather than rebuilding the tab collection.
