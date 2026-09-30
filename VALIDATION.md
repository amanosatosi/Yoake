# Foundation validation record

## Performed in the artifact-construction environment

- `python ci/source_policy.py` passes after the review fixes.
- All `.axaml` and `.csproj` files parse as well-formed XML.
- GitHub Actions YAML parses successfully.
- All repository JSON parses successfully.
- Native dependency pins remain immutable 40-hex commits and FFmpeg/FFMS2 descriptors preserve the old-Yoake build/cache strategy.
- Functional SVG source policy and the deliberately narrow path-only generator validation pass; the generated icon catalog is up to date.
- The app-identity SVG was rendered with ImageMagick into a multi-resolution Windows `.ico` during validation and inspected as a valid ICO; the generated artifact is not committed because the build regenerates it from the SVG master.
- Static guards confirm portable packaging no longer copies FFmpeg/FFMS2 install trees, uses `native/win-x64/`, emits a runtime hash manifest, and includes the Avalonia MIT text.
- Static guards confirm the smoke test fails on any early exit, command failures are contained/logged, Avalonia command-state refreshes use the UI dispatcher, undo transactions roll back unless committed, and `DocumentSession` is observable.
- Lightweight source/delimiter checks and archive-content checks are run before the source ZIP is produced.

## Not falsely claimed as locally performed

This construction environment does not contain the .NET SDK or PowerShell and cannot resolve GitHub/NuGet through its shell. Therefore it did **not** locally execute `dotnet restore`, compile the Avalonia projects, run xUnit, publish Windows NativeAOT, execute the PowerShell packaging scripts, or launch the Windows executable.

The repository's GitHub Actions jobs are the authoritative executable validation path. `managed-tests` restores/builds/runs the Core test project. `windows-nativeaot` builds/restores the pinned native stack, generates the Windows app icon, publishes `win-x64` NativeAOT, stages only the transitive native runtime DLL closure, validates hashes and safe DLL loading, requires the packaged desktop app to remain alive during the startup smoke interval, then uploads the portable ZIP. A green CI run is required before treating binary validation as satisfied.
