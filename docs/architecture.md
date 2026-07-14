# Yoake foundation architecture

This document records the architecture selected after a focused inspection of
the local Aegisub Toshi-ban source tree. It describes code that exists in this
repository now as well as the next migration boundaries. A future-facing
interface is not counted as an implemented feature.

## Findings that shaped the design

Aegisub Toshi-ban already has a useful per-project kernel, but it is constructed
once per window and mixed with view pointers:

- `src/include/aegisub/context.h` owns `AssFile`, `SubsController`, `Project`,
  selection, video, audio, search, and multiple wx views in one order-sensitive
  aggregate.
- `src/frame_main.cpp` constructs one such context for one frame. Multiple
  documents are multiple windows, not independent tabs.
- `src/subs_controller.cpp` provides strong per-context undo behavior and stable
  revision semantics, but restores whole files and must rebind raw dialogue
  pointers after undo.
- `src/async_video_provider.cpp` has a valuable monotonic request version that
  drops stale work, while its delivered events lack a media/session identity and
  can survive a provider switch.
- `src/subtitles_provider_libassmod.cpp` contains the Mangetsu RGBA path and good
  dynamic API probing, but shares mutable library state process-wide and can
  synchronously bounce from a worker to the GUI thread during renderer startup.
- waveform and spectrum calculations are invoked from the wx paint path, which
  lets analysis work block presentation.
- Toshi-ban themes are named JSON files, but applying a theme copies colors into
  global options instead of switching one atomic semantic palette.

The code worth retaining or extracting is the mature ASS model/parser/override
logic, serialization edge cases, command behavior catalog, hotkey precedence,
karaoke parsing and segmentation rules, timing policies, media backend behavior,
and Mangetsu-specific extensions. The wx object graph, raw-pointer identity, and
global mutable document assumptions are not migration targets.

## Ownership

```text
Application
├─ DocumentManager                       application-wide tab registry
│  ├─ DocumentContext                    one per real tab
│  │  ├─ ASS Document + SubtitleModel
│  │  ├─ selection and active line
│  │  ├─ QUndoStack + document state IDs
│  │  ├─ MediaSession
│  │  │  ├─ QMediaPlayer + QAudioOutput
│  │  │  └─ waveform decoder thread + numeric peak model
│  │  └─ MangetsuSession
│  │     └─ dedicated renderer thread, ASS_Library, renderer, and track
│  └─ DocumentContext …
├─ ThemeManager                          named immutable semantic palette
└─ immutable application services        future settings/commands/shortcuts/MRU
```

There is no `currentDocument` global used as storage. `DocumentManager` exposes
the selected context for command routing, but every tab remains alive and owns
its state while inactive. Closing a context tears down only that context's media
and Mangetsu threads.

Line identity is a UUID scoped by the owning document. QML never receives an
`AssDialogue *` or worker-owned provider pointer.

## Mutation and dirty-state contract

All implemented document changes enter through `DocumentContext` and become a
`QUndoCommand`. The list model's `apply*` functions are the internal projection
boundary and emit targeted Qt model notifications.

Each command records a before-state and after-state ID. The context separately
records the state ID that was last saved. This preserves the important Aegisub
behavior where save, undo, branching edits, and an asynchronous save can still
determine dirty state correctly. Saving a snapshot never marks newer edits
clean.

The current row/field commands are deltas. Structural commands retain event
vectors for correctness in the first stage. They can later become row deltas
without changing QML or the document mutation boundary.

## ASS compatibility boundary

`ass::Document` is deliberately a small compatibility adapter, not a claim to
replace Aegisub's mature ASS core. It parses editable event fields while retaining
unknown raw sections, script info, styles, attachments, extradata, project
metadata, and Mangetsu syntax for serialization. This makes the first Qt models
usable without binding them to wxWidgets.

The next core migration extracts and adapts the following BSD/ISC-licensed
Toshi-ban areas behind this boundary:

- `ass_parser.*`, `ass_dialogue.*`, `ass_override.*`, `ass_style.*`,
  `ass_attachment.*`, and `subtitle_format_ass.*`;
- relevant `libaegisub/ass/*`, including dialogue tokenization, time, and
  uuencoding; and
- Aegisub extradata and project-garbage compatibility.

Extraction must remove `wxString`, global options, UI dialogs, Boost intrusive
pointer identity, and `agi::Context` parameters. Until that extraction lands,
the adapter intentionally accepts canonical UTF-8 ASS v4+ event formats only
and rejects SSA or reordered event formats rather than rewriting them
destructively. It is not labeled the final parser.

