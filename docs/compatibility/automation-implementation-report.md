# Automation 4 implementation and review guide

Development branch: `automation-4-layer`. Integration base:
`bda055b2ec673a4b1e1cf4a2b1e60a3d9587ba4e` (`csharp-ban`). The final submitted
commit and its GitHub Actions run are reported with the branch submission;
acceptance requires all four workflow jobs, including the packaged application.
No Yoake, Aegisub or native dependencies were compiled locally.

The read-only reference is `C:\aegisub source\Aegisub-3.2.2`, tagged release
3.2.2 revision 8635, together with the frozen manual at `C:\aegisub source\3.2`.
Historical `automation/v4-docs/*` is not the API specification. The
[compatibility ledger](aegisub-automation-3.2.2.md) records release quirks and
manual/source discrepancies. The [architecture contract](../architecture/automation.md)
describes provider boundaries, ownership, threading, persistence and undo.

## Runtime, dependencies and NativeAOT

The native runtime is LuaJIT 2.1 at
`c6ffc141a8762b41703f9287d63d93622a13dd8f` (MIT), using Lua 5.1 semantics.
The reference bundles LuaJIT 2.0.3. The maintained 2.1 implementation supplies
the required native Lua ABI, BitOp and FFI without a managed interpreter,
reflection serialization or generated managed assemblies. JIT execution is
disabled and `jit.on` is inert so count hooks can interrupt tight Lua loops.

LPeg 0.10, bundled luabins, and MoonScript 0.2.5 are MIT. Release regex/Unicode
implementations use Boost.Regex/Locale 1.91.0 (BSL-1.0) and ICU 78.3 port 1
(ICU and constituent data licenses), acquired through the pinned vcpkg registry
`f3419f137f1a1e79b33b880ae6845f873d87820b`. Copied Aegisub sources retain their
BSD/ISC notices. Exact paths, unchanged-byte SHA-256 hashes and licenses are
recorded in `third_party/automation/provenance.json` and `THIRD_PARTY_NOTICES.md`.
The dependency builder packages full notices for every installed native port.

The measured Automation native artifact contains a 558,592-byte `lua51.dll`
and a 36,952,576-byte `yoake-automation.dll`: about 35.8 MiB combined, principally
the static Boost/ICU implementation. That measurement and artifact hash are in
the architecture document. The existing FFmpeg 7.1.1/FFMS2 5.0 acquisition remains
authoritative; Mangetsu continues its established fresh live-branch build.
Build/cache trees remain separate from `native/win-x64` runtime staging.

Core exposes data contracts, structured subtitle projections and command/export
ownership. `Yoake.Native` owns Lua states, registry callbacks, protected error
boundaries, buffers, GDI and Win32 file dialogs. The UI owns explicit workspace
sessions and platform services. Native protected calls contain Lua longjmp;
managed callbacks return data and never invoke the Lua C API. Static interop and
source-generated settings serialization preserve NativeAOT/trimming behavior.

## Script and editor workflows

* App and user `automation/autoload` roots are scanned deterministically on
  background workers; ordered autoload/include preferences are editable in
  Manager. An explicitly empty autoload list disables discovery. Each master
  owns an isolated interpreter. A failed master cannot publish stale features.
* Document-local scripts use release `~` subtitle-relative, `$` Automation-base,
  and `/` absolute references. Add/remove participate in project undo. Missing
  and malformed references remain inspectable; empty project overrides suppress
  old Script Info values. Save As rebases known references transactionally,
  retains unknown references, and rolls metadata back on a failed atomic save.
* `include` searches the master folder, then configured includes; slash-bearing
  names remain master-relative even inside nested includes. Unicode paths and
  multiple returns work. `require` supports editable `package.path`, module/init
  templates, caching and release MoonScript precedence. `dofile/loadfile` are
  removed as in the release. Relative edited templates are master-relative.
