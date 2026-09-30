# Yoake contributor / agent rules

Yoake is a native creative application. Do not trade architecture for a demo.

## Non-negotiable rules

1. **NativeAOT stays green.** Do not add runtime code generation, `Reflection.Emit`, arbitrary `Assembly.Load`, dynamic managed plugin DLL loading, or unconstrained reflection serialization.
2. **Do not casually add dependencies.** Record purpose, license, AOT/trimming behavior, maintenance state, and why .NET/Avalonia cannot already solve the problem.
3. **Commands own semantic actions.** Buttons, menus, context menus, hotkeys, and future command-palette entries invoke stable command IDs. Do not hide real application logic in click handlers.
4. **Editor mutations participate in undo.** Continuous drags become one named transaction. Transactions roll back unless explicitly committed; Esc, pointer-capture loss, exceptions, and cancelled gestures must not leave partial edits behind.
5. **ASS editing will be structured and lossless.** Never implement ASS changes with global regex replacement or naive backslash splitting. Unknown tags/sections must survive round-trip.
6. **Native handles stay behind native/provider boundaries.** Do not spread `IntPtr`, FFmpeg structs, FFMS2 handles, libass/Mangetsu pointers, or SoundTouch handles into application/UI code.
7. **No global current-document state.** Work through workspace/document lifetimes passed explicitly to the code that needs them.
8. **Functional icons remain real SVG sources.** M0 supports only the documented monochrome path subset; do not assume general SVG support. No PNG/JPEG replacement, base64 raster wrapped in SVG, or silent loss of transforms/strokes/fill rules/semantic layers. Keep one theme-neutral functional SVG geometry set and recolor through semantic theme resources; do not fork light/dark icon copies just for color. The app identity SVG is separate and is the master for generated platform icons.
9. **Do not duplicate FFmpeg/FFMS2 acquisition/build logic.** `ci/build_dependencies.ps1` + `third_party/*` are authoritative. Never silently bump native versions. Build/cache trees stay separate from shipping runtime staging; Windows portable runtime DLLs belong together under `native/win-x64/`, not in copied install trees.
10. **Do not disable failing tests to make CI green.** Fix the regression or explicitly document a real platform limitation.
11. **Do not claim TODO/scaffolding as a completed subsystem.** Foundation placeholders are labeled placeholders.
12. **Do not make FFmpeg and FFMS2 competing timeline authorities.** Future media code must implement the central timeline contract described in the architecture docs.
13. **Do not block the UI for expensive work.** Indexing, peaks, spectrograms, scans, analysis, and heavy renderer work must be cancellable/background-capable.
14. **Complex scripts are first-class.** Never assume a UTF-16 code unit equals a user-perceived character; do not enable invariant globalization.
15. **Keep Windows behavior native.** Integrated Chrome-style top tabs are allowed, including extending the client area into decorations, but never at the cost of snap layouts, maximize/minimize/system buttons, resize hit-testing, DPI behavior, or the system menu. Do not use a cursed fake borderless window.

16. **Async commands are UI-safe.** Command failures go through centralized reporting/logging; `ICommand.Execute` must not leak exceptions, and `CanExecuteChanged` must be raised on Avalonia's UI dispatcher.
17. **Document shell state is observable.** Update `DocumentSession` for title/path/dirty changes; do not rebuild tab snapshots just to refresh a dirty dot.

## Before submitting a change

- run `python ci/source_policy.py`
- run managed build/tests
- for release-impacting changes, run the Windows NativeAOT publish path
- regenerate icons with `python tools/generate-icons.py --check`
- update architecture documentation when a boundary changes
