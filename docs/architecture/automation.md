# Automation 4 implementation contract

Status: design and implementation in progress on `automation-4-layer`.
Acceptance is meaningful old 3.2.2 macro/KFX work in the packaged NativeAOT app,
not merely a runtime or a Manager window. See the
[compatibility ledger](../compatibility/aegisub-automation-3.2.2.md).

## Runtime decision

Use a native LuaJIT provider with Lua 5.1 semantics. The reference release bundles
LuaJIT 2.0.3. Yoake pins the maintained LuaJIT 2.1 branch at
`c6ffc141a8762b41703f9287d63d93622a13dd8f` in `third_party/versions.json`.
The native-provider CI job exercises its real interpreter, isolation, Unicode
loaders, mutation, validation/toggle and tight-loop cancellation. Full 3.2.2 module
parity still needs integration evidence. No Lua 5.2 compatibility build flag is
enabled. The interpreter runs with JIT disabled, and `jit.on` cannot re-enable it,
so count hooks can interrupt otherwise non-cooperative scripts.

LuaJIT is MIT licensed, implements the Lua 5.1 API/ABI, supports Windows x64,
and has Linux/macOS implementations. Its native DLL and modules add package size,
build time, native lifetime and ABI responsibilities. Measure the shipping binary
cost from CI. .NET/Avalonia do not implement Lua, LPeg or luabins. A managed Lua
package would require independent evidence of Lua 5.1/C-module parity, AOT/trimming
safety, and performance before substitution; no such dependency has been added.
References: [LuaJIT compatibility](https://luajit.org/extensions.html),
[platform and maintenance status](https://luajit.org/status.html),
[Windows DLL naming/installation](https://luajit.org/install.html),
[license](https://luajit.org/luajit.html).

Native state and C ABI calls belong in `Yoake.Native`. Core contracts and UI never
receive Lua state pointers, registry references or native handles. A native shim
must catch Lua errors/longjmp on the native side; a Lua error must never unwind
through a managed callback. `third_party/automation/yoake_lua.c` protects runtime
initialization/execution with `lua_cpcall` and calls managed code only with a UTF-8
data request. Reverse callbacks catch managed exceptions and return byte-escaped
data-only Lua literals; they never call the Lua C API. The shim releases response
buffers after returning them or after any protected failure. This transport is a
correctness-first implementation; its encoding/allocation cost must be measured
against real KFX before acceptance. NativeAOT uses static interop, no generated
managed assemblies, reflection-based marshalling, managed plugin loading or
reflection-heavy serializers. Use the existing safe native search layout.

## Lifetimes, commands and threading

One loaded master script owns one provider state. Globals do not cross scripts.
Commands and filters are staged during loading. Reload unregisters old features,
disposes the old state, and publishes new features as a unit; a failed reload has
no executable stale callbacks. Explicit script ownership must remove commands,
filters and handlers on document close/disposal. IDs include canonical script
identity and macro name with collision-safe deterministic encoding.

Document-local execution receives an explicit host context: editor, selected
events, active event, media timeline, paths, dialogs, progress and clipboard.
Finalize the current draft before capture. Capture/commit on the UI thread;
execute Lua and mutate a private projection on a worker. A script state is used
serially; validation/toggle calls also run off the UI thread with cached results
keyed by document/selection revision and script generation. Cancel obsolete work.
UI notifications marshal through Avalonia's dispatcher. Command failures use
central reporting and cannot escape ICommand.Execute.

## Lossless subtitle projection and undo

3.2.2 indexes Script Info, styles, then events. Raw headers, Format lines, blank
lines, other comments, attachments and unknown sections are not Lua entries.
Keep them anchored in Yoake source storage. Store original record provenance so
read/modify/write retains unrecognized columns, field order, syntax, whitespace,
line endings and unknown Mangetsu text. Never simplify the entire ASS file and
reparse it. The working state is independent of live record objects; reads are
copies, writes validate before replacing an entry. Append locates the end of a
class; large event loops must have amortized constant append cost.

`set_undo_point` saves an immutable checkpoint in the private working state.
Earlier points are **not** persistent when a later error/cancellation occurs.
Only successful processing reconciles changes. Multiple checkpoints produce
separate named operations in the document's existing UndoManager; the unmarked
tail produces a macro-named point. No point per table assignment. Commit guards
against a stale document revision, batches notifications, retains the session
and existing undo history, and resolves selection against resulting indexes.
Undo and redo restore both document and selection/draft coherently.

## Services and workflow

Installation and user roots each contain `automation/include` and
`automation/autoload`. Paths are explicit platform service values, not implicit
working-directory or hardcoded AppData values. Discovery is bounded, cancellable,
deterministic and independent per failure. Document-local `~`, `$`, `/` references
retain their metadata when missing. Include resolution is master-relative and
require preserves 3.2.2 MoonScript precedence and Unicode filesystem access.

Dialog descriptors are Core data. Avalonia builds all source-defined controls in
script grid units, with typed defaults and exact result/button conversions.
Colors use Yoake's authoring picker. Dialogs and clipboard marshal to the platform
thread; provider execution never manipulates controls. The progress service owns
title/task/percentage/log output and cancellation. A native instruction hook
interrupts tight loops without requiring voluntary progress calls.

Text metrics use a provider measurement service matching 3.2.2 GDI metrics and
style scale on Windows; TextBlock.Measure is not a substitute. Complex-script
handling and any necessary divergence from legacy UTF-16 spacing are tested and
documented. Media APIs read the existing central timeline, timecodes, keyframes,
dimensions and aspect ratio; there is no Automation playback clock.

The Manager exposes paths, metadata, counts, origin, errors and load/reload/remove
commands. Registered macros participate in menus and hotkeys through command IDs.
Export filters configure and run in priority order against a working copy; export
does not dirty the open document or reuse Save as an implicit filter operation.

## Packaging, provenance and trust

Karaoke parsing is a read-only structured scan that returns the 3.2.2 syllable
table, including the empty index zero and unnormalized relative millisecond
times. Other tags, comments and drawings remain in syllable text. No parser
operation edits ASS source. The native provider retains schema provenance
through exact raw/class fields when unmodified `table.copy` loses weak identity;
script-only derived fields are ignored during AssEntry conversion.

Windows text metrics use GDI at the source's 64x font size, with style scaling
applied to results. GDI objects never escape `Yoake.Native` and are released on
every error path. One deliberate Unicode deviation: nonzero spacing uses
whole-string shaping plus spacing per grapheme; 3.2.2 measures UTF-16 code units
individually, splitting surrogate pairs/combining sequences. This difference
must remain visible in the compatibility ledger and complex-script fixtures.

The standard modules use 3.2.2's unmodified LPeg 0.10 and luabins sources, its
unmodified Boost regex/Unicode adapters, and a narrow Yoake implementation of
the source-defined lfs surface using C++ standard filesystem. Shipped includes
(including MoonScript 0.2.5) remain unmodified and are hash-recorded in
`third_party/automation/provenance.json`. Their legacy versions are compatibility
pins, not claims of modern maintenance; all are trusted-script code.

Boost.Regex/Locale and ICU are additional **native**, statically linked
dependencies acquired through the existing pinned vcpkg registry. Boost is
BSL-1.0; ICU carries its ICU/data licenses at the selected registry revision. They implement
3.2.2's actual Unicode-aware Boost Perl regex and full Unicode case folding.
.NET Regex and simple case conversion have different regex syntax, captures,
replacement and expansion semantics, so they are not adequate substitutes.
Both projects remain maintained; the immutable registry pins the dependency
graph. They add CI build time and substantial native code/data size (especially
ICU); CI/package manifests provide measured output sizes and complete copyright
notices. They have no managed trimming/reflection cost and no UI/native-state
leak. Aegisub's old Boost/ICU versions are not copied; compatibility is judged
against its 3.2.2 module fixtures. Full fixture parity remains an acceptance gate.

Native acquisition/build extends only `ci/build_dependencies.ps1` and
`third_party/*`. Build/cache trees remain outside shipping `native/win-x64`.
Do not bump unrelated media dependencies. Copied includes, modules and fixtures
retain license headers and have exact source/version/path/hash/modification
provenance. CI does not depend on the user's local Aegisub folder.

Automation scripts are trusted local executable extensions, with the substantial
Lua standard-library/native-module access of the compatibility target. This is
not a strong sandbox. Managed provider boundaries and transactional documents
protect host architecture and subtitle integrity, not against malicious native
code. Future process isolation must be possible through the Core service boundary.

GitHub Actions builds/tests the managed host, native stack, NativeAOT package,
module fixtures and real-window macro/dialog/selection/undo scenario. Do not
compile or rebuild either Yoake or Aegisub locally during ordinary development.
