# ASS authoring controls

The ASS editor remains an Avalonia TextBox. Its TextPresenter supplies colored text runs to the same shaping/layout engine, preserving native selection, clipboard, accessibility and IME input. During preedit the presenter delegates layout to Avalonia unchanged. Syntax is presentation only: cached token ranges never rewrite the subtitle source. Theme resources define semantic colors.

The lightweight scanner handles incomplete blocks, nested function arguments, drawing mode, colors, escapes, karaoke and Mangetsu furigana. The tag catalog references Mangetsu parser names and can be extended without changing the text control. Unknown syntax stays editable. Highlighting currently rescans the changed line, with cached results when only selection/layout changes; it does not maintain a document-wide parser.

Formatting commands analyze static overrides and the inherited style, snap selection boundaries to graphemes/ASS escape units, splice only requested tag parameters and restore the previous state after a selection. Transforms and unknown tags remain untouched. Reset inserts ASS reset semantics; it does not destructively strip arbitrary override blocks. Formatting is a semantic document undo operation, separate from committing previously typed text.

Styles Manager keeps its directly editable pane. Standard fields use font, numeric, boolean, color and alignment controls; extra Format fields remain in Advanced. A draft updates live preview and commits on focus loss, selection change or Apply through the command registry and existing document-local style transactions. Rename updates event references; deletion requires a replacement style.

Style Library stores local named collections in source-generated JSON beside settings. Each collection retains a lossless ASS source document, including extra fields. Copies are independent records. Library editors have their own undo histories. Corrupt library data is reported instead of silently overwritten.

Preview uses the packaged Mangetsu renderer over an opaque checkerboard destination. Debounced, cancellable background jobs serialize native work; the renderer and pixel/bitmap buffers are reused. ASS colors preserve BGR order and inverted alpha. Font names remain editable and missing fonts are indicated without substitution.

Packaged startup verification opens the real Styles Manager, realizes the custom ASS presenter and style controls and waits for an actual Mangetsu preview. This verifies compiled bindings and NativeAOT/native-provider integration; real-device IME, pointer/color interactions and font fallback still need interactive QA.
