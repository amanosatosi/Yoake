# Architecture overview

Yoake is split by *responsibility*, not by feature-count theater.

`Yoake.App` is the composition root only. `Yoake.UI` owns Avalonia shell concerns. `Yoake.Core` owns editor/application infrastructure with no Avalonia dependency. `Yoake.Native` is the boundary where native ABI knowledge will accumulate. Future subtitle/media/rendering assemblies should be added only when the boundary is real enough to justify an assembly.

The initial shell intentionally renders placeholders for Video, Audio, Edit, and Subtitle Grid. These are milestone labels, not fake implementations.

The default spatial model is fixed: left is visual/spatial (`Video -> Visual Tools`), right is temporal/textual (`Audio -> Edit`), and the Grid spans the bottom. On Windows, M0 already extends the client area into a 40-DIP titlebar strip while keeping `WindowDecorations.Full`; tabs therefore sit beside the platform caption buttons instead of below a second titlebar. Other platforms keep normal chrome for now. The invariant is **native window behavior, not a permanently separate titlebar**: Windows snap layouts, maximize/minimize/system buttons, resize hit-testing, DPI behavior, and keyboard/system-menu behavior must remain platform-correct. A borderless homemade window that reimplements those behaviors is not acceptable.