* Shipped release libraries include utils, karaskel, cleantags, clipboard,
  Unicode/regex wrappers and MoonScript; LPeg, luabins, lfs, BitOp and FFI are
  native modules. Luabins round trips nested tables, Unicode and binary strings;
  malformed streams and unsupported values have explicit regressions.
  MoonScript masters/modules compile through the unchanged bundled compiler;
  errors retain full Unicode paths and original source lines, including tail
  calls, without replacing the cancellation hook.
* `register_macro` publishes stable path/name command IDs. Local scripts override
  globals only for their document. Validation and fifth-argument toggle callbacks
  run on private read-only views in workers. Cached help, enabled state and real
  menu check state update on the UI dispatcher. A toggle error does not erase a
  successful validator result. Reload removes old commands and publishes each
  replacement once; state-only updates retain submenu controls.
* The subtitle userdata is one-based info/style/dialogue, with `#subs`, `subs.n`,
  detached reads, positive replace/nil delete, zero append, negative insertion,
  grouped append, insert/delete/deleterange and `ipairs`. Read-only and expired
  views reject writes. Source headers, Format lines, comments, attachments,
  unknown fields/sections and Mangetsu syntax remain lossless outside or through
  the projection. Authoring helpers and cycles are ignored during line writeback.
* Input and returned selections use file indexes. Returned numeric strings use
  Lua coercion; invalid/non-dialogue indexes log and stop the selection prefix.
  Active rows reconcile with returned selection. Successful commits update the
  grid, draft and current Mangetsu preview, and undo/redo restore event identities
  and selection. Drafts/gestures are finalized before capture; tabs retain their
  own document, media and undo histories.
* `set_undo_point` queues named immutable checkpoints. Only successful runs commit
  them, with an implicit macro-named tail if needed. Failure/cancellation discards
  every queued point, preserving live source, dirty state and the redo branch.
  Large generation batches produce one collection reset/document notification.
* `parse_karaoke_data` is a structured read-only ASS scan, including numeric empty
  syllable index zero, relative timing, karaoke kinds and release edge cases.
  `text_extents` validates complete styles and uses native Windows GDI width,
  height, descent and external leading with scale factors. Real karaskel and
  furigana layout run through these services.
* Media APIs capture the actual session's timestamps/keyframes, dimensions,
  aspect ratio and current frame. Frame conversion follows exact release START
  rules and average-rate VFR extrapolation over the central timeline. No extra
  playback clock exists. No-video/default and initialization-context behavior
  are tested. `file_name`, fourteen project properties, path tokens and gettext
  are exposed; gettext currently returns the input because Yoake has no matching
  localization catalog.

## Dialogs, platform services, Manager and export

All ten release dialog classes use real Avalonia controls and grid spans:
label, edit, textbox, intedit, floatedit, dropdown, checkbox, color, coloralpha
and alpha. Alpha follows the exact source's plain edit/string behavior. Numeric
limits, finite float validation, multiline Unicode, hints, default/custom
buttons, third-argument button roles, Cancel/Escape/close and RGB/RGBA readback
are implemented. Color buttons open Yoake's practical ASS authoring picker.
Default OK returns the exact release's truthy empty string; Cancel returns false.

Clipboard get/set marshal to the UI dispatcher, including master initialization
through explicit platform services. Native open/save dialogs preserve Unicode,
working directory, multi-select, existence and overwrite options; source's
directory-second/file-third argument order takes precedence over the frozen
manual's reversed order. Owner HWNDs and cancellable dialog hooks stay native.

Progress has actual title/task/percentage, filtered log output, Cancel and
completed/error Close. The visible log is bounded while centralized logs retain
full accepted output/tracebacks. Messages above the invocation trace level are
filtered before formatting and transport, as in the release (default level 3).
Opt-in diagnostic summaries use the same log without enabling suppressed script
traces. Worker Lua remains cancellable through instruction hooks;
failures pass through UI-safe command reporting. A cancelled/failed interpreter
can subsequently run another macro.

