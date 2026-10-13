# Advanced fansubbing workflow continuation

Working branch: `automation-4-layer`. Local session starting HEAD: `d80159076d78ece105803e200c2642a2008df341`. No branch creation, local compilation, dependency changes or Automation rewrite. Unrelated local `agents.md` and the existing `luna.md` deletion remain outside the commits.

## Checkpoint 1: Original and Paste Over

Super Paste is the user's nickname for Paste Over, not an additional tool.

Implementation commits so far:

- `fcbf4946424a8d3764da4069a4ae498c6bfab472`: document-owned Original snapshot, semantic Original commands, compact read-only UI, shared lossless Paste Over transaction, real field dialog, managed regressions and separate native packaged acceptance.
- `72c36cd6a1dcc74ae6bee573f1c44099d64802bb`: explicit test assertion namespace.
- `074381da608484b7be697f36479556b8a80e5c23`: preserve an unselected active draft across Paste Over publication.
- `596d8ceba2efa62bc0c0a9a1f6cc9edd6daacbc2`: packaged diagnostics namespace correction.

Verified Checkpoint 1 implementation HEAD: `4b82a9a682fdca27ca7c3a036193e8bc9a921ac8`. Full [push run 38420848401](https://github.com/amanosatosi/Yoake/actions/runs/38420848401) passed source policy, 487 Core / 120 UI / 34 Automation tests (641 total, 25 new cases), Windows NativeAOT publishing, all startup profiles, all packaged acceptance processes, and native providers. The dedicated Original/Paste Over process and the subsequent complete Video presentation process both passed. Matching [PR run 38420854188](https://github.com/amanosatosi/Yoake/actions/runs/38420854188) also passed every job. Both runs are fully green.

Promoted `edit/insert_original` and `edit/line/paste/over` from missing, and `edit/revert` from partial. Totals: 141 complete, 51 partial, 31 missing, 13 deliberately N/A, 1 superseded. Video remains 55 complete, 0 partial, 0 missing, 1 N/A. Original Clear/Clear Text reuse the previously complete backend.

Repairs after the first implementation: `a091c8a` retains default editor height and hardens clipboard/preferences; `e1fd9a7` gives focused buttons native Enter; `59942dd` adds native selection preconditions and diagnostics plus consistent view notification on persistence failure; `2c9d4e2` fixes actual focus-loss selection clearing; `3f016e3` threads cancellation through parsing/dialog/publication; `17835c7` keeps nested lambda parameter names distinct; `3a16026` adds native modal cancellation acceptance; `e008b90` publishes observer notifications only after rollback has completed.

Earlier NativeAOT binaries published successfully, but packaged checks exposed reduced default editor height and later collapsed selection before Insert Original. Assertions were retained, product defects were repaired, and selection setup gained stronger visible-control checks. All startup profiles and existing keyboard/timing/media/Video utility acceptance passed at `e1fd9a7` before the new selection assertion. Full current branch verification, including subsequent Video presentation acceptance, now passed. `4b82a9a` corrected availability acceptance to inspect the actual opened menu and semantic/effective disabled states according to the pinned framework contract, retaining the availability requirement.

Detailed source oracle and deliberate differences: [Original/Paste Over reference](aegisub-original-paste-reference.md). Existing Clear/Clear Text are reused; The ledger now uses the real Original/Paste Over commands. Escape's existing `edit/cancel` behavior remains distinct from restoring Original.

Remaining manual QA for this checkpoint: hardware Japanese/Burmese IME composition and focus transitions, native clipboard contention with other applications, ordinary laptop sizes at 125/150/200% scaling, and representative externally copied ASS documents. Synthetic preedit and real native clipboard/menu/dialog input are covered in packaged acceptance; they do not replace hardware IME QA.

Checkpoint 1 implementation files:

- Core operations/settings: `src/Yoake.Core/Subtitles/SubtitlePasteOver.cs`, `src/Yoake.Core/Settings/AppSettings.cs`, `src/Yoake.Core/Settings/SettingsStore.cs`, `src/Yoake.Core/Hotkeys/HotkeyInputPolicy.cs`.
- UI/commands: `src/Yoake.UI/MainWindow.axaml`, `src/Yoake.UI/MainWindow.axaml.cs`, `src/Yoake.UI/PasteOverWindow.cs`, `src/Yoake.UI/Services/IEditorDialogs.cs`, `src/Yoake.UI/ViewModels/MainWindowViewModel.cs`, `MainWindowViewModel.LineOperations.cs`, `MainWindowViewModel.OriginalAndPaste.cs` in the same ViewModels directory.
- Managed regressions: `tests/Yoake.Core.Tests/SubtitlePasteOverTests.cs`, `tests/Yoake.UI.Tests/OriginalAndPasteWorkflowTests.cs`, and the clipboard/dialog test abstraction in `EditorWorkflowTests.cs`.
- Packaged integration: `src/Yoake.App/OriginalAndPasteUiAcceptanceVerification.cs`, `src/Yoake.App/StartupOptions.cs`, `src/Yoake.App/App.axaml.cs`, `ci/acceptance_windows.ps1`.
- Documentation: the detailed reference audit, this continuation, and the parity JSON/Markdown only after full successful verification.

## Pending checkpoints (no implementation or parity promotion yet)

2. **Autosave/recovery and attachments.** Inspected `src/subs_controller.cpp` (`autosave_timer_changed`, `SubsController::AutoSave`), `src/dialog_autosave.cpp`, `src/command/subtitle.cpp`, `src/ass_attachment.cpp`, `src/dialog_attachments.cpp`, `src/ass_parser.cpp` and `libaegisub/ass/uuencode.cpp` in the supplied 3.5 tree. Defaults enable autosave every 60 seconds; upstream copies the document before background serialization. Its recovery dialog groups files and versions. ASS attachment data uses six-bit values plus 33, 80-character wrapping and partial final groups; this is not ordinary uuencoding. The hardened attachment parser prioritizes active encoded data before section-header detection, including bracket-shaped payload. Extraction rejects separators, colons, control characters, trailing dot/space and Windows reserved names, and asks before overwriting. Yoake's existing raw ASS source preserves sections but needs this encoded-data context when interpreting attachment spans. Design recovery publication as an immutable, document-owned snapshot with original encoding/path/identity, one atomic publication, separate retained invalid draft values, per-document cancellation, bounded history/storage and explicit recovery into a separate unsaved document. Never overwrite manual work or drop the only valid recovery on decline/failure. Reuse settings/profile path resolution and `AssDocument` encoding/atomic-save semantics; current manual Save is synchronous on the owning UI thread. Add dedicated managed and native recovery/attachment acceptance, including subsequent process recovery.

3. **Toshiki K-Timing.** Supplied Toshi-ban is an archive without Git metadata. Located separate `audio/karaoke/toshiki_ktiming` and `audio/karaoke/39` commands in `src/command/audio.cpp`. Inspected their toggle/validation distinction, `src/audio_timing_toshiki.cpp`, `src/toshiki_timing_draft.h`, and `src/audio_karaoke.cpp` Auto Cut paths; identified `AssKaraoke::AutoSplitJapaneseKana` in `src/ass_karaoke.cpp` and `tests/tests/toshiki_k_timing_model.cpp`. Port the explicit slot-plan/preview/boundary workflow and its language behavior, not 39 Mode's capture/retake heuristics. Shared types in `timing39.h` do not make the two user workflows interchangeable. Reuse `AutomationKaraokeParser`, structured syntax, existing audio timing transactions and the central playback clock. Full interaction/source audit and implementation are still required.

4. **Resample Resolution.** Inspected `src/resolution_resampler.cpp` parameter classifications, drawing transformation, style scaling, aspect policies and LayoutRes handling; locate dialog and override-parameter definitions before implementing. Absolute X/Y/XY, relative X scale, drawing coordinates and style spacing/font/outline/shadow have different rules. Upstream also offers color conversion; the user requires unrelated matrix/color settings preserved. Inspect actual pinned Mangetsu parsing rules before structurally scaling extensions; preserve ambiguous/unknown geometry with warnings. No global numeric/regex substitution. No implementation yet.

5. **Font Collector.** Identified `src/dialog_fonts_collector.cpp`, `src/font_file_lister.cpp`, `src/font_file_lister_gdi.cpp` and platform variants. Inspected style-reset/inline font tracking and Windows DirectWrite/GDI file resolution entry points. Retain requested names separately from actual fallback faces; inspect collection faces and distribution rights. Native handles must remain in a provider, with explicit disposal, progress and cancellation. No implementation yet.

6. **Styling Assistant.** Inspected `src/dialog_styling_assistant.cpp` active-line refresh, `Commit(bool next)`, list single/double-click behavior, style-name completion, preview/media buttons and hotkey forwarding. A single click commits/stays, double click commits/advances; invalid style names do not commit. Operate on the existing document/draft/selection, with explicit modeless lifetime and IME guards. No implementation yet.

7. **Kanji Timer.** Identified `src/dialog_kara_timing_copy.cpp`, `libaegisub/common/karaoke_matcher.cpp`, kana table, karaoke parser and `tests/tests/karaoke_matcher.cpp`. Inspected source/destination group controls and matcher entry points. Full alignment workflow audit and implementation are still required, sharing the existing karaoke parser and one undo transaction.

8. **ASSDraw.** The supplied 3.5 `tool.cpp` has no launcher; the established 3.2.2 policy remains authoritative for this tool. Inspected 3.2.2 `tool_assdraw`, which launches the data-directory ASSDraw3 executable and conditionally registers it when present. Yoake must provide a configured/installed executable discovery path, safe external launch, exact missing message `ASSDraw3.exe doesn't exist.`, real menu/shortcut and launch failure handling. Never download/build it or execute attachments. No implementation yet.

All pending workflows need semantic routing, usable UI, explicit document ownership, managed regressions, separate relevant Windows NativeAOT packaged acceptance and verified ledger updates. Do not group them into one unverified commit or weaken the complete Video/Mangetsu/Automation acceptance.


This session completed and verified Checkpoint 1. Checkpoints 2–8 remain pending; their source entry points and next implementation/acceptance requirements are recorded above. No placeholder command registration or parity promotion was made for them. The next checkpoint is autosave/recovery plus attachments.
