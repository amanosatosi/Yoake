# Third-party notices

This source tree references or packages the following third-party components. Portable binary packages copy the applicable license texts into `licenses/`.

## Avalonia

Avalonia 12.1.3 is MIT licensed, copyright AvaloniaUI OÜ. The license text used by this repository is stored at `third_party/licenses/Avalonia-MIT.txt` and is included in Windows portable packages.

## xUnit.net

xUnit packages are test-only and are not shipped in the Yoake application. xUnit.net is licensed under Apache-2.0 (with upstream-noted exceptions where applicable), copyright .NET Foundation and contributors.

## FFmpeg / zlib / FFMS2 / Mangetsu

These are not vendored as binaries in this source repository. Windows CI builds pinned FFmpeg/FFMS2 versions from `third_party/versions.json`, while Mangetsu intentionally follows the latest `mangetsu` branch head and is rebuilt without a GitHub Actions or vcpkg binary cache on every run. `ci/build_dependencies.ps1` collects upstream license/copyright files, and `ci/package_windows.ps1` includes those texts in the portable package. Mangetsu is the project's libassmod renderer used for live ASS preview. FFmpeg feature/configuration changes must be reviewed for license impact before merging.

## Microsoft Visual C++ runtime

Windows portable packages may include only the x64 Visual C++ runtime DLLs that `dumpbin` reports as required by the pinned FFMS2/FFmpeg closure. They are sourced from the Visual Studio toolchain's `VCToolsRedistDir` and are redistributed under Microsoft's Visual Studio redistributable terms; the packager fails rather than silently depending on a developer-machine-only VC runtime.

## Aegisub

Yoake adapts five functional style-list SVG paths from
https://github.com/amanosatosi/Aegisub_Toshi-ban at commit
`b20d63af149568cbef00f3a22742217ecd915990`:
`docs/art-sources/buttons/arrow_up.svg`, `arrow_down.svg`,
`arrow_up_stop.svg`, `arrow_down_stop.svg`, `arrow_sort.svg`.

Copyright (c) 2004-2012, Aegisub Project. BSD 3-Clause; full license is
`licenses/Aegisub-BSD-3-Clause.txt` in Windows distributions and
`third_party/licenses/Aegisub-BSD-3-Clause.txt` in source.
Modifications: path coordinates scaled from 64 to 24 units, black fill replaced
with semantic currentColor, Inkscape metadata removed, decorative two-unit
black stroke deliberately removed for compact filled silhouettes. No unsupported
stroke/transform is passed to the SVG generator. Application identity is independent.
