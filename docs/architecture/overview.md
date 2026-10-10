# Architecture overview

`Yoake.App` is the explicit composition root. `Yoake.Core` owns the source model and semantic editing infrastructure without Avalonia or native-library references. `Yoake.Native` owns native pointers/ABI details. `Yoake.UI` adapts commands, drafts, workspace state and native provider results to Avalonia controls. No global current-document singleton or runtime managed plugin loading is introduced.

The workspace remains Video → Visual Tools on the left, Audio → Event Editor on the right, and a virtualized Subtitle Grid across the bottom. Windows tabs extend into a titlebar strip with Full native decorations, preserving system caption buttons, resizing and snapping. Splitters and compact desktop controls remain.

Each `DocumentState` in the workspace view model owns a `SubtitleEditor` (document plus undo history), media provider session, waveform, selected event and paused media time. Tab switches stop playback, save that state and invalidate obsolete frame requests. Media loading captures the owning state before awaiting; an inactive document's completion cannot replace another tab's preview. Closing cancels indexing/analysis and disposes native sessions away from the UI thread.

`IEditorDialogs` is the small platform boundary for file pickers, unsaved prompts, clipboard transfers and document dialogs. MainWindow code adapts focus/hotkeys, selection and pointer coordinates; semantic mutations live in the command model and `SubtitleEditor`. Dialogs have stable command registries and share the originating document editor. Native OS dialogs stay in the UI layer.

See `subtitle-editing.md` for source/undo rules, `media-timeline.md` for clocks and analysis, and `nativeaot.md` for runtime constraints.


On Avalonia 12, extending the client area creates a framework decoration overlay. Yoake suppresses only `PART_TitleTextPanel` on the main window; the window title remains available to Windows/taskbar accessibility. Framework caption buttons retain platform decoration roles and Win32 retains caption/resize/system-menu behavior. Caption clearance comes from the actual overlay button bounds, with DPI-aware DWM/system-metric fallback before those bounds are available. Layout, scaling and maximize changes recalculate clearance. NativeAOT packaged verification checks the redundant caption is hidden and tabs stop before the measured button area; monitor transitions and Windows snap flyouts still require interactive QA.
