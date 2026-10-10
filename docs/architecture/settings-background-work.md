# Settings, logging, and background work

Settings use a versioned record and source-generated System.Text.Json metadata. Normal mode writes to the OS roaming application-data directory; an explicit `yoake.portable` marker opts into config beside the executable.

`AppSettings.ViewMode` stores one `WorkspaceViewMode`: Default, Audio, Video, or AudioVideo. Missing, damaged or unknown values restore Default. The `view/default`, `view/audio`, `view/video` and `view/audio-video` registry commands select the mode and report checked state; the View menu binds these commands and the model's derived visibility/check properties. This setting describes presentation only and never reopens media.

The existing MainWindow regions remain instantiated. Hidden audio rows/video columns and their splitters collapse to zero; audio remains above the subtitle editor. The window remembers video/text column lengths while video is hidden and uses the existing persisted audio height when audio returns, clamped to leave usable editor space. Mode changes never synchronize the workspace, commit drafts, mutate subtitle history, seek, dispose providers, or alter Automation state. Provider/session ownership remains with the existing per-document model, including while controls are hidden.

The bottom pane reserves 140 DIP for subtitle rows in addition to its header, borders and margins (178 DIP total). The window minimum includes this and the existing audio/editor minimum, so resizing splitters or switching modes cannot crowd the row area out of the client bounds.

The background job service provides cancellation and lifetime tracking without a UI dependency. Expensive future work must run through equivalent cancellation-aware infrastructure; never hold a lock while calling back into the UI.

Logging is intentionally small and dependency-free in M0. If a richer logger is proposed later, its NativeAOT/trimming behavior and cost must be justified first.
