# Yoake — native ASS subtitle editor

Yoake is a C#/Avalonia subtitle editor using FFMS2 for media and Mangetsu for live ASS rendering. It keeps document tabs near native window controls, video and visual tools on the left, audio above the event editor on the right, and a virtualized subtitle grid below. Splitters remain resizable.

## Editing workflow

Open an ASS file with **Ctrl+O**, then use **Video → Open video/audio**. Existing Aegisub project media references and same-basename video files are discovered when opening through the File command. Each tab owns its subtitle model, undo history, media session, waveform and playback position.

Use the grid to select rows: click, Ctrl+click, Shift+click, arrows, Home/End, and Page Up/Down. The current row has an accent stripe; rows active at media time have a subtle highlight. Drag column boundaries in the header to resize columns; widths are saved in settings. Right-click for row operations.

The edit panel buffers text and metadata. **Ctrl+Enter** commits; **Enter** in subtitle text commits and advances (creating a following line at the end); **Shift+Enter** inserts a newline. Moving to another row or tab commits valid edits. Invalid timings or margins keep the draft available for correction. Actual line breaks normalize to ASS `\N` when committed. IME preedit and open dropdowns retain their Enter handling.

Undo/redo includes text, metadata, timings, multi-row operations, styles, Script Info and visual edits. Continuous drags coalesce to one operation; Esc or capture loss rolls them back. The dirty indicator tracks drafts and the saved undo state. Save As updates the tab path/title; closing dirty documents offers Save, Discard and Cancel.

## Timing and visual tools

- Click the audio display to seek. Drag green/red markers to change start/end. **Shift+drag inside the selected region** moves the whole timing range.
- The three vertical audio controls are horizontal zoom, display amplitude, and playback volume. Link couples amplitude to volume; the audio/edit sash independently controls panel height. Wheel pans, Ctrl+wheel zooms at the pointer, and Shift+wheel adjusts amplitude. The bottom panner shares the same viewport.
- The audio mode selector switches between full sequential waveform peaks and a working, bounded viewport spectrogram. Analysis runs as cancellable background jobs. The spectrum is intentionally a basic linear-frequency view rather than a high-end timing spectrogram.
- **Crosshair** shows script coordinates and double-clicks to shift the visible selection, including both move endpoints and explicit origins. **Position** shows standby/start/end/origin handles and the current frame's movement position. Endpoint drags associate their time with that frame; the contextual action converts pos/move.
- **Rotate Z**, **Rotate X/Y**, and **Scale** provide rings, transformed grids and oriented guides. Ctrl snaps angles to 30° or scale to 25%; Shift constrains axes; Alt preserves scale aspect. Origin handles create/update `\org` explicitly.
- **Clip** creates and resizes rectangles, shades clipped regions and deliberately moves existing masks. **Vector Clip** has Select/box selection, Line, Bicubic, Convert, Insert, Remove, Freehand and Smooth subtools. Integer drawing scale, inverse masks and animated `\clippos` survive editing.
- **Distort** edits four corners through Mangetsu's eight-value `\distort`, upgrading a legacy six-value tag only on an intentional edit. Bounds are measured asynchronously through Mangetsu; raster bounds and complex run/layout cases have the limits documented in [visual typesetting architecture](docs/architecture/visual-typesetting.md).
- Visual gestures edit visible selected dialogue lines together, preview during the drag, undo once and roll back on Esc, capture loss, seek or tool/document changes. Relative/expression positions that cannot be interpreted remain preserved in the text editor.
- Mangetsu remains the preview authority. Overlays are editing handles, not another subtitle renderer.

## Shortcuts

