# Aegisub 3.2.2 Automation compatibility

Target: the read-only tree `C:\aegisub source\Aegisub-3.2.2`.
The API/workflow manual is the frozen **3.2 manual** at
`C:\aegisub source\3.2`, particularly `Automation/Lua/{Registration,
Subtitle_file_interface,Dialogs,Progress_reporting,Miscellaneous_APIs}`,
`Automation/Lua/Modules`, `Automation/Manager`, and Karaoke Templater pages.
Use these together with the exact `v3.2.2` implementation. Do **not** use
`automation/v4-docs/*` as the API specification: those historical files contain
superseded interfaces. Where the frozen manual differs from the exact release's
execution behavior, record the discrepancy explicitly and test the implementation.
`build/git_version.h` identifies tagged release **3.2.2, revision 8635**;
its bundled `vendor/luajit/src/luajit.h` identifies LuaJIT **2.0.3**.
Integration base: `bda055b2ec673a4b1e1cf4a2b1e60a3d9587ba4e` on
`csharp-ban`. Development stays on `automation-4-layer`.

This ledger distinguishes tested release-host behavior from explicit deviations.
The packaged NativeAOT probe passed on `473b788` in workflow `38083691966`;
the final branch revision must also pass all four jobs before submission.
See [implementation report](automation-implementation-report.md) for review scope.
“Supported” does not imply compatibility with every third-party script.
Paths below refer to that exact local source tree, not current Aegisub.

