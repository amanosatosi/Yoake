# Local Video parity continuation

Stay on `automation-4-layer` in this local Yoake workspace. Fetch first and inspect newer commits before fast-forwarding normally. Do not reset/rebase, create a feature branch, merge into `csharp-ban`, rewrite Automation, change native dependencies or compile/publish/package locally. GitHub Actions remains authoritative. Preserve the user deletion of `luna.md` and untracked `agents.md`.

## Verified checkpoint

This job started at `b6e7daac043f2b32f8366e74a6e04d3034cb41e8` after fetching. On the user-requested resume/sync, remote had advanced from our `7a009862ff8044380a16f9e14b5ada7d9ecac087` to `8b491c241dedea0363ae9345f7f0eee2b81da6ad`. Inspected and fast-forwarded the newer commits: `556f2e2` (dummy playback acceptance after Audio EOF), `a76dd39`, `d344d63`, `8b491c2` (Explorer drops and tests). Those changes were preserved; Explorer drops were not added by this parity job.

Verified production HEAD: **8b491c241dedea0363ae9345f7f0eee2b81da6ad**. Both [branch run 38291051720](https://github.com/amanosatosi/Yoake/actions/runs/38291051720) and [PR run 38291058475](https://github.com/amanosatosi/Yoake/actions/runs/38291058475) passed source policy, managed tests, Automation providers, Windows NativeAOT, portable packaging, all startup profiles, separate packaged acceptance and domain/native provider verification. The enclosing documentation-only `[skip ci]` commit may be newer with identical production code.

455 Core, 108 UI workflow and 34 native Automation tests passed. This parity job added 24 Core cases and five UI cases; the newer remote Explorer work adds two UI tests. Packaged Video utilities run in a separate `--verify-ui-video-utilities` process, not startup verification. Existing ownership acceptance now also exercises native Audio Close, canceled Close, obsolete analysis rejection, retained Video seeking/playback and Audio reopening.

Earlier complete implementation proof: `556f2e21d155db17df6f8ebc3c89ce452ba4cc76`, green [38281874007](https://github.com/amanosatosi/Yoake/actions/runs/38281874007) and [38281880248](https://github.com/amanosatosi/Yoake/actions/runs/38281880248). Both logs explicitly pass the independent ownership process and the full new Video utilities process. Superseded intermediate runs were canceled as newer checkpoints arrived. `9d65f66` initially exposed a fixture that completed analysis a timer tick before Close; analysis now starts immediately before real Close menu input, with the rejection assertion retained. `847266a` exposed a test range-expression syntax error, fixed without removing the test. `7a00986` failed dummy playback stage 16 because hosted runners lack guaranteed Audio output; remote `556f2e2` exercises the existing central Video tail after the fixture's Audio EOF, retaining Audio and requiring active playback/live frames before Video EOF. Audible output/device latency remains hardware QA.

## Implemented and credited

- `audio/close`: independent `WithAudio(null)` ownership, active playback stopped, Audio open/export/analysis canceled, waveform/spectrum caches and presentation cleared, Video/frame map/external metadata plus document/draft/pending timing/selection/history retained. Existing pending markers survive; an unfinished Audio drag rolls back to its pending checkpoint. No subtitle undo entry; repeated Close is disabled safely. Fake-resource tests prove exactly-once release; native packaged ownership proves retained Video decoding, seeking/playback and Audio reopening.
- Six frame copy/save commands use ONE `FrameExtraction` pipeline. Raw decodes the captured source frame index; Full uses existing Mangetsu composition; Subtitles produces real transparent BGRA coverage. Captures include document/provider, frame-map identity, frame index/mapped time and preview revision; resource leases keep decoding safe while stale/canceled work cannot publish. The existing draft/pending-aware preview source and renderer are reused. Alpha accumulates premultiplied tile coverage in image order with clipping, then unpremultiplies. Tests cover opaque glyph/drawing, antialiased edges, outlines, shadows, ASS alpha, clipping and blank frames.
- Real Windows/Avalonia image clipboard supports all variants and RGBA. Its transfer owns the bitmap; guards reject obsolete pixels through asynchronous OLE retries/delayed rendering. Failure/cancellation leave editor state intact. PNG encoding and bitmap copying run off-thread; unique sibling temporary files and guarded atomic publication prevent partial/stale destinations. Unicode, valid PNG headers/dimensions/decoding, transparent alpha and failure cleanup are verified. Automatic filenames match `<stem>_<counter:03>_<frame>.png`, using video directory or dummy script/home fallback.
- `video/open/dummy`: internal deterministic `IVideoProvider`, exact 3.2.2 defaults/bounds/resolution choices, solid RGB and 8x8 checkerboard, saved last options. No temporary video file. Central timing/navigation/playback, preview/visual tools, extraction, aspect and Close use the same provider boundary. Audio is retained. Exact 3.2.2 `Project::DoLoadVideo -> SetPath` records dummy sources in Video MRU, so Yoake has typed reopenable `?dummy:` entries rather than treating them as filesystem paths.
- Five aspect commands: Default source pixel ratio, 4:3, 16:9, 2.35, or validated decimal / colon / slash / x / X custom input in 0.5–5.0. Real opens apply source DAR using pinned FFMS2 5.0 SAR fields; dummy starts Default. One logical-DPI viewport transform serves image, tools and pointer coordinates. Radio state belongs to the document/tab. Overrides never change source pixels, ASS coordinates/source, exports or undo.
- `video/copy_coordinates`: actual pointer through shared letterbox/aspect transform, script coordinates, comma-separated trimmed three-decimal formatting. Last position survives pointer exit; letterbox coordinates can be negative, as in 3.2.2. Real clipboard and DPI-independent transform tests pass.
- `video/details`: native/dummy source-backed identity, source FPS, resolution/reduced ratio, frame count/effective duration, last mapped frame time, decoder/provider, timing source and decoded BGRA format. No native rescan or fabricated codec metadata.
- `video/show_overscan`: saved check command, exact wide/narrow 3.2.2 dimensions and rounded blue 50% masks drawn only in presentation. It never enters source composition/export or undo.

All commands use semantic registry IDs, contextual hotkeys, menu enabled/check/radio/shortcut bindings, UI-safe error reporting and cancellation guards. Numeric dialogs protect IME preedit Enter/Escape. No dependency, native version, Automation runtime or platform chrome rewrite was introduced.

## Source authority and deliberate differences

Primary oracle: supplied `C:\aegisub source\Aegisub-3.2.2`, an archive rather than a Git checkout. `build/git_version.h` identifies tagged 3.2.2, build 8635. Twelve inspected command/dialog/provider/display/controller/project/vector/utility/config/hotkey files match the pinned release `f743d1411e09cbb2bd34ddd2d4b6738101fab710` after newline normalization. Do not substitute command names or historical manuals for those implementations. The requested subtitle-only IDs are absent from 3.2.2 and present in the union inventory from 3.5; they are explicit requested extensions.

Yoake improves transparent RGBA clipboard/PNG fidelity, stale/cancellation handling and atomic saves. Dummy identifiers retain round-trip FPS rather than six-decimal truncation; checkerboard uses existing higher-precision HSL with the same luminance shift. Details separates source FPS, effective mapped duration and final frame time. Aspect is retained within document lifetime and reset on Video replacement; unlike Aegisub project UI properties, Yoake does not alter ASS merely to persist presentation. These choices are documented in `docs/architecture/media-timeline.md` and the ledger.

Editable JSON and rendered parity Markdown are synchronized using `tools/render_aegisub_parity.py`; `--check`, exact hotkey generator `--check`, XAML XML parsing, PowerShell parsing and `git diff --check` passed locally. No Yoake build/native rebuild ran locally. Do not run the full source-policy scanner over ignored upstream fixture trees with intentionally invalid JSON; clean-checkout CI is authoritative.

## Remaining scope

Video has **48 complete, 0 partial, 7 missing, 1 deliberately N/A**. The exact missing list remains:

- `video/reset_pan`
- `video/zoom/50`
- `video/zoom/100`
- `video/zoom/200`
- `video/zoom/in`
- `video/zoom/out`
- `video/detach`

These commands have no registry entries or placeholders and were not marked partial. Pan/zoom interaction and detached-window architecture belong to a later job. `video/subtitles_provider/cycle` remains deliberately N/A because Mangetsu is the single renderer. Overall ledger: 131 complete, 52 partial, 40 missing, 13 N/A and one superseded record. Existing Audio/other partial workflows remain unpromoted.

Do not start `audio/karaoke`, ASSDraw, Font Collector, Resample Resolution, Styling/Translation Assistants, Kanji Timer, timing postprocess, spellchecker, attachments, recovery, app About/language/update/new-window or Automation changes in this continuation.

Manual QA remains: audible output/device failures/latency, native picker and cross-application clipboard behavior, installed Japanese/Burmese IME, physical GPU/display drivers, ordinary laptop DPI/multi-monitor layouts, unusual long/VFR/multi-track media and large exports. Hosted synthetic IME/pointer/clipboard checks are not physical hardware QA.