## Thread and generation contract

Presentation objects are mutated only on the GUI thread.

| Work | Owner/executor | Stale-result rule |
| --- | --- | --- |
| subtitle file read/parse | Qt concurrent worker | result creates a new context only on successful completion |
| subtitle save | Qt concurrent worker | completion records the captured document state, not the current state |
| video/audio playback | per-document Qt Multimedia session | opening/closing increments the media generation |
| waveform decode/reduction | dedicated per-document thread | every peak result carries the media generation |
| Mangetsu render | dedicated per-document renderer thread | every result carries client and request IDs; the overlay accepts latest only |

The GUI never waits synchronously for media decoding or rendering. Teardown is
ordered: stop new requests, drain/stop the owned lane, destroy renderer/provider
resources, then destroy the document context.

Future decoder results use the full identity tuple described below; the existing
Qt Multimedia presentation path owns its internal decoder but the same public
contract is retained for custom backends:

```text
{ mediaSessionId, mediaGeneration, requestId, frameIndex,
  timestamp, subtitleRevision, colorRevision }
```

## Mangetsu boundary

Mangetsu is the only subtitle preview backend. The application loads an absolute,
application-controlled `mangetsu.dll`, `libmangetsu.so`, or
`libmangetsu.dylib`. A custom absolute path can be supplied with
`YOAKE_MANGETSU_LIBRARY`. Bare-name DLL search is not used.

Only the loaded module and immutable function table are application-wide. Each
`DocumentContext` owns an independent Mangetsu library, renderer, track, script
snapshot, and serial render lane. Missing Mangetsu or an ABI mismatch produces
an explicit overlay error; Yoake does not silently substitute upstream libass,
VSFilter, or xy-VSFilter.

The first adapter implements the RGBA and legacy mask render paths observed in
Toshi-ban. Embedded fonts, `\img` attachment/file registration, color-coding
metadata invalidation, and authoritative extension capabilities are the next
Mangetsu adapter work and must remain within this boundary.

## QML/C++ boundary

QML composes the shell, menus, delegates, controls, and visual interaction.
Subtitle mutation, ASS parsing/serialization, state identity, media lifetime,
waveform analysis, syntax tokenization, and Mangetsu calls remain C++.

The subtitle grid is a `ListView` backed by `QAbstractListModel`, with delegate
reuse and a small cache buffer. Updating one field emits one row/role change; it
does not rebuild the list.

`AssHighlighter` attaches to the `QTextDocument` behind a QML `TextArea`.
Unknown tags remain lexically valid so future Mangetsu extensions are not marked
invalid merely because the UI registry lags the renderer. Known standard,
transform, karaoke, drawing, color/alpha, numeric, and locally observed Mangetsu
families receive semantic spans whose colors come from `ThemeManager`.

## Current foundation status

Implemented as functioning code:

- Qt Quick application/build structure;
- independent document tabs and tab-local lifetime;
- asynchronous UTF-8 ASS opening and loss-conscious saving;
- editable event fields, virtualized grid, active/multi-selection;
- per-document undo/redo and asynchronous-save-safe dirty tracking;
- stable-document save/close dialogs and sequential application-exit arbitration
  across every dirty tab, including pending K-Timing edits;
- named semantic themes and theme-driven syntax colors;
- ASS/Mangetsu lexical coloring and `\fn` value-aware double-click selection;
- per-document media opening, playback, seek, timestamp/frame estimate, and
  frame stepping through Qt Multimedia;
- asynchronous numeric waveform generation, playback cursor, and drag-to-time;
- Original K-Timing with per-document session state, space-based initial cuts,
  existing karaoke loading, boundary dragging, tag selection, syllable
  audition, reset, and one-command commit;
- per-document Mangetsu sessions, RGBA overlay rendering, and latest-result
  rejection; and
- Windows-first GitHub Actions build/test/staging.

Not yet claimed as implemented:

- the full extracted Aegisub ASS/style/attachment/extradata model;
- style manager, find/replace, clipboard line formats, and the broader command
  catalog;
- custom media providers, track selection, timecodes/keyframes, and exact video
  frame indexing beyond metadata FPS;
- Mangetsu tag images/embedded fonts and a pinned renderer packaging pipeline;
- Toshiki K-Timing logical-slot and explicit-boundary interaction sessions; and
- autosave/recovery, preferences, and a shortcut editor.

Those are staged migration work, not empty UI placeholders in the current shell.
OCR and Automation/Lua are deliberately excluded and are not parity TODOs.
