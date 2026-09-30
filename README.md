# Yoake — greenfield native editor foundation

This repository is the **new C#/Avalonia Yoake**, not an in-place port of the old Qt/QML architecture. The first milestone deliberately establishes the runtime and editor architecture before implementing subtitle/media features.

## Baseline

- .NET 10 LTS, SDK pinned by `global.json`
- Avalonia 12.1.3
- Windows-first desktop UX, Linux kept architecturally viable
- NativeAOT is a design constraint from the first commit
- No Electron, WebView shell, reflection-heavy DI container, or arbitrary managed plugin loading
- Native libraries live behind `Yoake.Native` / future provider boundaries

## What exists now

- Windows browser-style document tabs integrated into the titlebar region while retaining Full platform decorations/caption buttons; other platforms keep normal chrome
- corrected workspace: **Video + Visual Tools** on the left, **Audio + Edit** on the right, **Subtitle Grid** across the bottom
- resizable splitters for all required boundaries
- stable command registry and UI command adapters
- contextual hotkey resolver with conflict detection
- rollback-by-default transaction-aware undo manager with explicit commit/cancel
- observable workspace/document lifetime foundation with live tab title/dirty updates
- versioned, source-generated JSON settings with explicit portable-mode marker support
- cancellation-aware background job service and small logging abstraction
- Light/Dark/System theme foundation
- deliberately narrow monochrome SVG functional-icon pipeline plus SVG-master → Windows `.ico` application identity generation
- deterministic FFmpeg/FFMS2 dependency preparation derived from old Yoake's pins and layout
- Windows CI for native dependencies, managed build/tests, NativeAOT publish, strict startup smoke check, transitive runtime-DLL staging/verification, and portable ZIP
- architecture and agent guidance

Actual ASS parsing, media decode/playback, waveform/spectrogram, Mangetsu rendering, visual editing, timing modes, and Automation are intentionally **not** faked in this milestone. Their panels are explicit placeholders.

## Build

Windows app builds generate the executable/window `.ico` from the SVG master and therefore require ImageMagick's `magick.exe` on `PATH`. CI pins the Chocolatey ImageMagick package version; local developers can install the same tool or another compatible ImageMagick 7 build. Functional UI icons do not use ImageMagick at runtime.

```powershell
# .NET SDK 10.0.401 + ImageMagick 7 on PATH for Windows app builds
dotnet restore Yoake.sln
dotnet build Yoake.sln -c Debug
dotnet test tests/Yoake.Core.Tests/Yoake.Core.Tests.csproj -c Debug

dotnet publish src/Yoake.App/Yoake.App.csproj `
  -c Release -r win-x64 --self-contained true -p:PublishAot=true
```

Native dependency preparation is authoritative in one place:

```powershell
.\ci\build_dependencies.ps1 -OutputRoot .ci-cache\native -Stage All
```

## Portable mode

Normal mode stores settings under the user's application-data directory. To opt into Windows-style portable settings, create an empty file named `yoake.portable` beside the executable before launch.

## Repository map

- `src/Yoake.Core` — application/editor-domain infrastructure; no Avalonia dependency
- `src/Yoake.Native` — native dependency contracts/layout only; no native pointer leakage into UI
- `src/Yoake.UI` — Avalonia shell, theme service, command adapters, generated icon geometry
- `src/Yoake.App` — composition root and executable
- `tests/Yoake.Core.Tests` — command/hotkey/undo/settings/workspace tests
- `third_party` — immutable native dependency metadata/build descriptors
- `ci` — dependency, package, smoke, and source-policy scripts
- `docs/architecture` — architectural contracts future work must preserve
- `luna.md` — contributor/agent rules

See `docs/architecture/overview.md` before adding a subsystem.

## License

Yoake is licensed under the BSD 3-Clause License; see `LICENSE`. Third-party license texts shipped with the portable package live under `third_party/licenses` plus the native license bundle collected by CI.
