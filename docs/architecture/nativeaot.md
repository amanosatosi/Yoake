# NativeAOT rules

NativeAOT is a baseline, not a later optimization.

The executable sets `PublishAot` and all owned libraries declare `IsAotCompatible`. Trimming/AOT analyzers remain enabled; IL2026 and IL3050 are errors rather than suppressed warnings. Avalonia 12 compiled bindings are used by default.

Forbidden architectural dependencies include arbitrary managed plugin loading, runtime-generated assemblies, Reflection.Emit, dynamic proxies, unconstrained reflection serialization, and dynamic XAML loading. Prefer explicit composition, source-generated `System.Text.Json`, source-generated `LibraryImport` when native bindings arrive, and build-time generated UI resources.

Release CI publishes `win-x64` NativeAOT and launches the resulting executable. The portable artifact is self-contained and must not require an installed .NET runtime.

If arbitrary managed extensions are ever required, keep them out of the NativeAOT process: use a separately versioned JIT-capable PluginHost process over an explicit IPC protocol. Native extensions may later use a deliberately versioned C ABI.
