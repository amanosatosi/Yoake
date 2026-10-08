# ASS authoring controls

The ASS editor remains an Avalonia TextBox. Its TextPresenter supplies colored text runs to the same shaping/layout engine, preserving native selection, clipboard, accessibility and IME input. During preedit the presenter delegates layout to Avalonia unchanged. Syntax is presentation only: cached token ranges never rewrite the subtitle source. Theme resources define semantic colors; imperative lookups explicitly pass ActualThemeVariant so dark/light dictionary entries resolve instead of falling back to plain foreground.

The lightweight scanner handles incomplete blocks, nested function arguments, drawing mode, colors, escapes, karaoke and Mangetsu furigana. The tag catalog references Mangetsu parser names and can be extended without changing the text control. Unknown syntax stays editable. Highlighting currently rescans the changed line, with cached results when only selection/layout changes; it does not maintain a document-wide parser.

Formatting commands analyze static overrides and the inherited style, snap selection boundaries to graphemes/ASS escape units, splice only requested tag parameters and restore the previous state after a selection. Transforms and unknown tags remain untouched. Reset inserts ASS reset semantics; it does not destructively strip arbitrary override blocks. Formatting is a semantic document undo operation, separate from committing previously typed text.

Styles Manager keeps its directly editable pane. Standard fields use font, numeric, boolean, color and alignment controls; extra Format fields remain in Advanced. A draft updates live preview and commits on focus loss, selection change or Apply through the command registry and existing document-local style transactions. Rename updates event references; deletion requires a replacement style only when script events reference it.

Style Library stores local named collections in source-generated JSON beside settings. Each collection retains a lossless ASS source document, including extra fields. Copies are independent records. Library editors have their own undo histories. Corrupt library data is reported instead of silently overwritten.

Preview uses the packaged Mangetsu renderer over an opaque checkerboard destination. Debounced, cancellable background jobs serialize native work; the renderer and pixel/bitmap buffers are reused. ASS colors preserve BGR order and inverted alpha. Font names remain editable and missing fonts are indicated without substitution.

Packaged startup verification opens the real Styles Manager, realizes the custom ASS presenter and style controls and waits for an actual Mangetsu preview. This verifies compiled bindings and NativeAOT/native-provider integration; real-device IME, pointer/color interactions and font fallback still need interactive QA.

The compact numeric theme derives from Avalonia’s Fluent numeric/spinner themes, retains NumericUpDown template parts and input handling, and explicitly lays out the native spin buttons. Cross-template style selectors did not override Fluent’s fixed horizontal button widths in the packaged application, so sizing lives in the focused theme instead. Its vertical 18-DIP steppers leave at least 48 DIPs for editable values in the event panel. Timing/margin clusters and formatting groups reflow independently; audio/edit splitter rows have minimum useful heights. Styles Manager uses constrained resizable columns and stable operation grids. These are DIP constraints, not device-pixel assumptions.

`StyleEditSession` owns one validated draft across script, preset and collection selections. Switching commits to the previous target before attaching the next draft; invalid edits keep the original target. Logical focus departure (including leaving composite font/color/numeric controls), Apply and selection changes commit one document transaction. Explicit color acceptance commits once. Library serialization failures remain reportable and retryable.

`AssFormattingState` exposes static caret colors including channel alpha and nullable flag states. Mixed/animated selections are indeterminate rather than asserting a rendered state; swatch tooltips identify their static semantics. Numbered Mangetsu color/alpha gradients share the extensible tag catalog. The TextBox and preedit path are unchanged.

Packaged authoring verification checks the native numeric text part bounds, time fields, reflowed editor, swatches and inline style controls at normal and narrow window sizes. It checks every syntax category reaches real shaped text runs in both themes, tests native formatting selection preservation, commits script/library drafts through shipping controls and waits for the latest requested Mangetsu preview revision. PNGs under `authoring-visuals` capture the real controls at 96/120/144/192 DPI for review. These raster captures do not simulate an OS monitor-DPI transition; real Windows scaling, IME and pointer QA remain manual.

On constrained CI desktops the two requested main-window sizes may be clamped to the same work area. Verification also narrows the real video/editor splitter to the supported 480-DIP editor column and requires a measurable reduction, so responsive checks cannot silently run twice at the same editor width. The multilingual sample renders into a reused 640×300 Mangetsu surface before fitting the preview area, avoiding the default three-line sample clipping.

Style and color text fields observe synchronous TextProperty notifications, not Avalonia’s deferred TextChanged event. This keeps a logical draft current before a command changes selection and prevents queued old-field text events from writing into the next style. Packaged verification changes exact color, numeric size and Unicode name, then switches immediately and checks both serialized values and event references.

The window resolves edit hotkeys through visual ancestry and focused-editor
state across control templates. Enter commits/advances; Ctrl+Enter commits/stays.
`text/insert/hard-newline` replaces the current selection with literal ASS `\N`
in the live draft, updates the caret immediately and joins the normal undoable
edit burst. Active preedit owns Enter in both window routing and AssTextBox;
the TextBox does not insert a physical newline while composition is active.
Packaged verification sends routed keys to the focused shipping editor and
checks text stability beyond 900 ms. Real Windows IME confirmation remains a
manual check because synthetic preedit does not simulate an OS input method.

FontPicker's custom Popup belongs to its logical/control tree. Its installed
family ListBox has an actual popup visual root; loading shows the exact current
family and refreshes an already open list when enumeration finishes. Missing
exact names remain unchanged and explicitly unavailable. Packaged tests open
the Subtitle Font dialog, inspect realized rows/dimensions, and choose by row
release and keyboard rather than relying on IsOpen alone.