| Behavior | Status | Authority / acceptance evidence |
| --- | --- | --- |
| Lua 5.1, module/unpack/standard libraries, LuaJIT extensions | Partially supported | Real native interpreter CI passes; JIT disabled for cancellation. CI additionally covers Lua 5.1 environment/module/coroutine, BitOp/FFI and edited Unicode package.path. `src/auto4_lua.cpp:LuaScript::Create`, `vendor/luajit` |
| One interpreter per master script; reload/disposal | Supported | Native isolation/disposal tests pass; Manager owns reload and command cleanup. Packaged repeated reload and independent document/reopen workflows passed. `src/auto4_lua.cpp:Create/Destroy` |
| `.lua` and `.moon` masters | Supported | Real Lua and MoonScript masters load in native CI; original-source traceback regression added. `src/auto4_lua.cpp:LuaScriptFactory`, `libaegisub/lua/script_reader.cpp:LoadFile` |
| Metadata and filename fallback; Automation 3 rejection | Supported | Native metadata/fallback/rejection implemented and shown in Manager; packaged Manager metadata/reload and shipped autoload discovery passed. `src/auto4_lua.cpp:LuaScript::Create` |
| `include` search/returns/errors; removed `dofile/loadfile` | Supported | Core path and real native Unicode include/return tests; packaged shipped-library discovery passed. `src/auto4_base.cpp:Script::Script`, `src/auto4_lua.cpp:LuaInclude/Create` |
| `require`, editable package.path, Unicode paths, Moon precedence | Supported | Actual include wrappers and bundled MoonScript resolve master/configured paths. Native module/include fixtures pass; CI additionally covers edited Unicode package.path/module-init/cache/Moon precedence. |
| `register_macro(name, help, run, validate, isactive)` | Supported | Real native callback/duplicate/validation/toggle tests pass; stable command/menu binding implemented; actual checkable-menu state/help assertions added to packaged CI. `src/auto4_lua.cpp:LuaCommand`; fifth callback is implemented |
| Duplicate macros, stable IDs, command unregister | Supported | Core command ownership/override/reload tests and native duplicate rejection pass; real command validation/toggle/undo probe is in packaged CI. `src/auto4_lua.cpp:RegisterCommand/LuaCommand` |
| Validation boolean + dynamic help, toggle state | Supported | Native callback results and Core cache/command-state regressions pass; real-window validation/isactive assertions are in packaged CI. `src/auto4_lua.cpp:Validate/IsActive` |
| File indexes, input selection/active, returned selection | Supported | Core and native index/selection tests pass; identity-restoring undo and returned-index redo are asserted by the actual-window probe. `src/auto4_lua.cpp:selected_rows/operator()`, `automation/tests/automation/selection-set-test.lua` |
| Subtitle projection: info, styles, dialogue | Supported | Core and real native mutation tests pass. Only these three classes exposed. `src/auto4_lua_assfile.cpp:LuaAssFile/AssEntryToLua/LuaToAssEntry` |
| Headers, Format, comments outside dialogue, attachments, unknown sections | Intentionally unsupported as Lua line classes | 3.2.2 exposes **only info/style/dialogue**. Yoake must preserve other source lines outside the projection. `src/auto4_lua_assfile.cpp:LuaAssFile/LuaToAssEntry` |
| Style and dialogue fields, raw/section, extra | Partially supported | Field mapping and no-op lexical preservation implemented; nonempty extradata writes explicitly rejected pending support. `src/auto4_lua_assfile.cpp:AssEntryToLua/LuaToAssEntry`; `margin_b` is read but `margin_t` is written; `relative_to` is read-only compatibility data |
| `#subs`, `subs.n`, detached table reads, read-only/expired objects | Supported | Native real-macro length/read tests, validation write rejection and expired userdata after cancellation tests pass. `src/auto4_lua_assfile.cpp:ObjectIndexRead/ObjectGetLen/GetObjPointer` |
| Positive replace/nil delete; zero append; negative insert | Supported | Core and actual native mutation tests pass, including structured lossless source writeback. `src/auto4_lua_assfile.cpp:ObjectIndexWrite`, `automation/tests/automation/basic-tests.lua` |
| append grouped by class; insert at n+1 invokes grouped append | Supported | Core class grouping and boundary mutation tests pass; native macro insertion participates in transactional commit. `src/auto4_lua_assfile.cpp:ObjectAppend/ObjectInsert`, `automation/tests/automation/appended-lines.lua` |
| delete variadic/table, duplicates, deleterange clamp, ipairs | Supported | Core release-quirk tests and native iteration/mutation coverage pass; negative deleterange wraparound remains an explicit deviation. `src/auto4_lua_assfile.cpp:ObjectDelete/ObjectDeleteRange/ObjectIPairs` |
| Lossless unknown fields/Mangetsu text; efficient bulk generation | Supported | Core 50,000-event test and actual Lua read/modify/write tests pass. Actual table.copy with unknown columns and cyclic helpers passes native CI. Yoake stronger guarantee; `AssDocument`, `AssRecord` |
| set_undo_point, implicit final point, multiple points, rollback | Supported | Core checkpoint/rollback/history tests and native macro test pass; editor selection/packaged exact undo/redo and selection passed. `src/auto4_lua_assfile.cpp:LuaSetUndoPoint/ProcessingComplete/Cancel`. Points are queued; failed runs discard **all** pending commits, including earlier points. Successful uncheckpointed tail uses macro display name. |
| parse_karaoke_data, empty syllable zero, tags/text/times | Supported | Core parser tests pass; native numeric-zero/contiguous-array regression fixes real karaskel writeback. Empty syllable generation and noblank behavior remain explicitly tested. `src/auto4_lua_assfile.cpp:LuaParseKaraokeData`, `src/ass_karaoke.cpp:ParseSyllables` |
| text_extents width/height/descent/external-leading | Partially supported | Native GDI scaling/grapheme-spacing and unmodified furigana/karaskel layout tests pass; declared spacing deviation retained. `src/auto4_base.cpp:CalculateTextExtents` |
| frame_from_ms/ms_from_frame/video_size/keyframes | Supported | Immutable compatibility view is bound to central media timestamps and keyframes; Core START-boundary VFR fixture passes. CFR, VFR extrapolation and single-frame fixtures pass Core CI; actual loaded-media assertions are in packaged CI. `src/auto4_lua.cpp`; START timecode rounding, video_size returns four values; bind existing timeline |
| file_name/project_properties/decode_path/gettext | Partially supported | Captured session services expose all fourteen properties, basename, token decoding and translation identity fallback. Unicode/document reference tests pass; localization fidelity pending. `src/auto4_lua.cpp`; file_name returns basename or nil; properties contains fourteen fields |
| Clipboard get/set + compatibility wrappers | Supported | Native adapter and dispatcher-marshalled clipboard service implemented; initialization/platform-service native tests pass; actual Unicode clipboard get/set/restore is asserted in packaged CI. `src/auto4_lua.cpp:clipboard_*`, `automation/include/aegisub/clipboard.lua`; empty clipboard returns nil |
| All ten dialog classes and grid/layout/typed defaults | Supported | Typed shared control panel implemented for macros and embedded export configuration. Actual packaged ten-class layout/readback and invalid finite-float probe passed; dismissal regressions cover custom Cancel, Escape and window close. `src/auto4_lua_dialog.cpp`, `automation/tests/automation/config-dialog-test.lua`; **alpha is an Edit control in source** |
| Dialog default/custom buttons, cancellation, third button-ID map | Partially supported | Default/custom results, Escape/close and third button-ID map implemented. Native transport and packaged modal regressions added; packaged modal acceptance passed; expanded cancellation/dismissal is additionally asserted. `src/auto4_lua_dialog.cpp:LuaDialog/LuaReadBack`; source supports a third argument absent from older docs |
| Dialog RGB/RGBA color string conversion | Supported | Canonical ASS color authoring dialog backs color buttons; RGB/RGBA defaults and return strings passed packaged verification. `src/auto4_lua_dialog.cpp:LuaControl::Color`; must test ASS/BGR/alpha conversion |
| Native open/save file dialogs | Partially supported | Unicode Win32 dialogs preserve directory, support multiple/non-existing/overwrite flags, and cancel through a native window hook. Native request/result transport tests pass; actual picker interaction remains manual/packaged QA. Exact release argument order differs from frozen manual, as recorded below. |
| Progress task/title/set/cancel, logging levels, debug.out | Supported | Cancellable modal progress, coalesced reporting, bounded display plus full centralized log implemented. Native cancellation tests pass; packaged forced cancellation/log-level/failure/reuse workflow passed. `src/auto4_lua_progresssink.cpp`, progress and trace-level fixtures |
| Background execution, forced loop cancellation, tracebacks | Supported | Native isolation, tight-loop cancellation/reuse and traceback tests pass; MoonScript full-path/original-line and hook cancellation/reuse regressions pass native CI; actual progress/cancellation/failure probe is in packaged CI. `src/auto4_lua.cpp:LuaThreadedCall`, `libaegisub/lua/utils.cpp:add_stack_trace` |
| `lfs`, `lpeg`, `luabins` | Supported | Real native LPeg/luabins and narrow Unicode filesystem surface pass native module fixtures. Windows filesystem Unicode/error/iterator-close/timestamp regressions pass native CI; POSIX upstream lfs tests require platform adaptation. |
| `aegisub.__re_impl` / `aegisub.__unicode_impl` | Supported | All unmodified release regex and Unicode module assertions pass in native CI through bundled MoonScript. Exact copied source hashes are checked by source policy. |
| Shipped includes, wrappers, `aegisub.util` | Supported | All release includes/wrappers and autoload sources are shipped unchanged with verified hashes; native wrapper/module/karaskel/export fixtures pass. Full packaged discovery remains pending. |
| Real karaskel collect_head/preproc_line/furigana | Supported | Unmodified release furigana script/ASS fixture runs real collect_head/preproc/layout with GDI and exact undo in native CI. |
| Real Karaoke Templater + realistic retiming workflow | Supported | Unmodified templater runs through real Lua/karaskel/GDI. Both initial blank/noblank behaviors and the unmodified release retiming fixture pass native CI, including generated timing and exact undo/redo. |
| register_filter, priority/config/run, isolated ASS export | Supported | Real priority/default and chosen order, duplicate suffixes, readonly configuration, isolated working-copy chain and embedded settings/export window implemented. Native release export/name-clash fixtures pass; packaged configured isolated export interaction passed. `src/auto4_lua.cpp:LuaExportFilter`, `src/ass_export_filter.cpp`, basic-export and name-clash fixtures |
| Automation Manager, script metadata/errors/load/remove/reload | Supported | Metadata, load/remove/reload/rescan, errors and configurable search paths implemented through stable commands. Actual Manager selection/details/repeated reload/removal of stale macro IDs is asserted in packaged CI. `src/dialog_automation.cpp`, `src/command/automation.cpp` |
| App/user autoload; duplicate discovery; document-local scripts | Supported | Default token directories and configurable ordered search paths implemented; globals and document locals have independent ownership. Missing references are retained. Core path/reference/command fixtures pass. `src/auto4_base.cpp:AutoloadScriptManager/LocalScriptManager`; local `~`, `$`, `/` specifiers; preserve missing entries |
| Project Automation Scripts persistence | Supported | 3.2.2 ~, $, absolute markers round-trip; Add/Remove edit project properties through undo and reopening synchronizes locals. Core persistence/empty-override and Save As rebasing/failed-save rollback tests pass; packaged Add/Save As/reopen workflow passed. `src/ass_parser.cpp:HeaderToProperty`, `src/auto4_base.cpp:SaveLoadedList`; 3.2.2 stores project properties rather than exposing them as ordinary info lines |
| Draft finalization, batch grid/preview/selection refresh, undo | Supported | Macro execution finalizes drafts and gestures, runs worker copies, then batches grid/draft/preview/selection updates. Checkpoint undo restores identities and final redo returned indexes; Core tests and actual-window probe added. |
| Native dependency staging / NativeAOT / real-window smoke | Supported | CI builds pinned runtime/modules and stages native DLLs, licenses and measured sizes. Expanded actual-window Automation workflows, all startup profiles, runtime closure and native provider checks passed packaged workflow 38083691966. The final revision includes additional menu/dismissal regressions. |
| Custom file-format reader/writer registration | Intentionally unsupported | Not registered by 3.2.2 `LuaScript::Create`; old docs alone are not evidence |
| DependencyControl | Not implemented yet; outside initial acceptance | Third-party ecosystem, not the 3.2.2 host specification; do not claim compatibility |

