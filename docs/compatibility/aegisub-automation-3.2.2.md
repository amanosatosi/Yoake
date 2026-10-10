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

This is a work-in-progress compatibility ledger, not a claim that scripts
already run in Yoake. “Supported” requires executable regression evidence;
Core implementations without a script/provider/UI path remain partial.
Paths below refer to that exact local source tree, not current Aegisub.

| Behavior | Status | Authority / acceptance evidence |
| --- | --- | --- |
| Lua 5.1, module/unpack/standard libraries, LuaJIT extensions | Partially supported | Real native interpreter CI passes; JIT disabled for cancellation. Full language/C-module fixtures pending. `src/auto4_lua.cpp:LuaScript::Create`, `vendor/luajit` |
| One interpreter per master script; reload/disposal | Partially supported | Native isolation/disposal tests pass; Manager/command reload integration pending. `src/auto4_lua.cpp:Create/Destroy` |
| `.lua` and `.moon` masters | Not implemented yet | `src/auto4_lua.cpp:LuaScriptFactory`, `libaegisub/lua/script_reader.cpp:LoadFile` |
| Metadata and filename fallback; Automation 3 rejection | Partially supported | Native provider implements these; Manager exposure pending. `src/auto4_lua.cpp:LuaScript::Create` |
| `include` search/returns/errors; removed `dofile/loadfile` | Partially supported | Core path and real native Unicode include/return tests; packaged discovery pending. `src/auto4_base.cpp:Script::Script`, `src/auto4_lua.cpp:LuaInclude/Create` |
| `require`, editable package.path, Unicode paths, Moon precedence | Not implemented yet | `libaegisub/lua/script_reader.cpp:module_loader/Install` |
| `register_macro(name, help, run, validate, isactive)` | Partially supported | Real native callback/duplicate/validation/toggle tests pass; command/menu binding pending. `src/auto4_lua.cpp:LuaCommand`; fifth callback is implemented |
| Duplicate macros, stable IDs, command unregister | Not implemented yet | `src/auto4_lua.cpp:RegisterCommand/LuaCommand` |
| Validation boolean + dynamic help, toggle state | Not implemented yet | `src/auto4_lua.cpp:Validate/IsActive` |
| File indexes, input selection/active, returned selection | Not implemented yet | `src/auto4_lua.cpp:selected_rows/operator()`, `automation/tests/automation/selection-set-test.lua` |
| Subtitle projection: info, styles, dialogue | Partially supported | Core and real native mutation tests pass. Only these three classes exposed. `src/auto4_lua_assfile.cpp:LuaAssFile/AssEntryToLua/LuaToAssEntry` |
| Headers, Format, comments outside dialogue, attachments, unknown sections | Intentionally unsupported as Lua line classes | 3.2.2 exposes **only info/style/dialogue**. Yoake must preserve other source lines outside the projection. `src/auto4_lua_assfile.cpp:LuaAssFile/LuaToAssEntry` |
| Style and dialogue fields, raw/section, extra | Partially supported | Field mapping and no-op lexical preservation implemented; nonempty extradata writes explicitly rejected pending support. `src/auto4_lua_assfile.cpp:AssEntryToLua/LuaToAssEntry`; `margin_b` is read but `margin_t` is written; `relative_to` is read-only compatibility data |
| `#subs`, `subs.n`, detached table reads, read-only/expired objects | Not implemented yet | `src/auto4_lua_assfile.cpp:ObjectIndexRead/ObjectGetLen/GetObjPointer` |
| Positive replace/nil delete; zero append; negative insert | Not implemented yet | `src/auto4_lua_assfile.cpp:ObjectIndexWrite`, `automation/tests/automation/basic-tests.lua` |
| append grouped by class; insert at n+1 invokes grouped append | Not implemented yet | `src/auto4_lua_assfile.cpp:ObjectAppend/ObjectInsert`, `automation/tests/automation/appended-lines.lua` |
| delete variadic/table, duplicates, deleterange clamp, ipairs | Not implemented yet | `src/auto4_lua_assfile.cpp:ObjectDelete/ObjectDeleteRange/ObjectIPairs` |
| Lossless unknown fields/Mangetsu text; efficient bulk generation | Partially supported | Core 50,000-event test and actual Lua read/modify/write tests pass. Lua copies losing table provenance need further duplication tests. Yoake stronger guarantee; `AssDocument`, `AssRecord` |
| set_undo_point, implicit final point, multiple points, rollback | Partially supported | Core checkpoint/rollback/history tests and native macro test pass; editor selection/packaged undo pending. `src/auto4_lua_assfile.cpp:LuaSetUndoPoint/ProcessingComplete/Cancel`. Points are queued; failed runs discard **all** pending commits, including earlier points. Successful uncheckpointed tail uses macro display name. |
| parse_karaoke_data, empty syllable zero, tags/text/times | Partially supported | Structured Core/parser/native bridge implemented; new timing/comment/drawing/transform/Unicode regressions await CI. `src/auto4_lua_assfile.cpp:LuaParseKaraokeData`, `src/ass_karaoke.cpp:ParseSyllables` |
| text_extents width/height/descent/external-leading | Partially supported | Windows GDI provider at 64x size, scale applied afterwards; grapheme spacing deviation below. Native regression awaits CI. `src/auto4_base.cpp:CalculateTextExtents` |
| frame_from_ms/ms_from_frame/video_size/keyframes | Not implemented yet | `src/auto4_lua.cpp`; START timecode rounding, video_size returns four values; bind existing timeline |
| file_name/project_properties/decode_path/gettext | Not implemented yet | `src/auto4_lua.cpp`; file_name returns basename or nil; properties contains fourteen fields |
| Clipboard get/set + compatibility wrappers | Not implemented yet | `src/auto4_lua.cpp:clipboard_*`, `automation/include/aegisub/clipboard.lua`; empty clipboard returns nil |
| All ten dialog classes and grid/layout/typed defaults | Not implemented yet | `src/auto4_lua_dialog.cpp`, `automation/tests/automation/config-dialog-test.lua`; **alpha is an Edit control in source** |
| Dialog default/custom buttons, cancellation, third button-ID map | Not implemented yet | `src/auto4_lua_dialog.cpp:LuaDialog/LuaReadBack`; source supports a third argument absent from older docs |
| Dialog RGB/RGBA color string conversion | Not implemented yet | `src/auto4_lua_dialog.cpp:LuaControl::Color`; must test ASS/BGR/alpha conversion |
| Progress task/title/set/cancel, logging levels, debug.out | Not implemented yet | `src/auto4_lua_progresssink.cpp`, progress and trace-level fixtures |
| Background execution, forced loop cancellation, tracebacks | Partially supported | Native isolation, tight-loop cancellation/reuse and traceback tests pass; UI progress and MoonScript line rewriting pending. `src/auto4_lua.cpp:LuaThreadedCall`, `libaegisub/lua/utils.cpp:add_stack_trace` |
| `lfs`, `lpeg`, `luabins` | Not implemented yet | `libaegisub/lua/modules.cpp`, `lua/modules/{lfs.cpp,lpeg.c}`, `vendor/luabins`; lfs module tests + luabins round trips required |
| `aegisub.__re_impl` / `aegisub.__unicode_impl` | Not implemented yet | `libaegisub/lua/modules/{re.cpp,unicode.cpp}`, `automation/tests/modules/{re,unicode}.moon` |
| Shipped includes, wrappers, `aegisub.util` | Not implemented yet | `automation/include`: karaskel, utils, unicode, re, clipboard, cleantags, moonscript and `aegisub/*` |
| Real karaskel collect_head/preproc_line/furigana | Not implemented yet | `automation/include/karaskel.lua`, `automation/tests/automation/test-furi.lua` |
| Real Karaoke Templater + realistic retiming workflow | Not implemented yet | `automation/autoload/kara-templater.lua`, `automation/tests/automation/kara-templater-retime.ass` |
| register_filter, priority/config/run, isolated ASS export | Not implemented yet | `src/auto4_lua.cpp:LuaExportFilter`, `src/ass_export_filter.cpp`, basic-export and name-clash fixtures |
| Automation Manager, script metadata/errors/load/remove/reload | Not implemented yet | `src/dialog_automation.cpp`, `src/command/automation.cpp` |
| App/user autoload; duplicate discovery; document-local scripts | Not implemented yet | `src/auto4_base.cpp:AutoloadScriptManager/LocalScriptManager`; local `~`, `$`, `/` specifiers; preserve missing entries |
| Project Automation Scripts persistence | Not implemented yet | `src/ass_parser.cpp:HeaderToProperty`, `src/auto4_base.cpp:SaveLoadedList`; 3.2.2 stores project properties rather than exposing them as ordinary info lines |
| Draft finalization, batch grid/preview/selection refresh, undo | Not implemented yet | Yoake `MainWindowViewModel`, `EventEditDraft`, `SubtitleEditor` |
| Native dependency staging / NativeAOT / real-window smoke | Not implemented yet | `ci/build_dependencies.ps1`, `third_party/versions.json`, real packaged macro/dialog/mutation/undo scenario required |
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
* Modified relative `package.path` templates resolve against the master directory,
  protecting other document sessions from process CWD changes.
* Nonempty Aegisub extradata writes are rejected explicitly; unknown source and text
  remain preserved. Full typed extradata projection is pending.
* Negative deleterange endpoints are rejected instead of reproducing unsigned
  conversion/wraparound. Header/raw class writes are rejected, as in 3.2.2.
* Lua table-copy helpers lose weak identity; the native bridge now recovers schema
  provenance from exact class/raw fields. Copy/unknown-column regression awaits CI.
* Nonzero GDI spacing uses whole-string shaping plus one space increment per
  grapheme. 3.2.2 instead measures UTF-16 units individually; Yoake preserves
  surrogate pairs, combining sequences and complex-script shaping. Zero-spacing
  uses the same whole-string GDI path as the reference.
* Interpreter/provider tests are not packaged application acceptance evidence.
  Manager, command/menu registration, dialogs, progress, snapshot media/path
  services and macro commit/selection now have application code awaiting CI.
  Document-local persistence now has undo-aware project-property/reference code
  and source regressions awaiting CI. Export filters, complete module fixtures and the
  real packaged macro/dialog/selection/undo scenario remain acceptance work.
