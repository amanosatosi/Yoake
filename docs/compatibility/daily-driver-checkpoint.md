# Daily-driver checkpoint

Starting point: `c9654db63f3855c361d95b8c0fcc991a6422c477` on `automation-4-layer` (fetched 2026-11-16). The Toshi-ban remote includes `ver.3.0` at `8e9dd8f6ba7048ee09cac01dee1a695c0c1621fb`; this checkpoint does not constitute a full comparison of that branch.

## Implemented, awaiting packaged verification

- Separate UI font family and size settings, normalized and source-generated with existing settings. The preference command saves before publishing and never touches ASS documents. The existing asynchronous installed-family browser is reused. The Options appearance section previews changes and restores the last applied value when closed.
- Options has a section search and scrollable sections, but remains an incremental change to the old tabbed window. Existing shortcuts, video, recovery and external-tool controls have their previous immediate-save semantics. Search selects sections rather than filtering every individual preference. There is no universal Apply/Cancel or complete category coverage yet.
- A recovery failure now identifies a file occupying the configured recovery directory on both Windows and Unix.

## Verification and limits

Linux managed build and Core/UI tests exercise settings persistence, validation, preservation of subtitle source and history, and recovery failure. They do not establish that the font applies to every Avalonia control, that all layouts accommodate larger fonts, or that preview/cancel and IME work in a real window. Several controls retain explicit font sizes and fixed layout dimensions. Windows DPI, theme, font fallback, IME and packaged restart verification remain required. No parity ledger entries are promoted by this checkpoint.

The existing ledger and continuation documents record substantial earlier work but are not proof of behavior at this commit. Full comparison and end-to-end acceptance across document/editing, audio, video, visual tools, Mangetsu, Toshi-ban extensions and 3.2.2 Automation remain outstanding. In particular, no Automation 3.5 compatibility work is required by this assignment. A Windows NativeAOT portable build and its native provider/GUI acceptance remain outstanding.
