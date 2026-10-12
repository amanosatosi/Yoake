# Continuation after verified Video playback; local ownership work next

Local ownership implementation checkpoint (the enclosing commit resolves its exact SHA): independent document-owned Video Open/Close/Recent and the companion Audio Open/Recent ownership path are implemented. `DocumentMedia` shares separate native resources through tested ownership leases; it contains no timeline/editor state. Combined Open indexes once then transfers handles without reopening. Dedicated replacements prepare off-thread, retain the companion provider and reject canceled, superseded, obsolete-metadata and closed/replaced-document results. Audio analysis/export/playback validate actual audio identity. External frame metadata, waveform, draft/pending/selection/undo are preserved; host path metadata follows the correct resource without Automation runtime changes. Managed ownership/MRU/workflow tests and `--verify-ui-media-ownership` native/menu/keyboard acceptance are added. These rows remain partial until GitHub Actions passes. No local build was performed. Next dependency after verification: shared source-resolution raw/full/transparent-subtitle image extraction; do not start aspect presets first. The older status list below describes the verified cloud checkpoint, not this pending implementation.

Continue only on `automation-4-layer`; fetch first, inspect newer commits and fast-forward normally. Do not reset/rebase, create another feature branch, merge into `csharp-ban`, change native dependency versions, rebuild Automation, or publish/rebuild the native graph locally. GitHub Actions remains package authority. The cloud implementation is now synchronized into the local workspace at `3ad99b24e70cd01b281de4083c9dc22605b9f8d6`. Preserve the local `luna.md` deletion and untracked `agents.md`. Interrupted local timing edits are retained in a named stash; remote successor validation supersedes the old code, so do not blindly apply that stash. Three earlier architecture-document edits remain in the worktree.

Recorded ending implementation HEAD: `2fa8a0701945cfa63ed883c922204398ada80541`. This continuation records the code checkpoint; a subsequent documentation commit may contain the final ledger/CI update. Resolve the enclosing document commit for the final branch HEAD.

