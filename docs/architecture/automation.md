# Automation 4 implementation contract

Status: design and implementation in progress on `automation-4-layer`.
Acceptance is meaningful old 3.2.2 macro/KFX work in the packaged NativeAOT app,
not merely a runtime or a Manager window. See the
[compatibility ledger](../compatibility/aegisub-automation-3.2.2.md).

## Runtime decision

Use a native LuaJIT provider with Lua 5.1 semantics. The reference release bundles
LuaJIT 2.0.3. The final acquisition pin must be recorded in `third_party/versions.json`
before a runtime is integrated. Evaluate a maintained LuaJIT 2.1 revision against
the 3.2.2 fixtures before adopting it; do not enable incompatible language behavior
merely because a newer runtime supports it.

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
through a managed callback. Reverse callbacks must catch managed exceptions and
return an error payload to that shim. NativeAOT uses static interop, no generated
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