| Action | Shortcut / context |
| --- | --- |
| New / Open / Save | Ctrl+N / Ctrl+O / Ctrl+S |
| Save As / close tab | Ctrl+Shift+S / Ctrl+W |
| Undo / Redo | Ctrl+Z / Ctrl+Y (also Ctrl+Shift+Z) |
| Find / Replace | Ctrl+F |
| Commit / commit and next | Ctrl+Enter / Enter in subtitle text or grid |
| Previous / next subtitle | Alt+Up / Alt+Down |
| Insert after / before | Insert / Ctrl+Insert in grid |
| Duplicate / split at cursor | Ctrl+D / Ctrl+Shift+D |
| Delete lines | Delete in grid |
| Copy / cut / paste / select all lines | Ctrl+C / Ctrl+X / Ctrl+V / Ctrl+A in grid |
| Move selected lines | Alt+Shift+Up / Down |
| Set start / end to media time | Ctrl+3 / Ctrl+4 |
| Play / pause | Space in video, audio or grid |
| Play current subtitle | R in video, audio or grid |
| Previous / next frame | Left / Right in video |
| Stop | Esc in video/audio |
| Cancel draft / gesture | Esc in editor/grid/visual tools |

The File, Edit, Subtitle, Timing, Video, Audio and View menus expose implemented commands. Styles Manager supports creation, duplication, editing, rename with event reference updates, deletion with a replacement style, reordering, undo and redo. Script Info and find/replace operate on the current document. Find supports case sensitivity, regular expressions with timeouts, and the selection captured when opening the dialog.

## Data and runtime guarantees

The source model preserves unknown sections, comments, unknown fields, field ordering, unknown override syntax, Mangetsu tags and mixed line endings. Records retain their local Format; structural edits preserve unrelated source lines. UTF-8, UTF-16 and UTF-32 BOM/endianness are retained. Unsupported legacy code pages are refused rather than silently corrupted. Saves write a temporary file before replacing the destination.

Native pointers stay in `Yoake.Native`. FFMS2 supplies indexed frame timestamps for VFR stepping. Audio output supplies the playback clock when audio exists; video-only playback uses a monotonic clock. There is no second audio-display timeline. No managed runtime dependency was added for the editor work.

The baseline is .NET 10 LTS (SDK `10.0.401`), Avalonia `12.1.3`, Windows first, with NativeAOT/trimming checks enabled. Normal settings use application data; an empty `yoake.portable` marker beside the executable selects portable settings.

## Validation and limitations

GitHub Actions is the build/test authority for this implementation. It runs source policy, Core regression tests, UI command-model workflow tests, compiled bindings, Windows NativeAOT publish, native runtime closure verification, strict desktop startup smoke, and a packaged editor/provider verification against deterministic AVI/PCM fixtures. No local compilation or native build was performed for this task.

Interactive MKV playback, audio hardware, Japanese/Burmese input methods, OS clipboard interoperability, pointer gestures and long-film laptop performance still need a hands-on release smoke test. The automated checks do not replace that test. Legacy code-page import, advanced vector-path construction, movement/rotation/tracking tools, automation, and a high-end spectrogram remain future work. Styles use explicit ASS field editors and hex/decimal color entries rather than font/color picker dialogs.

## Repository map and developer build

- `src/Yoake.Core` — lossless subtitle source model, editor operations, undo, commands, settings and jobs
- `src/Yoake.Native` — FFMS2, waveOut and Mangetsu provider boundaries
- `src/Yoake.UI` — Avalonia workspace, drafts, controls and dialogs
- `src/Yoake.App` — composition and packaged verification entry point
- `tests` — domain and command-model workflow regressions
- `ci` / `third_party` — authoritative native dependency and packaging pipeline
- `docs/architecture` / `luna.md` — architectural contracts and contributor rules

Windows executable builds generate the application `.ico` from its SVG master and require ImageMagick 7 on PATH. The native dependency entry point remains `ci/build_dependencies.ps1`; do not duplicate its acquisition/build logic. Developers outside this CI-only task can restore/build `Yoake.sln`, run both test projects, and publish `Yoake.App` for `win-x64` with `PublishAot=true`.

Yoake is BSD 3-Clause; see `LICENSE`. Third-party texts are packaged under `licenses`. Functional icons remain Yoake-owned SVG geometry and are independent from the application identity.
