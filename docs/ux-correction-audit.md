# Real-device UX correction audit

Baseline: `53cf231`, branch `codex/usable-subtitle-editor`.

Existing lossless ASS records/tokenization, transactional undo, per-document
workspace/media ownership, Mangetsu renderer track updates, asynchronous font
enumeration, spectrogram provider, SVG generation and packaged verification are
retained. Presence alone is not completion.

| Workflow | Baseline assessment |
| --- | --- |
| Style editing | Functional but vertically wasteful; standard fields scroll; preview and action bar poorly placed |
| Style lists | Shallow single-item operations; clipboard, bulk movement and sorting missing |
| Style references | Behaviorally incorrect: override resets ignored during rename/delete |
| Font picker | Enumeration exists, browsing affordance missing |
| Color authoring | Shallow RGB/alpha dialog; spectrum, HSV/HSL, history and eyedropper missing |
| Audio timing | Signed waveform structure lost; fixed 10 ms detail; panner and display controls missing |
| Playback volume | Missing |
| Event drafts | Validation/undo useful; grid and renderer feedback lag until explicit commit |
| Text boundary movement | Default TextBox behavior unsuitable; wrapped-line boundary override missing |
| Grid | Layout poor: language-dependent height, Type column and redundant toolbar |
| Video navigation | Generic time slider; keyframes/frame ruler missing |
| Visual tools | Working transactions and clip manipulation; poor toolbar adjacency and standby feedback |
| Window chrome | Native decorations retained, redundant framework caption overlay and fixed caption inset incorrect |
| Empty state | Nullable nested binding paths need explicit presentation state |

Reference inspected: local Aegisub/Toshi-ban source at
`b20d63af149568cbef00f3a22742217ecd915990`, notably style manager,
color picker, audio box and video slider. Its audio sliders are horizontal zoom,
amplitude scale and volume; panel height is separately resizable. Yoake must
preserve those distinct capabilities and its existing audio-above-text layout.


## Implemented corrections

- Style editing remains integrated with both multi-select lists. The selected style uses compact font, color, outline/transformation and placement groups, a persistent Mangetsu preview, dirty indication and bottom action bar. Collection management is collapsed separately. List splitter proportions persist.
- Both lists support bulk duplicate/delete, move up/down/top/bottom, sort, valid ASS clipboard copy/paste, import and transfers. Script operations use logical undo operations. Referenced deletion asks for a replacement only when needed.
- Event Style fields and complete top-level `\rStyleName` references change together, preserving unrelated syntax and unknown records. Incomplete blocks, literal escapes and transforms stay opaque.
- Font browsing has an explicit dropdown, virtualized installed-family list, async cached enumeration, search, keyboard selection and exact unavailable-name retention. Main formatting and Styles Manager share the control.
- The canonical color dialog provides a 256-DIP spectrum/crosshair, hue and transparency strips, checkerboards, RGB/HSV/HSL, ASS/HTML hex, original/current preview, palette, exact clipboard operations and 32 persistent recent RGBA colors. Both entry points share profile-local history. Windows screen sampling uses isolated physical-coordinate APIs and a cancellable per-monitor overlay; alpha is independent.
- Audio keeps signed min/max envelopes in a bounded overview and multiresolution pyramid, with cancellable viewport decoding at fine zoom. Display height, amplitude/spectral intensity, linear playback volume and mute remain separate. The bottom panner serves both visualizations and remembers each document's range. Duplicate neighboring timing markers draw once per pixel column.
- Detached drafts update grid and Mangetsu preview immediately, debounce rendering by 30 ms and commit typing bursts after 900 ms. Preview serialization splices the selected record into the cached base track. Renderer/generation checks reject stale output. Gestures render transactional state and explicitly invalidate while document notifications are deferred.
- AssTextBox handles first/last wrapped visual-line Up/Down and Shift selection while retaining Avalonia shaping, normal vertical movement and IME/preedit ownership.
- The virtualized grid has fixed 30-DIP single-line rows, theme-aware comment/current/active/selected states and `# | L | Start | End | Style | Actor | Effect | Text`. The permanent grid mini-toolbar is removed. Initial collection synchronization avoids searching for every new record.
- FFMS2 frame timestamps and keyframe flags drive the custom video ruler, drag seeking, Shift snapping, wheel frame/keyframe stepping and exact/relative timing labels.
- Visual tools sit directly beside the navigation workspace, with selected tool buttons, effective position/alignment/moving anchors and persistent rectangular/vector clip handles. Clip inverse state survives edits.
- Five compatible Aegisub movement/sort SVGs were adapted with exact paths, commit, modifications and BSD attribution recorded in THIRD_PARTY_NOTICES and packaged licenses. No application logo or managed dependency was added.
- Avalonia 12's actual redundant caption overlay is hidden while caption roles and native window decorations remain. Insets use realized caption bounds with DPI-aware provider fallback. Empty edit bindings use a detached presentation value while the active document draft remains nullable.

## Verification and manual limits

Core/UI regression tests cover references and undo, bulk styles/clipboard, conversions/history, signed peaks/resolution, gain/mute/settings, draft coalescing/revert, transactional preview, visual anchors, VFR/keyframe calculations and cached preview over 20,000 events.

Packaged NativeAOT verification additionally exercises real windows and controls: mixed-script fixed rows; compact headers and removed toolbar; realized font browsing; standard style fields with preview visible without scrolling at ordinary size; numeric inner edit bounds; full color spectrum/recent area; audio controls/panner; adjacent tools; hidden caption and measured tab clearance; real FFMS2 keyframe drawing/snapping; actual Mangetsu pixel changes before idle draft commit; signed waveform/fine-zoom decoding; and end/middle scrolling through a 20,000-event document with bounded realized containers. Binding warnings and framework errors fail verification. Dark/light and 100/125/150/200% raster captures remain artifacts.

Interactive Windows QA remains necessary for snap flyouts/system menu/double-click/resize, actual mixed-monitor DPI transitions, eyedropper selection under desktop composition/HDR, audio-device latency and keyboard/trackpad timing ergonomics. Japanese/Burmese/Arabic IME composition and integrated-GPU long-file scrolling should be exercised on real hardware. Raster DPI captures verify scaling output, not monitor transitions. These limitations do not replace the packaged assertions.

Future advanced authoring (39 Mode, K-Timing, tracking, gradient/chat/distort/curved-text GUIs, .asa and extended furigana tools) is intentionally deferred as requested. No tests or source-policy checks are disabled and no local compile/publish/native dependency build is used.
