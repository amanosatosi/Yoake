# Third-party notices

This source tree references or packages the following third-party components. Portable binary packages copy the applicable license texts into `licenses/`.

## Avalonia

Avalonia 12.1.3 is MIT licensed, copyright AvaloniaUI OÜ. The license text used by this repository is stored at `third_party/licenses/Avalonia-MIT.txt` and is included in Windows portable packages.

## xUnit.net

xUnit packages are test-only and are not shipped in the Yoake application. xUnit.net is licensed under Apache-2.0 (with upstream-noted exceptions where applicable), copyright .NET Foundation and contributors.

## FFmpeg / zlib / FFMS2 / Mangetsu

These are not vendored as binaries in this source repository. Windows CI builds the exact pins in `third_party/versions.json`; `ci/build_dependencies.ps1` collects their upstream license/copyright files, and `ci/package_windows.ps1` includes those texts in the portable package. Mangetsu is the project's libassmod renderer used for live ASS preview. FFmpeg feature/configuration changes must be reviewed for license impact before merging.

## Microsoft Visual C++ runtime

Windows portable packages may include only the x64 Visual C++ runtime DLLs that `dumpbin` reports as required by the pinned FFMS2/FFmpeg closure. They are sourced from the Visual Studio toolchain's `VCToolsRedistDir` and are redistributed under Microsoft's Visual Studio redistributable terms; the packager fails rather than silently depending on a developer-machine-only VC runtime.

## Aegisub

No Aegisub asset or source file is copied into this M0 repository. Aegisub/Toshi-ban is used as a behavioral and visual reference; Yoake's functional icons remain separately authored, theme-neutral SVG geometry. If future work actually derives or copies an Aegisub asset, its exact source and BSD attribution must be recorded before merge. See `docs/architecture/asset-provenance.md`.
