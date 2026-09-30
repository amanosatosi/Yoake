# Native interop rules

Native bindings are implementation details of provider layers. Raw pointers must not become general application currency.

For every native API document: owner, lifetime, allocator, UTF-8 conversion, callback lifetime, thread affinity, cancellation behavior, error mapping, and ABI/version check. Owned handles should use `SafeHandle` where appropriate. Prefer C ABI surfaces and `LibraryImport` source generation.

No FFmpeg structure, FFMS2 handle, Mangetsu pointer, or SoundTouch handle may appear in UI/Core APIs. A provider converts native state into managed values and explicit buffer/lifetime abstractions.

Windows portable packages do **not** mirror native build/install trees. CI computes the transitive runtime DLL closure beginning at `ffms2.dll`, stages only those DLLs together in `native/win-x64/`, and writes a hash manifest. `WindowsNativeRuntime.ConfigurePackagedRuntime()` opts the process into safe default DLL directories and adds that one packaged runtime directory before Avalonia starts. Headers, import libraries, CMake/pkg-config metadata, share trees, and debug/development files must never enter the portable runtime.

The dependency-closure roots are an explicit shipping contract. M0 roots the closure at `ffms2.dll`; when Yoake begins directly loading another native DLL that is not transitively reachable from FFMS2, add that DLL as a runtime entrypoint instead of copying an entire install tree. The packager also resolves required VC14x redistributable DLLs from `VCToolsRedistDir` and fails if such an import cannot be staged.
