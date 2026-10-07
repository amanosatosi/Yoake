# Editor validation

This task deliberately performs no local .NET restore/build/test, NativeAOT publish, or native dependency build. Local checks are source policy, XML/JSON/generated SVG checks, `git diff --check`, and deterministic media-fixture generation.

GitHub Actions remains authoritative and now runs:

1. Architecture/source policy and generated SVG verification.
2. Core regression build/tests: real fansub fixture roundtrip, field order, unknown fields/sections/tags, Unicode, mixed newlines, styles, structural operations, clipboard payloads, timing, undo/savepoints, document ownership, splitting and visual spans.
3. UI project/compiled bindings build, plus command-model workflow tests for drafts, tab history, Save As, dirty state, unsaved-close cancellation, validation, multi-row actions and gestures. These use no additional runtime/test framework dependency beyond the existing xUnit/Avalonia references.
4. Existing FFmpeg/FFMS2 dependency pipeline and fresh live-branch Mangetsu build.
5. Windows NativeAOT publish with trimming/AOT errors retained, portable staging, native closure/hash checks and strict desktop startup smoke.
6. Packaged executable `--verify-editor` using deterministic uncompressed AVI/PCM fixtures. This exercises ASS edits/undo/save/reopen, FFMS2 decode/frame stepping/audio reads, full peaks and cancellation, spectrum tiles, and Mangetsu render/update/compositing through the shipped native runtime.

The packaged provider verification reports its result under `artifacts/editor-fixtures/verification.txt`. The portable ZIP remains the release artifact. The source pipeline has not been replaced or weakened.

Interactive release smoke is still required: real MKV/VFR files, hardware audio playback, long/complex tracks on an integrated-GPU laptop, pointer capture/Esc, clipboard exchange with Aegisub, window snapping/DPI, input-method composition and font fallback. The command-model and native-provider checks do not prove all of those UI/hardware behaviors.

See `docs/editor-smoke.md` for the manual sequence and `docs/editor-limitations.md` for the intentionally limited first tools. CI run links and final status are recorded in the implementation PR.
