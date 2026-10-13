# Checkpoint 2 source audit

Primary oracle: supplied read-only `C:\aegisub source\Aegisub-3.5.0` archive, without Git metadata. No source files in that tree were modified. The established 3.2.2 Automation compatibility policy remains unchanged.

Autosave/recovery inspected:

- `src/subs_controller.cpp`: `autosave_timer_changed`, constructor option/timer subscriptions, `SubsController::AutoSave`. Autosave defaults to enabled every 60 seconds; unchanged committed IDs skip writes. It copies `AssFile` before queued background serialization and uses `<filename>.<timestamp>.AUTOSAVE.ass`, with Untitled fallback. It updates the autosaved ID before the queued write; Yoake deliberately records successful publication only, allowing failed writes to retry.
- `src/dialog_autosave.cpp`: `DialogAutosave`, `Populate`, `OnSelectFile`, `ChosenFile`, `PickAutosaveFile`. Native resizable dialog groups filenames, displays newest versions first and leaves copies intact on cancel. It also lists original backups and recovered files.
- `src/command/subtitle.cpp`: `subtitle_open_autosave` invokes the picker and loads the chosen file after normal close checks.
- `src/libresrc/default_config.json`: `App/Auto/Save = true`, `App/Auto/Save Every Seconds = 60`, `Path/Auto/Save = ?user/autosave`.

Yoake differences: independent document GUID groups prevent identical basenames colliding; committed ASS bytes and raw valid/invalid draft fields are bundled atomically, preserving original encoding/BOM. Recovery opens separate unsaved tabs and retains original provenance instead of replacing an open document or treating a recovery path as the normal Save target. Portable/profile settings resolve the default directory. Options supports enable/interval/custom directory. History is bounded and successful saves/shutdown retain it safely; explicit discard confirms before removal. Yoake does not import Aegisub's original-backup/recovered directories or write Aegisub's plain autosave naming scheme, because those files cannot carry its invalid drafts coherently.

Stage 2A implementation and managed/native restart acceptance are pending CI verification. Neither `subtitle/open/autosave` nor `subtitle/attachment` has been promoted. Stage 2B must not begin until the full Stage 2A pipeline passes.

Attachment source entry points identified in the prior checkpoint: `src/ass_attachment.cpp`, `src/dialog_attachments.cpp`, `src/ass_parser.cpp`, `libaegisub/ass/uuencode.cpp`. Exact six-bit +33 encoding, 80-character wrapping, partial final groups, active-payload-before-section parsing and hardened extraction still require implementation and verification in Stage 2B. No attachment command or manager is claimed here.
