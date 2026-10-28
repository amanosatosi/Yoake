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

The contextual default keyboard table is adapted from
`TypesettingTools/Aegisub`, `src/libresrc/default_hotkey.json`, at exact
3.5.0 release commit `43af875ea01e80052cc6c44f24fc8c5258261c6d`.
`tools/generate_aegisub_hotkeys.py` preserves all common-platform bindings and
contexts in a C# source table. Separate mapping normalizes key names and
existing Yoake command IDs; Yoake additions and user overrides remain separate.
The exact 3.2.2 table at `f743d1411e09cbb2bd34ddd2d4b6738101fab710`
is retained in the source-derived compatibility inventory for the delta audit.
Copyright (c) 2004-2012, Aegisub Project; BSD 3-Clause. The upstream `LICENCE`
applies this license to headerless files. The existing full BSD text below is
also included in portable distributions for this adaptation.

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

## File, undo and audio action artwork

Yoake adapts the following functional icons from
https://github.com/amanosatosi/Aegisub_Toshi-ban at commit
`28b5156df94cb2538d373db503445d2b07466396`:
`docs/art-sources/buttons/new_toolbutton.svg`, `open_toolbutton.svg`,
`save_toolbutton.svg`, `undo_button.svg`, `redo_button.svg`,
`button_playsel.svg`, and `button_stop.svg`.
The matching files in the user's local fork were checked for license headers,
as were the root-to-source directory levels for license exceptions; none were
found. The fork's root `LICENCE` therefore applies: Copyright (c) 2004-2012,
Aegisub Project, BSD 3-Clause. The full text is retained in
`third_party/licenses/Aegisub-BSD-3-Clause.txt` and distributed in
`licenses/Aegisub-BSD-3-Clause.txt`.

The artwork was scaled from 64 to 24 units, stripped of editor metadata and
converted to currentColor. The document, folder and floppy use monochrome
outlines in place of colored fills; their line widths were adjusted for small
toolbar sizes. Undo, redo and audio silhouettes retain their curves and strokes.
No Aegisub application or logo branding is included.

The expanded command, timing, transport, and editor icon set is also adapted
from the same fork and commit, in `docs/art-sources/buttons/`:

- `jumpto_button.svg`, `zoom_in_button.svg`, `zoom_out_button.svg`,
  `video_to_substart.svg`, `video_to_subend.svg`, `substart_to_video.svg`,
  `subend_to_video.svg`, `select_visible_button.svg`,
  `snap_subs_to_scene.svg`, `shift_to_frame.svg`, `style_toolbutton.svg`,
  `properties_toolbutton.svg`, `attach_button.svg`,
  `font_collector_button.svg`, `automation_toolbutton.svg`,
  `shift_times_toolbutton.svg`, `resample_toolbutton.svg`,
  `options_button.svg`, `toggle_tag_hiding.svg`, `button_motion_track.svg`.
- `button_prev.svg`, `button_next.svg`, `button_playline.svg`,
  `button_playfivehbefore.svg`, `button_playfivehafter.svg`,
  `button_playfirstfiveh.svg`, `button_playlastfiveh.svg`,
  `button_playtoend.svg`, `button_leadin.svg`, `button_leadout.svg`,
  `button_audio_commit.svg`, `button_audio_goto.svg`,
  `toggle_audio_autocommit.svg`, `toggle_audio_nextcommit.svg`,
  `toggle_audio_autoscroll.svg`, `toggle_audio_spectrum.svg`,
  `toggle_audio_link.svg`, `kara_spectrogram_timing.svg`,
  `kara_split_accept.svg`, `kara_split_cancel.svg`,
  `button_play.svg`, `button_pause.svg`, `toggle_video_autoscroll.svg`.
- `save_as_toolbutton.svg`, `open_video_menu.svg`, `close_video_menu.svg`,
  `open_audio_menu.svg`, `close_audio_menu.svg`,
  `open_timecodes_menu.svg`, `save_timecodes_menu.svg`,
  `close_timecodes_menu.svg`, `open_keyframes_menu.svg`,
  `save_keyframes_menu.svg`, `close_keyframes_menu.svg`, `find_button.svg`,
  `find_next_menu.svg`, `find_replace_menu.svg`, `copy_button.svg`,
  `cut_button.svg`, `delete_button.svg`, `paste_button.svg`,
  `button_bold.svg`, `button_italics.svg`, `button_underline.svg`,
  `button_strikeout.svg`, `button_fontname.svg`, `export_menu.svg`,
  `about_menu.svg`, `select_lines_button.svg`, `detach_video_menu.svg`,
  `show_video_details_menu.svg`, `use_dummy_video_menu.svg`.