Manager shows scope, filename/path, metadata, feature counts and errors. Stable
commands perform Add, local Remove, Reload, Rescan and search-path edits. Global
removal follows the release's remove-file/rescan workflow. Document reopen reloads
its saved local references.

`register_filter` supplies priority/default selection, unique duplicate names,
read-only configuration and processing callbacks. The real export window embeds
typed settings, filter selection/reordering and Unicode encoding choices.
Filters operate on an isolated ASS copy; cancellation/failure discards it, and
export does not dirty or alter the open document/history. Output is atomically
written after all selected filters succeed.

The unchanged shipped Karaoke Templater executes real collect_head/preproc/GDI,
syllable templates, blank/noblank behavior and realistic retiming. Regression
fixtures assert generated effect text/times/styles and exact undo/redo; the
packaged probe runs a real template after Add, Save As and independent-tab media
loading, checks actual Mangetsu pixels, and reopens the saved local-script file.

## Executable verification

The [performance measurement guide](automation-performance.md) identifies the
instrumented baseline, real-provider workloads, metric boundaries and reference
output oracle. Each CI run uploads its own measurements; performance numbers
never replace the existing correctness or packaged NativeAOT gates.

GitHub Actions runs source policy/hash/generated-asset checks, all Core/UI
workflow tests, real native Automation tests, and Windows NativeAOT/package
verification. The native suite includes unchanged release `re.moon` and
`unicode.moon` assertions, furigana Lua/ASS, Karaoke Templater retiming ASS,
basic export and filter-name-clash fixtures. CI uses committed fixtures and does
not depend on the local reference tree. Additional cases cover lossless field
copies, malformed lines, errors, include/module failures, independent callback
failures, expired views, forced cancellation/reuse, language/modules, filesystem
behavior, 5,000 generated Lua effects and 50,000 Core appends.

The actual packaged `MainWindow` probe verifies macro validation/menu state,
ten-class dialogs, invalid numeric feedback, default/custom acceptance and
dismissal, real Unicode clipboard, progress/log filtering, forced cancellation,
error traceback/reuse, unchanged failure/export history, configured export,
Manager metadata/repeated reload, local persistence/Save As/reopen, real media
APIs, returned grid selection, exact undo/redo and Mangetsu pixel changes. Startup
also covers clean, legacy, damaged, null, irrecoverable and fatal profiles; runtime
closure and native media/rendering provider checks remain required.

## Exact gaps and remaining manual QA

* Nonempty Aegisub extradata projection/writes are not implemented. `extra` reads
  as an empty table; nonempty writes fail explicitly. Raw extradata source remains
  preserved, but scripts depending on typed extradata are not compatible.
* Identical raw lines read under different Format schemas have ambiguous provenance
  after `table.copy`; copied-table writes fail explicitly to protect unknown
  columns. Original detached line tables remain writable.
* Negative deleterange endpoints are rejected rather than reproducing unsigned
  wraparound. An explicit empty selection also clears Yoake's active row; the
  release can retain active focus outside an empty selection.
* Nonzero GDI spacing uses whole-string shaping plus grapheme increments instead
  of release UTF-16-unit measurements, preserving complex-script ergonomics.
* JIT is deliberately unavailable; scripts requiring JIT execution differ. Relative
  edited package.path uses the master folder, and gettext uses identity fallback.
* Export supports ASS with UTF-8/BOM and UTF-16 LE/BE. Legacy code pages and other
  output formats are deferred. Custom format reader/writer registration is absent
  from the actual 3.2.2 host and is intentionally unsupported.
* DependencyControl is deliberately deferred; it is a third-party ecosystem,
  outside this initial release-host acceptance target.

Manual QA remains for native file-picker interactions and overwrite prompts,
physical keyboard/pointer/IME composition in Automation dialogs, ordinary laptop
sizes and Windows DPI/multi-monitor behavior, and a broader external fansub/KFX
script corpus. Automated real-window and shaping assertions do not replace those
device/input checks. The branch remains available for review; it is not merged or
deleted automatically.
