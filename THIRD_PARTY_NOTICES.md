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

## Aegisub 3.2.2 Automation sources

The subsequently integrated include/autoload libraries and LPeg, luabins, regex
and Unicode sources come from the user's read-only Aegisub 3.2.2 tagged-release
archive, revision 8635, corresponding to https://github.com/Aegisub/Aegisub.
`third_party/automation/provenance.json` records every exact original path,
shipping/source destination, SHA-256 and modification status. Source files are
unchanged; the native helper boundary and lfs implementation are original Yoake
code. Original source license/copyright headers are retained.

Aegisub library licenses vary by file: BSD 3-Clause and ISC, with authors including
Niels Martin Hansen, Rodrigo Braz Monteiro and Thomas Goyne. MoonScript 0.2.5 is
MIT, Copyright 2013 Leaf Corcoran (license verified against tag v0.2.5,
`ea282f23d213a2ad8c784f4f04a907b5085443c0`, README.md). LPeg 0.10 is MIT,
Copyright Lua.org, PUC-Rio; its license is retained from the official 0.10
distribution's `lpeg.html`, alongside the original source copyright 2007.
Luabins is MIT, Copyright 2009-2010 Luabins authors, from `src/luabins.h`.
Full notices are in `third_party/licenses`, copied into the package's `licenses`.

Native module dependencies Boost.Regex/Locale 1.91.0 (BSL-1.0) and ICU 78.3 port 1
(ICU license and constituent data licenses) use the pinned vcpkg registry
`f3419f137f1a1e79b33b880ae6845f873d87820b`. Every installed native port's complete
copyright notice is included by the authoritative dependency builder.
