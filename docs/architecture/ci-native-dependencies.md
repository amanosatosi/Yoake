# CI / native dependency strategy

Old Yoake's `codex/yoake-foundation` dependency work was inspected first. This repository preserves its useful invariants rather than inventing a parallel system:

- vcpkg commit: `f3419f137f1a1e79b33b880ae6845f873d87820b`
- FFmpeg 7.1.1 / commit `a1328e68877e12ab5a6e5d92a84aefa566783ea5`, dynamic, vcpkg port version 6
- FFMS2 5.0 / commit `25cef14386fcaaa58ee547065deee8f6e82c56a2`, dynamic
- one authoritative PowerShell entry point: `ci/build_dependencies.ps1`
- one stable **build/cache** output root with `ffmpeg`, `ffms2`, licenses, completion sentinels, and `versions.json`
- cache keys derive from immutable pins plus build descriptors

The build/cache tree is intentionally not the portable runtime layout. `ci/package_windows.ps1` asks `dumpbin` for the transitive DLL dependency closure rooted at `ffms2.dll` and copies only that closure into `native/win-x64/`. Required VC14x redistributable DLLs are resolved from `VCToolsRedistDir` only when the closure imports them. `ci/verify_windows_runtime.ps1` rejects leaked development files, verifies manifest hashes, installs the same safe Windows DLL-directory search policy used by the app, and loads `ffms2.dll` by name so missing FFmpeg/zlib/VC runtime dependencies fail CI immediately.

GitHub Actions cache is repository-scoped, and the old repository currently exposes no release assets to bootstrap a cross-repository binary bundle. Therefore this repository's first CI run builds/seeds its own cache. Subsequent runs reuse it.

Mangetsu's known old-Yoake pin is recorded for provenance but deliberately not built in M0; actual integration belongs to M3 and must inspect current Mangetsu ABI first. SoundTouch/Lua are similarly deferred until their owning milestones rather than shipped as unused baggage.

## Managed dependency caching

NuGet caching is intentionally separate from the native dependency caches. `actions/setup-dotnet` caching is not enabled in M0 because its cache mode is keyed from committed `packages.lock.json` files and fails when those lock files are absent. If deterministic NuGet lock files are introduced later, enable managed-package caching together with locked restore; do not couple it to FFmpeg/FFMS2 caches.