The local files have no license headers, and no intervening license files were
found in `docs/`, `docs/art-sources/`, or `docs/art-sources/buttons/`;
the fork's root `LICENCE` applies the BSD 3-Clause text cited above. The
selected local SVGs were compared with the listed commit. The 64-unit paths
were scaled to 24 units; colors and gradients were replaced by theme-aware
foreground, and overlapping backgrounds were made outlines to keep details
visible. Dashed outlines in shift/resample were redrawn as explicit path
segments. The 16-unit motion tracker and 32-unit karaoke spectrogram were
redrawn as compact paths. Karaoke accept/cancel keeps the source boxes and
foreground check/cross while omitting the faint miniature lettering and arrows.
Start/end timing actions use theme-aware green/red
foregrounds, and selection/line playback uses blue/red, to retain their
functional distinctions. Source-to-command
mappings and exceptions are recorded in `docs/architecture/aegisub-icon-migration.md`.

## Bundled ASSDraw fork

The independent ASSDraw3.exe is built from https://github.com/amanosatosi/assdraw
at dda58f913e16743d63ef13bef1beeea0323bd653, without source changes. It preserves
ai-chan's historical BSD attribution and the GPL-2.0-or-later notices in
src/agg_bcspline.*, src/agg_conv_bcspline.h and src/agg_vcgen_bcspline.*.
The executable is not described as BSD-only. Its static dependencies are
wxWidgets 3.2.11 (wxWindows Library Licence plus bundled component notices) and
AGG c4f36b4432142f22c0bf82c6fbdb41567a236be2 (historical permissive AGG copying notice).

Full license texts and notices ship under licenses/assdraw. Complete corresponding
source archives, dependency sources, build instructions and integrity manifest
ship under sources/assdraw in the same portable download. They accompany the
separate executable rather than relying on an expiring Actions artifact or a
source-hosting promise. No upstream license header is removed or reassigned.
This aggregate distribution adds no restriction to users' source modification or
redistribution rights under the applicable ASSDraw licenses. Yoake does not link
to ASSDraw, embed its code or introduce IPC; its established Copy ASS clipboard
workflow is unchanged.

## Automation Lua runtime

LuaJIT 2.1, https://github.com/LuaJIT/LuaJIT,
commit `c6ffc141a8762b41703f9287d63d93622a13dd8f`.
Copyright Mike Pall and contributors; MIT. Acquired without source modifications
by `ci/build_dependencies.ps1`; its complete COPYRIGHT is packaged as
`licenses/LuaJIT-MIT.txt`. The Yoake bridge and host adapter are original Yoake
code. Lua execution uses interpreter mode for reliable cancellation; the managed
application remains NativeAOT. No upstream Automation library is copied by this
runtime foundation milestone.

## Aegisub 3.5.1 Automation sources

The separate `automation-3.5-above` Lua and MoonScript sources are copied from
the user-supplied Aegisub 3.5.1 archive, https://github.com/TypesettingTools/Aegisub.
The archive has no verified Git revision. Exact paths, hashes and modification
status are in `third_party/automation/aegisub-3.5.1-provenance.json`. Each file's
own license header applies; the root BSD-3-Clause license applies to headerless
files, and its full text is packaged in `licenses/Aegisub-BSD-3-Clause.txt`.

## Aegisub 3.2.2 Automation sources

The subsequently integrated include/autoload libraries and LPeg, luabins, regex
and Unicode sources come from the user's read-only Aegisub 3.2.2 tagged-release
archive, revision 8635, corresponding to https://github.com/Aegisub/Aegisub.
`third_party/automation/provenance.json` records every exact original path,
shipping/source destination, SHA-256 and modification status. This includes
unmodified release regression fixtures under
`tests/Yoake.Automation.Tests/Fixtures`. Their original license headers are
retained; Yoake's module assertion runner is original test code. Source-policy
checks verify raw SHA-256 hashes, and Git attributes preserve the recorded bytes.
Except for the two documented Mangetsu adapter hooks, the recorded release source files are unchanged; the native helper boundary and lfs implementation are original Yoake code. Original source license/copyright headers are retained.

The Mangetsu Karaskel adapter is copied without modification from the user-supplied
`Aegisub_Toshi-ban-release/automation/include/mangetsu-karaskel.lua` archive from
https://github.com/amanosatosi/Aegisub_Toshi-ban. The archive has no Git metadata;
its exact SHA-256 and source path are recorded in
`third_party/automation/mangetsu-adapter.json`. Its headerless source is covered
by the fork's root BSD-3-Clause LICENCE, copyright (c) 2004-2012 Aegisub Project;
the full text is included in `licenses/Aegisub-BSD-3-Clause.txt` in packages.

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
