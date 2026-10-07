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
| Window chrome | Native decorations retained, redundant native title and fixed caption inset incorrect |
| Empty state | Nullable nested binding paths need explicit presentation state |

Reference inspected: local Aegisub/Toshi-ban source at
`b20d63af149568cbef00f3a22742217ecd915990`, notably style manager,
color picker, audio box and video slider. Its audio sliders are horizontal zoom,
amplitude scale and volume; panel height is separately resizable. Yoake must
preserve those distinct capabilities and its existing audio-above-text layout.
