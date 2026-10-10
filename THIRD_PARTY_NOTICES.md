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


## Visual authoring artwork

Yoake adapts functional SVG artwork from https://github.com/amanosatosi/Aegisub_Toshi-ban
at `28b5156df94cb2538d373db503445d2b07466396`, BSD 3-Clause,
Copyright (c) 2004-2012, Aegisub Project; the full license is retained at
`third_party/licenses/Aegisub-BSD-3-Clause.txt` and packaged in `licenses/`.

Exact source directory: `docs/art-sources/buttons/`. Source filenames:
`visual_standard.svg`, `visual_move.svg`, `visual_rotatez.svg`,
`visual_rotatexy.svg`, `visual_scale.svg`, `visual_clip.svg`,
`visual_vector_clip.svg`, `visual_perspective.svg`, `visual_move_conv_move.svg`,
`eyedropper_tool.svg`, `visual_vector_clip_drag.svg`,
`visual_vector_clip_line.svg`, `visual_vector_clip_bicubic.svg`,
`visual_vector_clip_convert.svg`, `visual_vector_clip_insert.svg`,
`visual_vector_clip_remove.svg`, `visual_vector_clip_freehand.svg`,
`visual_vector_clip_freehand_smooth.svg`.

Adaptations: all coordinates and stroke widths scaled from 64 to 24 units;
fill/stroke colors replaced with semantic currentColor; Inkscape metadata and
unused defs removed. Curves, strokes, caps, joins and fill rules are retained.
The quadrilateral icon is renamed Distort and denotes Mangetsu bilinear
four-corner authoring; it does not introduce Aegisub tag semantics.
No upstream application identity artwork is used.

## Automation Lua runtime

LuaJIT 2.1, https://github.com/LuaJIT/LuaJIT,
commit `c6ffc141a8762b41703f9287d63d93622a13dd8f`.
Copyright Mike Pall and contributors; MIT. Acquired without source modifications
by `ci/build_dependencies.ps1`; its complete COPYRIGHT is packaged as
`licenses/LuaJIT-MIT.txt`. The Yoake bridge and host adapter are original Yoake
code. Lua execution uses interpreter mode for reliable cancellation; the managed
application remains NativeAOT. No upstream Automation library is copied by this
runtime foundation milestone.
