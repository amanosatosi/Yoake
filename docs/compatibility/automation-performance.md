# Automation performance measurements

Run the existing GitHub Actions build workflow on `automation-4-layer`. The real
native-provider test step writes the `Automation-performance` artifact. It uses
the native LuaJIT bridge, unchanged shipped Karaoke Templater/karaskel, real GDI
metrics and the same synchronous `FileAppLog` used by the desktop. No local build
or separate benchmark framework is required. Correctness assertions remain the
gate; elapsed times are observations without absolute shared-runner limits.

The instrumented baseline is `84c4db5f0041f3895fca1b8d62e94d8d7b00408f`.
Its only difference from the first measurement revision `ae89186` is fixture
retention, an independently corrected line-template expectation and earlier
diagnostic-file writing. There are no execution optimizations in that baseline.

| Workload | Input / generated events |
| --- | --- |
| Baseline replacement | 100 dialogues |
| Sequential read/replace | 1,000 and 5,000 dialogues |
| Lua generation | 5,000 and 20,000 generated events |
| Original Karaoke Templater | 100 / 400 songs generating 7,000 / 28,000 events |

Each workload reports loading separately and runs three fresh subtitle
projections in the same script state. Iteration zero includes first-invocation
costs; compare iterations one and two for warm throughput. This is not a fresh
process/startup measurement. `run_ms` includes projection capture, provider
execution and result readback. `commit_ms` covers guarded source/undo publication;
asynchronous rendering is outside this provider benchmark. Managed allocations
use process-wide `GC.GetTotalAllocatedBytes`, not native/Lua heap measurements.
Nested diagnostic timings overlap and must not be added together.

`Fixtures/automation/kfx-throughput.ass` is the committed 100-song input. The
larger case repeats identical projected songs to isolate scaling. Output checks
cover total event count, timing, layer, style, effect, comment state, text, unknown
columns/sections, and exact undo/redo. Expected tuples are derived from the frozen
3.2.2 implementation, not recorded by executing Aegisub: notably a line template's
main text repeats per syllable, producing `LL` for this two-syllable input.
The separate unchanged upstream retiming/furigana fixtures continue to run.
Unknown-column preservation is Yoake's stronger source guarantee; the fixture is
not a claim that Aegisub's own ASS loader preserves custom Format columns.

Both baseline and optimized measurements keep JIT disabled. The pinned recorder
offers `LUAJIT_ENABLE_CHECKHOOK`, but it is disabled by default and designed for
asynchronous hook installation while hooks are otherwise unset. Enabling that
requires explicit build-policy, hook lifetime, tight-loop/coroutine, callback and
MoonScript traceback verification. Process isolation is a larger alternative.
Neither is needed to preserve the current tested interpreter cancellation path;
no unmeasured JIT speedup or weakened cancellation is claimed.

For normal desktop diagnostics, start Yoake with
`YOAKE_AUTOMATION_DIAGNOSTICS=1`; summaries appear in the existing Automation log.
See the architecture contract for phase boundaries and the compatibility ledger
for remaining API differences and physical-device QA.
