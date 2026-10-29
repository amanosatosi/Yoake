# Local Video parity continuation

Stay on local `automation-4-layer`. Fetch first and inspect newer commits before ordinary fast-forward synchronization. Do not create a branch, reset/rebase, merge into `csharp-ban`, overwrite user changes, rewrite Automation, modify native dependency versions or build/publish/package locally. GitHub Actions is authoritative. Preserve the tracked deletion of `luna.md` and untracked `agents.md`.

## Verified checkpoint

This seven-command job started at `fcc815c9e954b4706682be976b83ff407fb07669` after fetching. Production verification: **5a23837f3a7ccd267a8aad2cf493d9f1360144e4**, green [branch run 38304952560](https://github.com/amanosatosi/Yoake/actions/runs/38304952560) and [PR run 38304957636](https://github.com/amanosatosi/Yoake/actions/runs/38304957636). The documentation-only `[skip ci]` checkpoint may have a newer SHA with identical production code.

472 Core, 110 UI workflow and 34 Automation provider tests passed: 616 total, including 17 new Core presentation cases and two new UI workflows. Source policy, Windows NativeAOT, portable packaging, all startup profiles, every previous separate acceptance process, the new native Video presentation process and domain/native providers passed. Local validation was source-only (XML/PowerShell parsing, generators and diff checks); no compilation or native build ran locally.

The previous Audio Close / fifteen Video utilities remain verified and complete. Their checkpoint `8b491c241dedea0363ae9345f7f0eee2b81da6ad` passed [38291051720](https://github.com/amanosatosi/Yoake/actions/runs/38291051720) and [38291058475](https://github.com/amanosatosi/Yoake/actions/runs/38291058475). Do not regress independent media ownership, source-resolution extraction or real subtitle RGBA.

## Seven implemented commands

- `video/zoom/50`, `/100`, `/200`: physical decoded-source-height scales .5/1/2, width corrected by aspect, converted to Avalonia logical pixels using actual render scaling. Presets stop playback and have exact radio state. The fixed editor viewport clips larger images.
- `video/zoom/in`, `/out`: additive .125 window-zoom steps, clamped .125–10, without stopping playback. All five commands require attached loaded Video, exactly as 3.5. A shared editable percentage/dropdown and wheel action handle native free-size hosting instead.
- Middle-button drag: actual captured input with the normal visual-tool cursor, bounded physical pan (half content dimension plus 40% viewport dimension), even when the image fits. No subtitle edits. Release commits presentation; Escape, capture/focus loss, source replacement, tab/view/host changes and shutdown cancel safely. Pan checkpoints include document/provider revision so Close cannot restore an old provider's offset.
- `video/reset_pan`: clears pan AND content zoom, retaining window zoom/aspect. Default wheel changes window zoom; Ctrl-wheel applies pointer-anchored content zoom; Shift-wheel pans; Ctrl+Shift does nothing. No source pixels, ASS edits, subtitle undo or export changes.
- `video/detach`: moves the existing complete Video control group (image, overlay, primary/subtools, seek/playback and zoom input) into a real native owned resizable/maximizable/minimizable window. Same provider, frame, subtitle renderer, selection/draft and one central clock. Toggle or native Close reattaches; closed host releases its platform handle and handlers. Video Close closes the host while retaining the document preference, Recent/Dummy reopen it, Audio Close leaves it alive, and tab changes host each document's own state. Native position/size/maximized state are retained per document within the editor lifetime, constrained to a working monitor area. Seek focus in detached Audio view does not change the main display mode.

`VideoViewState.Layout` yields one `VideoPresentation` rectangle for image, overscan, tools, hit testing and pointer/script mapping. Detached resize with active content manipulation retains absolute content height/pan; intermediate native layout bounds cannot lose explicit zoom. Monitor DPI changes preserve logical image size with the reference's zoom adjustment. Captured visual edits roll back when their display rectangle changes. Source extraction does not read presentation state.

The packaged `--verify-ui-video-presentation` process proves real menu/default/remapped keyboard routing, enabled/radio states, rendered pan pixels, real middle capture/release/Escape/loss, Reset, script clipboard mapping, exact visual clip coordinates and one-step undo under zoom/pan, native detach and close, active playback across both hosts, editable zoom IME, resizing, tabs, view modes, provider Close/Recent/Dummy, external keys/timecodes and independent Audio Close. Earlier packaged assertions remain intact. It is separate from startup verification.

## Primary source and deliberate differences

Provided `C:\aegisub source\Aegisub-3.5.0` is an archive, not a Git checkout; there is no generated `build/git_version.h`. Thirteen inspected files match pinned release `43af875ea01e80052cc6c44f24fc8c5258261c6d` after newline normalization. Exact functions and behavior are recorded in `docs/compatibility/aegisub-video-presentation-reference.md`; architecture is in `docs/architecture/media-timeline.md`.

The established parity policy follows the 3.5 Reset Pan / two-scale backend delta from 3.2.2. This does not change the frozen 3.2.2 Automation implementation. No second decoder, renderer, clock, frame ownership path, native version or dependency was added.

Yoake retains its existing initial fitted preview and fixed embedded workspace rather than importing Aegisub's fixed-size canvas/window growth. Explicit percentages are real source scales, never fit factors. Yoake moves the same controls rather than constructing another display; active playback continues without Aegisub's stop/resume current-frame request. Presentation and geometry belong to the document lifetime, do not alter ASS and are not globally persisted across launches. The default wheel actions are implemented; configurable alternate wheel actions and native pinch gestures are outside these seven command workflows.

CI repairs retained assertions: the first implementation used a nonexistent Avalonia focus override (changed to the focus event); a later native process reached real detach/visual editing but its zoom-input locator found both the percentage field and dropdown template text field (the actual percentage field now has a stable name). Native resize exposed an old-root layout queue retaining moved controls; the handoff now drains source layout while detached before adoption by another root. Final packaged resize, active playback transfer, native Close/handle disposal and metadata/provider lifetime assertions all pass. Future diagnoses must be fixed without weakening acceptance.

## Remaining parity

Video: **55 complete, 0 partial, 0 missing, 1 deliberately N/A**. `video/subtitles_provider/cycle` remains N/A because Mangetsu is the single renderer. Overall ledger: 138 complete, 52 partial, 33 missing, 13 N/A and one superseded record. Unrelated partial/missing categories were not promoted.

Do not start other parity categories without a new request. `audio/karaoke`, ASSDraw, Font Collector, Resample Resolution, assistants, Kanji Timer, timing postprocess, spellchecking, attachments/recovery, app utilities and Automation remain outside this job.

Manual QA: real multi-monitor DPI transitions and removed-monitor restoration, native maximize/minimize/snap and interactive resizing on physical GPUs, installed Japanese/Burmese IMEs, simultaneous media/tool gestures under load, unusual large/VFR media, and audible Audio device/latency behavior. Hosted input and software render captures are not hardware QA.
