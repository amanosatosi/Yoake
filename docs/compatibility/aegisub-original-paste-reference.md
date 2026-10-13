# Original text and Paste Over reference

Primary source: the supplied read-only `C:\aegisub source\Aegisub-3.5.0` archive. It has no Git metadata. The supplied Toshi-ban archive also has no Git metadata; its `src/command/edit.cpp` retains the same Original and Paste Over workflows. Neither tree contains a Super Paste command. The user clarified that Super Paste is a nickname for Paste Over, not another feature.

Inspected Aegisub 3.5 files/functions:

- `src/initial_line_state.cpp`: `InitialLineState::OnActiveLineChanged` captures text only when the active line ID changes, including clearing on no active line. It does not subscribe to commits or replace the snapshot on typing, undo or redo.
- `src/subs_edit_box.cpp`: the read-only `secondary_editor`, `OnLineInitialTextChanged`, `OnSplit`, `DoOnSplit`, and the bottom buttons expose Show Original, Revert, Clear, Clear Text and Insert Original. Visibility is a saved application preference, initially false.
- `src/command/edit.cpp`: `edit_revert`, `edit_clear`, `edit_clear_text`, `edit_insert_original`, `get_dialogue`, `paste_lines`, `paste_over`, and `edit_line_paste_over`. Insert replaces the text selection. Clear Text keeps non-plain blocks, including drawings. One selected row pastes downwards from the active row, stopping at the document end; multiple selected rows receive clipboard records in sorted selection order, stopping at the shorter count. Paste Over retains active row and selection and makes one commit.
- `src/dialog_paste_over.cpp`: eleven fields, including Comment; All/None/Times/Text replace the field selection, successful confirmation saves preferences, cancellation makes no field edit.
- `src/libresrc/default_config.json`, `default_hotkey.json`, and `src/libresrc/default_menu.json`: Show Original preference, Paste Over preferences, default Ctrl-Shift-V and Edit menu exposure. Yoake retains its existing hotkey profile and contextual/IME guards.
- Supplied Toshi-ban `src/command/edit.cpp` and `src/dialog_paste_over.cpp`: the corresponding Original/clear/insert and single-versus-multiple Paste Over paths, without a separate Super Paste workflow.

Yoake implementation uses one original snapshot per document's currently active line. Draft reloads and history changes on that same object retain it. Returning to a different line captures its current committed text; switching tabs restores the owning document's snapshot. The read-only box sits above the edited text, below the existing Audio region. Show Original uses a semantic command and persists in the existing source-generated settings.

The ASS editor explicitly retains selection when focus moves to authoring controls. [Avalonia 12.1.3 `TextBox`](https://raw.githubusercontent.com/AvaloniaUI/Avalonia/12.1.3/src/Avalonia.Controls/TextBox.cs) defaults `ClearSelectionOnLostFocus` to true, which collapsed the selected range before keyboard activation of Insert Original. The packaged assertion exposed this real workflow defect; the input policy and selection retention were fixed, with the assertion retained.

Availability acceptance opens the real Edit menu and checks both the semantic command and `IsEffectivelyEnabled`. [Avalonia 12.1.3 `MenuItem`](https://raw.githubusercontent.com/AvaloniaUI/Avalonia/12.1.3/src/Avalonia.Controls/MenuItem.cs) combines command availability in `IsEnabledCore` and defers command reevaluation while a parent menu is closed; its raw `IsEnabled` property alone is not the visible disabled state. The packaged test retains the no-selection assertion against the actual exposed menu.

Paste Over parses on a background task and captures document, revision, active line, selection and draft values before reading the clipboard or showing the modal field selector. A changed owner/input cannot receive the delayed result. Every combined destination is validated before any mutation. A valid active draft participates in the same transaction as the paste; cancel/None/failure leave it pending. Untouched columns, records, tags and source newline spelling remain exact.

Deliberate differences:

- Full ASS clipboard documents and custom Format ordering (including columns after Text and Actor/Name aliases) are supported through Yoake's existing lossless parser. Upstream parses standalone standard dialogue records.
- Malformed structured ASS is reported rather than silently pasted as plain text. Plain clipboard text retains upstream trimmed, nonempty-line behavior and default dialogue metadata. Clipboard size/row limits bound the work.
- A selected field absent from a custom source/destination Format is preserved; unknown columns are never copied implicitly. Resulting invalid time ranges fail atomically instead of admitting an invalid editor state.
- First-use fields default to Text. The supplied 3.5 default configuration has ten flags although the dialog has eleven fields; resizing that list would select Effect rather than Text. Yoake avoids that surprising default and remembers all eleven fields thereafter.
- Text insertion protects Unicode grapheme boundaries. The original snapshot and presentation controls never rewrite tags.

Verification is pending the dedicated managed and Windows NativeAOT packaged acceptance checkpoint. No parity promotion is justified until those checks pass.
