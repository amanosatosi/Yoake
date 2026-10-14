# ASSDraw3 external integration source audit

Research record. No launcher implementation or parity promotion is claimed until its independent full packaged verification passes.

Primary oracle: supplied Aegisub 3.2.2 `src/command/tool.cpp`, `tool_assdraw::operator()` and `init_tool`; Windows `libaegisub/windows/path_win.cpp` and common `Path::SetToken` in `libaegisub/common/path.cpp` (existing executable tokens become their parent directory). The command is `tool/assdraw`, its menu label is ASSDraw3, and its operation launches the quoted executable at `?data/ASSDraw3.exe` via asynchronous `wxExecute`. The data token is the executable's parent directory on Windows. Registration is conditional on that executable existing. It neither imports an ASS file nor returns drawing data to the active editor.

The requested Yoake policy deliberately keeps the command discoverable when missing and shows the exact message `ASSDraw3.exe doesn't exist.`. A configured existing executable takes precedence over supported application-data locations; profile settings use the existing installed/portable resolution. Launch must use an explicit `ProcessStartInfo.FileName`, no shell-command concatenation, no subtitle-derived arguments and no attachment-derived path. Release the local process handle without waiting for the external application or coupling its lifetime to Yoake.

Tests must use a harmless dedicated process stub compiled only in GitHub Actions, not ASSDraw sources or downloads. Verify actual Tools menu and remapped keyboard routing, missing-message and configuration controls, Unicode/spaces, fallback discovery, removed/inaccessible executables, launch errors and unchanged document/media/draft/history. Real ASSDraw desktop drawing behavior remains manual QA because this checkpoint supplies a launcher, not an embedded replacement application.

## Implementation awaiting verification

The launcher is implemented through an explicit configured path, application root and supported data subdirectory, with background startup, no shell arguments, independent process lifetime and central failure handling. The command stays visible when missing; its native exact-message dialog opens the actual External tools options tab. Existing source-generated settings persist the new member, including damaged-profile recovery. Managed regressions and a separate real native acceptance process are present; do not promote until the full pipeline passes.
