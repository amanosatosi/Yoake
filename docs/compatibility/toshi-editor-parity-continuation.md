# Toshi-ban editor parity continuation

Scope: compare the whole editor with the local `Aegisub_Toshi-ban-codex-39-mode` source, preserving Yoake's tabs, independent document/media/undo lifetimes, Mangetsu behavior and Toshiki-only K-Timing. Do not remove Yoake workflows to match the reference. The supplied source is an archive without Git metadata; do not invent a source commit.

The existing `aegisub-command-feature-status.json` is a starting inventory, not proof of current behavior: for example, it still lists ASSDraw as missing although Yoake now has a packaged launcher. Re-audit commands and their actual workflows before changing status. Do not mark parity complete without the corresponding full GitHub Actions and packaged UI evidence.

## First editing checkpoint: insert at video time

The local reference's `src/command/subtitle.cpp`, `insert_subtitle_at_video`, inserts a fresh Dialogue before or after the active line at the current frame's START boundary, sets the configured default duration and copies the active style. It selects the new line and commits one insertion. It requires a selected line and loaded video. Yoake routes the two explicit command IDs through its document-owned editor and central frame timing; it retains the existing ordinary insert commands. The context and Subtitle menus expose both commands. Undo uses the existing lossless structural transaction. Managed regression covers availability, foreign-document rejection, frame start, duration, metadata, order and undo/redo. GitHub Actions and real menu/input verification are pending; no ledger promotion is implied.

## Branch validation checkpoint

At `d0a7646`, both Windows jobs published NativeAOT but timed out in startup acceptance after the motion-tracking Apply step. The captured profile log also showed media-close command availability reading a removed tab. `5302627` changes the acceptance driver to activate the actual focused button with Enter and guards the document lifetime; it adds a tab-removal regression. CI for that fix is pending. The timeout diagnosis is based on the captured stage and source inspection and must be confirmed by a new full run.
