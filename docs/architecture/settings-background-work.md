# Settings, logging, and background work

Settings use a versioned record and source-generated System.Text.Json metadata. Normal mode writes to the OS roaming application-data directory; an explicit `yoake.portable` marker opts into config beside the executable.

The background job service provides cancellation and lifetime tracking without a UI dependency. Expensive future work must run through equivalent cancellation-aware infrastructure; never hold a lock while calling back into the UI.

Logging is intentionally small and dependency-free in M0. If a richer logger is proposed later, its NativeAOT/trimming behavior and cost must be justified first.
