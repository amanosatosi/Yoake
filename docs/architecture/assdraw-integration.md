# External ASSDraw3 integration

`tool/assdraw` is an application-level launcher, not a document importer, renderer or vector-editor replacement. Discovery checks the explicitly configured absolute `.exe` first, then `ASSDraw3.exe` beside Yoake and in its supported `data` subdirectory. A missing/disappeared configured path falls back to these locations. The command remains discoverable when absent, with the exact required message and a direct route to Options → External tools.

Configuration belongs to the existing source-generated settings/profile system. Portable installations use the existing portable profile resolver; application-data discovery always uses the application directory. Damaged unrelated settings retain the executable member through the existing recovery path. No subtitle/attachment text is used for executable discovery or arguments.

Filesystem discovery and process startup run through the existing background-job service. `ProcessStartInfo.FileName` is explicit, with no concatenated shell command or subtitle-derived arguments, and `UseShellExecute=false`. The local process handle is disposed immediately; its exit and window lifetime remain independent of Yoake. Windows reports invalid/inaccessible executable failures through the central UI-safe command error route. There is no wait for external exit, process-kill-on-document-close behavior or additional playback clock.

The configuration tab and missing-message dialog use real native Avalonia controls and protect IME preedit Enter/Escape. New handlers are unsubscribed on close. Source, draft, selection, media and pending Toshiki state are not committed or changed by launch/configuration.

Packaged acceptance uses an original harmless native process fixture compiled only in GitHub Actions. It verifies real menu/key activation, configuration and exact missing message, Unicode/spaces, persisted settings, launch returning while the independent process remains alive, repeated launch and exit, configured disappearance, both bundled locations and a genuine Windows bad-executable failure. No ASSDraw source is downloaded, compiled or bundled. Actual ASSDraw3 drawing behavior remains manual QA.