Cloud experiment baseline: first measured timestamp `2026-10-12 00:58:21 UTC` (`07:28:21 Asia/Rangoon`), fetched HEAD `6ea216041db2653028fdeec9050d15af4ebff0f0`. The environment status API exposed no instance start timestamp. Initial run [38179602119](https://github.com/amanosatosi/Yoake/actions/runs/38179602119): source-policy, managed-tests and automation-provider-tests passed; Windows NativeAOT compiled/published, then packaged audio-timing acceptance failed at stage 3 on focus. Wall time is measured from the first timestamp, including CI waits.

Timing recovery: commit-default/next now prevalidates the complete successor state before publishing current markers/draft/source. Regressions cover malformed stored timing, bad untouched metadata, representability overflow, and preservation of selection/draft/markers/undo. Zero-ended successors with a stored nonzero start remain repairable. The initially stranded edits were reimplemented from remote source, not copied from another workspace.

The diagnostic run at `b0a22180f942751d56ff2bbc3d3f7cabb7035679` ([38190465578](https://github.com/amanosatosi/Yoake/actions/runs/38190465578), paired PR run 38190468418) passed all cheap jobs and NativeAOT publishing, then failed at audio-timing stage 4: `The actual timing control must own focus: SubtitleRows; enabled=True, visible=True, attached=True.` The harness was focusing the nonfocusable ListBox rather than its realized interactive row. The latest code uses the same realized-row focus path as the existing green keyboard/timing/selection harnesses. It retains Focus() AND IsKeyboardFocusWithin assertions before raising real routed input. Native menu closure and later input use separate dispatcher turns. No IME assertion was bypassed.

Latest implementation runs: [38197606988](https://github.com/amanosatosi/Yoake/actions/runs/38197606988) (push), [38197611903](https://github.com/amanosatosi/Yoake/actions/runs/38197611903) (PR), at `2fa8a0701945cfa63ed883c922204398ada80541`, are **fully green**. Both Windows job logs confirm separate packaged Video navigation acceptance: real Space/Shift-Space/Escape, pending-aware frame-bounded Play Line, gesture cancellation, video EOF independent of longer audio, actual clock continuation after shorter audio EOF without an output device, stale/canceled requests, and native provider/frame-map/source/undo retention. All existing acceptance processes passed. `video/play`, `video/play/line` and `video/stop` are promoted to complete without implementation changes.

Newest completed red implementation: `ac02e7e7f14428eb4ca2a19e5a115f39fe1a9015`, runs [38196156244](https://github.com/amanosatosi/Yoake/actions/runs/38196156244) and [38196159947](https://github.com/amanosatosi/Yoake/actions/runs/38196159947). All prerequisite jobs, NativeAOT publishing, startup profiles, packaged audio timing and Video tools passed. Navigation stages 1–4 passed keyframes/boundaries/steps/jumps/focus, and actual changed-frame Jump accept/cancel/invalid/IME checks. Stage 5 then failed: `Video Escape stops central AV playback.` The idle visual overlay consumed every Escape as gesture cancellation. `2fa8a07` routes idle Escape through Video Stop while keeping captured edit gestures and point/box selections under overlay cancellation. The failing assertion and the detailed gesture-cancellation acceptance remain intact. Those final stages now pass in both green runs above.

Prior red implementation `11f14f85590f7a7c2105951a064a4f2c2369f1f9`, runs 38194541465/38194544760, verified the complete audio-timing and Video-tools paths and navigation stage 1, then failed at navigation stage 2: `Invalid jump input cannot close the dialog or seek.` Deferred TextChanged left a cached valid request available. `ac02e7e` uses synchronous TextProperty updates and fresh submit validation; this repair is verified by the newest red run. The 15 tool rows plus 10 navigation rows are now complete based on their real packaged paths.

Earlier red run at `dff02289e66c020b5fbaad00765cffbdf0bcf8f2` (38192470521/38192473844) reached audio-timing stage 10 and failed `IME Enter in the edit box must not leak timing commit/navigation.` The harness assigned preedit before focusing its input. All three new/affected harnesses now acquire focus before starting composition, then assert preedit survives guarded Enter. This fix is verified by the green audio-timing and Video tools processes in the newest red run. Earlier intermediary runs were superseded/cancelled. Last fully green implementation known before this cloud run: `444aa657df691569ae9c9ac2770223c22e88a974`, run [38175227881](https://github.com/amanosatosi/Yoake/actions/runs/38175227881).

Current cloud validation: 425 core tests and 98 UI workflow tests pass at the final implementation checkpoint. Managed app/XAML compilation and source policy pass. No local NativeAOT publication, native graph build, dependency version update, or hardware-visible claim was made.

Video implementation: retain existing Yoake visual tools, structured ASS edits, unknown/Mangetsu tags, multi-line transactions and preview. Primary/vector mode selection now has loaded-video/owning-document availability and remains radio checked while temporarily unavailable. Visual handles now use the compositor’s pending timing before/during/after gestures, including frames outside stored timing. Mode state is document-owned; tool switching keeps uncommitted draft input, cancels active gestures, and creates no undo entry. Menus/toolbars/context keys share semantic actions. Exact 3.2.2 primary keys remain Video A/S/D/F/G/H/J; 3.5 vector IDs map to the existing eight mode backends. Rectangles now preview from pointer down with no existing clip, including inverse clips and one-gesture undo/redo/capture-loss cancellation. Existing prospective vector geometry is retained and gets native line/bicubic/control/closing proof rather than being rebuilt.

Navigation: source-backed keyframe first/EOF fallbacks, configurable frame-based large steps (default 10), current-line START/END then actual adjacent row traversal (including comments), exact start/end jumps, usable frame/time Jump dialog, validation/cancellation/IME handling, slider focus return/toggle, video-only playback availability and shared checked Play/Stop state. Requests cannot resume a stale provider/document after asynchronous seeking. Ffms now retains its real video EOF separately from combined audio/video duration (including external VFR map overrides). Video and play-line capture video-bounded endpoints; the existing central clock continues silently after shorter audio EOF, while explicit Audio auditions retain their previous stop semantics. Packaged acceptance tests both longer-audio bounds and actual tail playback started after audio EOF without an output device. Play-line consumes the existing pending primary range and maps it to included video frames on one synchronized AV clock, deliberately improving upstream's separate rounded audio/video clocks. Video gets Shift-Space for line audition; shipped Styling/Translation Home stays in its own contexts, Grid/Edit Home stays native, and Space typing in text controls is protected. Menus prefer the usable Video shortcut when no Default binding exists.

Separate acceptance processes: `--verify-ui-video` covers native visual radio keys/toolbar state, draft retention, rectangle press/live/release/inverse/undo/redo, capture loss, tool/document switch, vector line/bicubic prospective controls/curve/closing, IME isolation and providers. `--verify-ui-video-navigation` covers keyframe/Grid contexts, large steps, subtitle boundary traversal, native Jump dialog/focus/IME/invalid/cancel, video play/stop and pending-aware line playback. The pre-existing detailed visual pixel/transform acceptance remains intact.

Exact reference archives were downloaded fresh at `f743d1411e09cbb2bd34ddd2d4b6738101fab710` and `43af875ea01e80052cc6c44f24fc8c5258261c6d`. The local pinned trees are `.ci-cache/reference/Aegisub-3.2.2` and `.ci-cache/reference/Aegisub-3.5.0`; do not run the full repository source-policy scanner over their intentionally invalid upstream test fixtures locally. The existing frozen inventory was not regenerated.

Next coherent dependency: independent video open/replace/close + `recent/video`. `DocumentState.Media` and `_media` still own one combined `FfmsMediaSession`; current video/open remains partial and must not be called complete. Audio export/playback/peaks/spectrum currently read that same provider. Split ownership explicitly so replacing/closing video retains independent audio, document/pending timing/selection/draft/undo and external frame metadata. Ffms already has separate audio/video gates and native handles; avoid duplicate decoder ownership and test sharing/release before expanding image/view utilities. No half-written ownership refactor is left in this checkpoint.

Then implement the six frame extraction commands via one pipeline: 3.2.2 `get_image` obtains provider output at current frame and frame-map time; full uses subtitle composition, raw excludes subtitles. 3.5 `GetSubtitles` produces only subtitles with alpha, copied at 32 bits; raw/full clipboard variants use 24 bits. These are source-resolution images, not visual-tool/overscan/window screenshots. The current `MangetsuSubtitleRenderer.Composite` preserves the host frame alpha because the native BGRA compositor zeroes tile alpha. Subtitles-only output must accumulate the actual RGBA tile alpha; a transparent host passed through this method would remain transparent. Reuse Mangetsu composition and real clipboard abstraction, Unicode/cancel-safe atomic PNG files, and stale provider/frame checks. Next: shared zoom/pan/aspect transform plus detach/details/coordinates/overscan, dummy source, aspect presets last. Do not implement ASSDraw or multiple subtitle renderers.

Manual QA: audible output/device failure/latency, installed IME behavior, long playback and audio/video EOF relationships, Windows DPI/multi-monitor and physical GPU/display drivers, weaker-hardware pointer responsiveness. Packaged tests use actual focused controls and real FFMS2/Mangetsu providers; immediate playback stop proves routing, not audible hardware output.

Ending Video status (excluding recent/video): {'missing': 23, 'already complete': 31, 'partially implemented': 1, 'deliberately not applicable': 1}. `recent/video` remains separately missing. No complete-to-partial or complete-to-missing movement.

already complete:

`video/focus_seek`, `video/frame/next`, `video/frame/next/boundary`, `video/frame/next/keyframe`, `video/frame/next/large`, `video/frame/prev`, `video/frame/prev/boundary`, `video/frame/prev/keyframe`, `video/frame/prev/large`, `video/jump`, `video/jump/end`, `video/jump/start`, `video/opt/autoscroll`, `video/tool/clip`, `video/tool/cross`, `video/tool/drag`, `video/tool/rotate/xy`, `video/tool/rotate/z`, `video/tool/scale`, `video/tool/vclip/bicubic`, `video/tool/vclip/convert`, `video/tool/vclip/drag`, `video/tool/vclip/freehand`, `video/tool/vclip/freehand_smooth`, `video/tool/vclip/insert`, `video/tool/vclip/line`, `video/tool/vclip/remove`, `video/tool/vector_clip`, `video/play`, `video/play/line`, `video/stop`

partially implemented:

`video/open`

missing:

`video/aspect/cinematic`, `video/aspect/custom`, `video/aspect/default`, `video/aspect/full`, `video/aspect/wide`, `video/close`, `video/copy_coordinates`, `video/detach`, `video/details`, `video/frame/copy`, `video/frame/copy/raw`, `video/frame/copy/subs`, `video/frame/save`, `video/frame/save/raw`, `video/frame/save/subs`, `video/open/dummy`, `video/reset_pan`, `video/show_overscan`, `video/zoom/100`, `video/zoom/200`, `video/zoom/50`, `video/zoom/in`, `video/zoom/out`

deliberately not applicable:

`video/subtitles_provider/cycle`

Exact baseline-to-ending parity changes:

- `video/focus_seek`: missing → already complete
- `video/frame/next/boundary`: missing → already complete
- `video/frame/next/keyframe`: partially implemented → already complete
- `video/frame/next/large`: missing → already complete
- `video/frame/prev/boundary`: missing → already complete
- `video/frame/prev/keyframe`: partially implemented → already complete
- `video/frame/prev/large`: missing → already complete
- `video/jump`: partially implemented → already complete
- `video/jump/end`: partially implemented → already complete
- `video/jump/start`: partially implemented → already complete
- `video/tool/clip`: partially implemented → already complete
- `video/tool/cross`: partially implemented → already complete
- `video/tool/drag`: partially implemented → already complete
- `video/tool/rotate/xy`: partially implemented → already complete
- `video/tool/rotate/z`: partially implemented → already complete
- `video/tool/scale`: partially implemented → already complete
- `video/tool/vclip/bicubic`: partially implemented → already complete
- `video/tool/vclip/convert`: partially implemented → already complete
- `video/tool/vclip/drag`: partially implemented → already complete
- `video/tool/vclip/freehand`: partially implemented → already complete
- `video/tool/vclip/freehand_smooth`: partially implemented → already complete
- `video/tool/vclip/insert`: partially implemented → already complete
- `video/tool/vclip/line`: partially implemented → already complete
- `video/tool/vclip/remove`: partially implemented → already complete
- `video/tool/vector_clip`: partially implemented → already complete

Pushed implementation commits in this cloud run:

- `ed64046d1c7409cc768b461f53cf1617b512f357` — Validate timing successors before edits and yield after native menu input
- `b0a22180f942751d56ff2bbc3d3f7cabb7035679` — Use supported TopLevel focus diagnostic API
- `12fb81f2903c8d9b93b4d4f729ed6b1f164889fe` — Finish document-owned visual tool commands and real Video input verification
- `1907fa11b8b55ec4c5fa3ad26416a75ef5cb8c0f` — Complete VFR video navigation and shared line playback workflows
- `973be79295fa33a6ec707e38bd50d183bb4f056f` — Send acceptance grid keys to the actual focused row container
- `822a1040aa11fd50c98a640f40f677b63b2e538e` — Give packaged boundary traversal distinct START and END frames
- `d47a8ad96a41cc32cb39b308779e2fdf60ba4766` — Add context-local Video line audition without intercepting text input
- `dff02289e66c020b5fbaad00765cffbdf0bcf8f2` — Keep visual handles aligned with pending subtitle preview timing
- `d2bb028cc07803c5546ac2d25fbcd2cdf8d47fb8` — Start packaged IME composition only after the real input owns focus
- `49f2c1e121ca266c6e4257b6c1e322bad87b921e` — Require packaged Jump dialog acceptance to change the current frame
- `11f14f85590f7a7c2105951a064a4f2c2369f1f9` — Bound video playback to video EOF and continue after shorter audio ends
- `ac02e7e7f14428eb4ca2a19e5a115f39fe1a9015` — Validate current Jump input synchronously before closing the dialog
- `2fa8a0701945cfa63ed883c922204398ada80541` — Route idle overlay Escape to Video Stop while retaining capture cancellation

Measured cloud wall time at the documentation checkpoint: approximately 121 minutes from 00:58:21 UTC, including CI waits and repair iterations. A final docs-only commit follows this implementation checkpoint and uses [skip ci] to avoid repeating already-green native jobs for documentation; production code is identical to their tested implementation SHA. Its exact branch SHA is reported in the chat.
