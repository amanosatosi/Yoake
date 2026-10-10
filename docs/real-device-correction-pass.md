# Real-device correction pass

Branch: `codex/usable-subtitle-editor`. The checkout was fetched first and
started at `04f14df`, already containing `3c88d9c` and the subsequent input/layout
corrections. The display-mode fix and unrelated local agent-rule changes were
preserved. No local managed/native compilation or publishing was performed.

## Workflow references

Inspected [Aegisub/Toshi-ban frame layout](https://github.com/amanosatosi/Aegisub_Toshi-ban/blob/28b5156df94cb2538d373db503445d2b07466396/src/frame_main.cpp),
its [live color dropper](https://github.com/amanosatosi/Aegisub_Toshi-ban/blob/28b5156df94cb2538d373db503445d2b07466396/src/dialog_colorpicker.cpp),
and [Kainote pane layout](https://github.com/bjakja/Kainote/blob/6e1bbb152bdca5249ad8c6d585ccb16e58dc9ec9/Kainote/TabPanel.cpp).
Their work surfaces share horizontal space; the audio/edit column adjoins video
and the grid spans the bottom. Aegisub captures desktop pointer movement into a
magnified neighborhood and supports both dragging and latching the dropper.
No upstream artwork, code, dependency or license obligation was added.

## Corrections retained and completed

| Area | Cause and resulting behavior |
| --- | --- |
| Rectangle creation | Gesture initialization previously set its tool baseline before draft commit, whose notifications could cancel/reset the tool. Initialization now retains the active snapshot and installs it after BeginGesture succeeds. The overlay paints a transparent input surface even without a clip. Packaged pointer drags check live handles/Mangetsu pixels, normalized commit, exact undo and Escape. Vector and unsupported clips in other selected lines are protected. |
| Vector preview | Hover previously added a dangling segment over the old closing edge. It now renders a copied, appended closed contour using the same mapping compensation as commit. Line/Bicubic captures check prospective closure, cubic handles and preview/commit equality. |
| Font browser | Popup was absent from the logical/control tree, allowing an open flag without a visible browser. It is now attached and has realized rows. Native list navigation needs a realized focused row; opening scrolls and focuses that row after layout. Tests wait for realization and deliver Down/Enter from it. Enter acceptance runs in the list tunnel before Avalonia's row handler consumes it. Loading placeholders cannot become font names; unavailable exact names remain unchanged. |
| Enter routing | Logical-parent traversal missed template focus contexts; Enter/Return enum aliases also failed the canonical hotkey name. Visual ancestry plus focused-editor state resolves the command. Enter commits/advances, Ctrl+Enter commits/stays, Shift+Enter immediately replaces selection with literal ASS `\N`, with stable caret/text after idle commit. Active preedit retains Enter ownership. |
| Color strips | Dedicated ColorStrip input draws a thin black/white marker, handles click/drag/arrows/Home/End and rolls back capture loss. Packaged hit tests and screenshots require no Slider descendants. |
| Eyedropper | Pointer capture samples a live 7×7 physical-pixel neighborhood into the dialog, with center marking, drag/latch acceptance and frozen neighbor selection. Escape/capture loss cancel; sampled RGB and independent alpha are checked. A failed first sample cannot leave the sampling timer running. |
| Audio | Time changes horizontal span, Amp changes display gain, Vol changes output gain. Link persists its control-position/attenuation relationship; unlink restores independence. Packaged checks exercise focused slider keys and actual panner-thumb drag, in addition to waveform/spectrogram settings and sash persistence. |

## Workspace balance

Primary visual tools use a compact vertical rail. Contextual options alone use
a bottom row. Help uses tooltips and command-bar status. Metadata, timing/margins
and formatting are packed into semantic groups with 2–4 DIP within groups and
10–12 DIP between groups, without repeated pane headers/cards.

The latest extra row removed is audio status: it now shares playback controls.
Fresh profiles reserve 180 DIP for audio, providing 120 DIP of waveform at the
default 1440×900 window, rather than the former 84 DIP. Saved splitter heights
remain respected. Video and grid proportions are unchanged. Acceptance requires
600×300 DIP video, 600×120 audio, 600×140 ASS text and 270 DIP grid height. The
decoded picture's fitted size is checked separately from overlay bounds.

Grid rows remain fixed at 26 DIP, with shaped-text containment for Japanese,
Burmese, Arabic, Latin, emoji and combining marks. The 20,000-event fixture
checks bounded container realization and recycling at the middle and end.

## Validation and manual QA

GitHub Actions is authoritative for managed tests, NativeAOT, packaging, native
providers and real-window verification. Source-only policy/generator checks and
`git diff --check` are run locally. The final chat report records the final commit,
CI run and downloaded screenshots actually reviewed; passing properties alone
does not establish visible workflow acceptance.

The no-clip pixel fixture uses a rectangle spanning 48–52% of script dimensions,
which cuts through its centered glyphs. The previous 40–60% rectangle could
contain the entire word at 1920×1080, producing identical pixels even when the
renderer applied the clip correctly. The live pixel-change assertion is retained.

Real Japanese/Burmese IME confirmation, Windows monitor-DPI transitions,
multi-monitor/HDR eyedropper behavior, caption/snap behavior and audio-device
latency remain manual QA. Routed key/preedit probes and scaled raster captures
do not simulate OS input methods or monitor transitions. No unrelated feature
expansion or weakening of existing assertions is included.