## Non-obvious behaviors to retain

The frozen manual is checked alongside source, with these known discrepancies:
* Default dialog OK is described as `true` by `Automation/Lua/Dialogs`; exact
  3.2.2 `LuaDialog::LuaReadBack` returns the default button's empty label string.
  Yoake follows that truthy empty-string return; Cancel remains `false`.
* The manual describes floor/ceiling frame conversion. Exact 3.2.2 calls use
  `agi::vfr::START`, including midpoint `TimeAtFrame` and shifted `FrameAtTime`.
  Yoake's compatibility view uses these boundary rules over the central media
  provider's timestamps; it does not create another playback timeline.
* `alpha` is documented as a color class but exact 3.2.2 constructs `Edit`.
  Yoake retains the edit/string behavior. The manual's nonpositive width/height
  fallback to one is retained for usable dialog layout.
* The frozen Dialogs manual lists open/save arguments as
  `(title, default_file, default_dir, wildcards, ...)`. Exact 3.2.2
  `LuaDisplayOpenDialog` / `LuaDisplaySaveDialog` read directory at argument two
  and filename at three. Yoake follows release execution order and records it
  in a native transport regression. `must_exist` defaults true; save's fifth
  argument suppresses the overwrite prompt when truthy.

* `include("name.lua")` searches the master script directory followed by configured
  include directories. Names containing either slash are resolved against the
  **master** directory, even inside a nested include. Absolute paths are accepted.
