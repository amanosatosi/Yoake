# Daily-driver checkpoint

Starting point: `c9654db63f3855c361d95b8c0fcc991a6422c477` on `automation-4-layer` (fetched 2026-11-16). The Toshi-ban remote includes `ver.3.0` at `8e9dd8f6ba7048ee09cac01dee1a695c0c1621fb`; this checkpoint does not constitute a full comparison of that branch.

## Implemented, awaiting packaged verification

- Separate UI font family and size settings, normalized and source-generated with existing settings. The preference command saves before publishing and never touches ASS documents. The existing asynchronous installed-family browser is reused. The Options appearance section previews changes and restores the last applied value when closed.
- Options has a section search and scrollable sections, but remains an incremental change to the old tabbed window. Existing shortcuts, video, recovery and external-tool controls have their previous immediate-save semantics. Search selects sections rather than filtering every individual preference. There is no universal Apply/Cancel or complete category coverage yet.
- A recovery failure now identifies a file occupying the configured recovery directory on both Windows and Unix.

## Verification and limits

Linux managed build and Core/UI tests exercise settings persistence, validation, preservation of subtitle source and history, and recovery failure. They do not establish that the font applies to every Avalonia control, that all layouts accommodate larger fonts, or that preview/cancel and IME work in a real window. Several controls retain explicit font sizes and fixed layout dimensions. Windows DPI, theme, font fallback, IME and packaged restart verification remain required. No parity ledger entries are promoted by this checkpoint.

The existing ledger and continuation documents record substantial earlier work but are not proof of behavior at this commit. Full comparison and end-to-end acceptance across document/editing, audio, video, visual tools, Mangetsu, Toshi-ban extensions and 3.2.2 Automation remain outstanding. In particular, no Automation 3.5 compatibility work is required by this assignment. A Windows NativeAOT portable build and its native provider/GUI acceptance remain outstanding.

## Karaoke split continuation

Compared Aegisub 3.2.2 (`f743d1411e09cbb2bd34ddd2d4b6738101fab710`, `src/command/edit.cpp`) and 3.5 (`43af875ea01e80052cc6c44f24fc8c5258261c6d`, same path). Both replace each selected line with timed syllable lines in a single operation. Yoake now exposes `edit/line/split/by_karaoke` in both Subtitle and grid context menus, uses the document-owned editor, retains the timing tags on resulting lines, and leaves untagged selections alone. It deliberately rejects absolute `\\kt`, grapheme-splitting boundaries, furigana, and lines containing additional overrides, whose effects may span syllables or use the original line time. This conservative restriction prevents silent source damage but leaves those cases incomplete. Supporting formatting overrides and proving visual equivalence need further work and rendered tests.

Managed tests cover selection, timing, Unicode, metadata, unknown-tag safety, atomic failure, and undo/redo. Real GUI interaction, Mangetsu rendering, Windows packaged acceptance, and CI have not been verified for this command; do not promote its ledger entry yet.

## Sol continuation — unified Options implementation (2026-11-16)

Started from remote `3204bcac8116b8a1749ec939f95e6d19eca83569`. Options now has a navigation sidebar, individual-setting search, one scrollable preference surface, shared Apply/OK/Cancel and category-scoped defaults. Supported preferences cover General, Appearance, Editor, Subtitle grid, Audio, Timing, Video, Automation paths, Keyboard shortcuts, Recovery and External tools. No placeholder Mangetsu or advanced settings have been invented: renderer authoring remains document-owned. The dialog stages shortcuts too; saving merges only edited preferences so unrelated runtime configuration and MRUs survive. Atomic persistence precedes publication. Font preview, missing-family retention, system-default reset and proportional grid/control typography remain independent of ASS fonts. Damaged-member recovery now also retains fast-jump and karaoke Auto Cut preferences.

Managed verification at this checkpoint: 714 Core and 173 UI tests pass on Linux (before the final damaged-member test addition). A separate native Options acceptance process passed on Linux twice, including real restart persistence, search, 780×540 resize with 24px font preview, menu typography, cancellation, scoped defaults, missing-family fallback and synthetic IME preedit. The shipping Windows acceptance script now runs this process twice and retains the previous keyboard/recovery checks with explicit Apply semantics. Physical Windows IME, DPI and full packaged Windows verification remain outstanding; no parity command is promoted on Linux evidence.

SDK `10.0.401` resolved ILCompiler `10.0.12`, affected by [GHSA-p3jp-9v83-76w9](https://github.com/advisories/GHSA-p3jp-9v83-76w9) / CVE-2026-111497. The advisory identifies SDK `10.0.403` and compiler/runtime `10.0.14` as patched. Updated the SDK pin and matching CI setup; local restore/build with the patched SDK no longer reports NU1903. NativeAOT artifacts must be rebuilt with the patched compiler; updating a shared runtime alone is insufficient.
