# Toshi-ban editor parity continuation

Scope: compare the whole editor with the local `Aegisub_Toshi-ban-codex-39-mode` source, preserving Yoake's tabs, independent document/media/undo lifetimes, Mangetsu behavior and Toshiki-only K-Timing. Do not remove Yoake workflows to match the reference. The supplied source is an archive without Git metadata; do not invent a source commit.

The existing `aegisub-command-feature-status.json` is a starting inventory, not proof of current behavior: for example, it still lists ASSDraw as missing although Yoake now has a packaged launcher. Re-audit commands and their actual workflows before changing status. Do not mark parity complete without the corresponding full GitHub Actions and packaged UI evidence.

A source scan of literal `CMD_NAME` declarations in the local fork found 289 distinct names, 64 absent from that older ledger. This is a discovery list, not a count of missing features: some are aliases, dynamic recent-menu prefixes, or workflows already implemented under different Yoake IDs. It includes fork additions for folding, text sorting, reload, playback speed, OCR, curved text, perspective, chat styling and gradient editing. `audio/karaoke/39` is intentionally excluded by the user's Toshiki-only requirement. Each group needs source and behavior review, including licensing and native/provider implications before any dependency is considered.

## First editing checkpoint: insert at video time

The local reference's `src/command/subtitle.cpp`, `insert_subtitle_at_video`, inserts a fresh Dialogue before or after the active line at the current frame's START boundary, sets the configured default duration and copies the active style. It selects the new line and commits one insertion. It requires a selected line and loaded video. Yoake routes the two explicit command IDs through its document-owned editor and central frame timing; it retains the existing ordinary insert commands. The context and Subtitle menus expose both commands. Undo uses the existing lossless structural transaction. Managed regression covers availability, foreign-document rejection, frame start, duration, metadata, order and undo/redo. GitHub Actions and real menu/input verification are pending; no ledger promotion is implied.

## Text sorting checkpoint

The fork's `src/command/grid.cpp` exposes whole-document and selected-block text sorts, including a stripped variant. `src/ass_file.cpp` compares full text or `GetStrippedText`, and `src/ass_dialogue.cpp` defines the latter as plain blocks, excluding override/comment and drawing blocks. Yoake's version derives a comparison key without modifying ASS source, preserves raw escape and Mangetsu text, and uses the existing cancellable background sort, revision fence and undo transaction. Incomplete braces remain literal for repair. Managed and packaged verification remain pending.

## Branch validation checkpoint

At `d0a7646`, both Windows jobs published NativeAOT but timed out in startup acceptance after the motion-tracking Apply step. The captured profile log also showed media-close command availability reading a removed tab. `5302627` changes the acceptance driver to activate the actual focused button with Enter and guards the document lifetime; it adds a tab-removal regression. CI for that fix is pending. The timeout diagnosis is based on the captured stage and source inspection and must be confirmed by a new full run.
