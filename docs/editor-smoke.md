# Manual release smoke

Use the Windows portable artifact from the implementation PR's green Actions run, a real MKV with audio, and `tests/Yoake.Core.Tests/Fixtures/fansub.ass` (then a representative production script).

1. Open the ASS, then MKV. Check video, Mangetsu subtitle pixels, duration, waveform, play/pause/stop and synchronization.
2. Select rows by click, Ctrl, Shift and keyboard. Resize columns, scroll to the end of a large file and return to the current row. Confirm playback does not steal selection.
3. Drag start/end and Shift+drag the timing range. Undo/redo once per gesture. Esc and capture loss must restore the original timing.
4. Edit text, actor, style, margins, effect and layer. Test Enter/Shift+Enter/Ctrl+Enter, actual newlines and literal `\N`. Try Japanese IME, Burmese combining text and family emoji. Invalid timing must retain the draft.
5. Duplicate several rows, toggle comments, insert before/after, move, join, copy/cut/paste, select all and delete. Undo/redo each operation.
6. Split at a grapheme boundary. Verify unknown tags remain unchanged; inspect the second line's leading overrides.
7. Create, duplicate, rename, edit, reorder and delete a style with a replacement. Undo reference changes. Edit Script Info resolution/title.
8. Use Ctrl+3/Ctrl+4, play current line, jump start/end and frame-step through VFR content. Seek while playing and after pause.
9. Move a static position. Edit rectangular clip/iclip and vector control points, including explicit integer scale 3. Shift+drag a vector clip. Cancel both tools and verify one-step undo.
10. Switch waveform/spectrum, scroll, zoom rapidly, switch tabs while indexing/analysis runs, and confirm inactive work cannot replace the current preview.
11. Find next/previous, case-sensitive and regex searches, replace and replace all in captured selected rows. Undo replace all once.
12. Save As, close with Save/Discard/Cancel, reopen, and diff unknown sections/fields/override spans against the original. Verify BOM/newlines and another tab's independent history/media.
13. Check native caption buttons, snap layouts, resize hit tests, theme switching and laptop-scale workspace proportions.

Automated CI covers domain/command workflows and native provider calls; interactive audio-device, IME and pointer behaviors need this hands-on check.