* `require` checks a same-name `.moon` before `.lua`; the loader is installed before
  the master script and initializes `moonscript.loadstring` eagerly.
* Subtitle reads return new tables. Editing one does not write until assignment.
  Unknown table fields are ignored by 3.2.2; unknown **ASS** source is retained by
  Yoake outside this compatibility view.
* Source `ObjectDelete` does not deduplicate its sorted index list. A duplicate
  prevents consumption of subsequent indexes. Record this oddity in regression
  evidence rather than silently “fixing” it for old scripts.
* 3.2.2 has no host strong sandbox. Trusted scripts can access Lua I/O, OS functions,
  and native module facilities. NativeAOT restrictions apply to the managed host,
  not to Lua language execution. Yoake must not load managed plugin assemblies.

## Current explicit deviations and pending integration

* JIT execution is disabled and `jit.on` is inert so instruction hooks can cancel
  loops reliably. Lua 5.1 language/BitOp/FFI libraries remain native LuaJIT features.
  Performance stabilization retains that policy. The pinned recorder's optional
  `LUAJIT_ENABLE_CHECKHOOK` requires a different build and asynchronous hook
  installation/lifetime policy; it is not enabled or claimed as verified here.
  See [pinned recorder](https://raw.githubusercontent.com/LuaJIT/LuaJIT/c6ffc141a8762b41703f9287d63d93622a13dd8f/src/lj_record.c).
* Modified relative `package.path` templates resolve against the master directory,
  protecting other document sessions from process CWD changes.
* Nonempty Aegisub extradata writes are rejected explicitly; unknown source and text
  remain preserved. Full typed extradata projection is pending.
* Negative deleterange endpoints are rejected instead of reproducing unsigned
  conversion/wraparound. Header/raw class writes are rejected, as in 3.2.2.
* Multi-argument append validates every argument before changing Yoake's working
  view. The release's `ObjectAppend` inserts each converted argument immediately,
  so a Lua `pcall` catching a later conversion error can retain the valid prefix.
  Yoake's existing atomic validation policy is retained by the grouped fast path.
* Returned selection values use Lua numeric-string coercion and ignore nonnumeric
  helpers; invalid/non-dialogue indexes log an error and stop readback as in the
  release. An explicit empty selection clears Yoake's active row as well; the
  release can retain a focused active row outside its empty selected set.
* Lua table-copy helpers lose weak identity; the native bridge now recovers schema
  provenance from exact class/raw fields. Copy/unknown-column and cyclic authoring-helper regressions pass native CI. When identical raw lines have distinct Format schemas, a copied-table mutation is rejected explicitly; original detached tables retain exact schema identity. This rare integrity guard has a native regression.
* Nonzero GDI spacing uses whole-string shaping plus one space increment per
  grapheme. 3.2.2 instead measures UTF-16 units individually; Yoake preserves
  surrogate pairs, combining sequences and complex-script shaping. Zero-spacing
  uses the same whole-string GDI path as the reference.
* Interpreter/provider tests are not packaged application acceptance evidence.
  The expanded actual-window probe checks dialogs, Unicode clipboard, forced loop
  cancellation, full failure traces, interpreter reuse and configured isolated
  export in addition to mutation/selection/undo and actual preview pixels. These workflows passed packaged CI on `473b788`; the final revision must
  also pass its expanded menu and dialog-dismissal assertions. Manager/Add/Save As/reopen and shipped Karaoke Templater are also
  exercised by that probe. Native file-picker interaction remains manual QA; source/provider checks alone do not close those gaps.
