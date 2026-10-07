# Desktop startup regression

The October 7 failure was a managed NullReferenceException. Windows Application event 1026 recorded `MainWindow.AttachModel → App.OnFrameworkInitializationCompleted → AppBuilder.SetupUnsafe → StartWithClassicDesktopLifetime → Program.Main`.

Assigning the DataContext synchronously invoked AttachModel, which dereferenced `GridColumnWidths.Count`. The user's settings contained only schemaVersion/theme/mainSplitRatio/gridHeight. Source-generated deserialization supplied null for the absent init-only collection, despite its C# initializer. The regression test committed before the fix reproduced this in Actions: `LegacyProfileRetainsNewCollectionDefaults` failed `Assert.NotNull(settings.GridColumnWidths)`. RecentFiles had the same hazard. A pristine profile returned `new AppSettings()` without deserializing, and avoided the crash. Command-model tests never constructed MainWindow; the old smoke checked only process survival.

## Startup order

1. Program initializes independent diagnostics/hooks and configures the packaged DLL search directory. Empty startup does not create FFMS2 sessions or a Mangetsu renderer.
2. Avalonia initializes platform services, creates App, registers services, loads App XAML/styles and completes framework setup. The dispatcher exception hook is installed.
3. App resolves and normalizes settings, creates the logger, registry, workspace, undo/background infrastructure and theme service, then applies the theme.
4. MainWindowViewModel registers commands before creating the initial document and initializing tabs/current-document state.
5. MainWindow loads compiled XAML, configures native Windows decorations and registers view adapters. DataContext assignment invokes AttachModel and initializes bindings/custom controls.
6. App assigns the desktop MainWindow. Desktop lifetime opens it; opened, loaded, first-layout and dispatcher checkpoints establish completion.

## Settings recovery

Normalization runs at persistence and view-model boundaries. Eight finite widths are restored: usable values survive, missing entries receive defaults, excess entries are dropped, nonpositive/nonfinite values recover and positive extremes are bounded. Null recent lists become empty; null/empty, duplicate and excess paths are removed. Layout/theme values recover independently. Named floating-point strings are accepted for recovery; bare NaN remains invalid JSON. Unknown future properties survive save through source-generated extension data.

Read/parse failures report why defaults were used and leave the original file unchanged. Startup never rewrites/migrates the user's file in place. The old source-generated serializer and NativeAOT support remain in use.

## Independent diagnostics

`%LOCALAPPDATA%/Yoake/logs/startup.log` records bounded startup checkpoints and Avalonia warning/error messages. `crash-<process-id>.log` contains timestamp, informational version/commit, OS/runtime/process architectures, dynamic-code/NativeAOT information, context and full exception/inner-exception stacks. Temporary-directory fallback works independently of settings and FileAppLog. Existing Avalonia traces retain their previous sink; normal runtime logging remains separate.

The top-level desktop path, AppDomain unhandled exceptions and Avalonia dispatcher hooks report fatal errors. Dispatcher exceptions remain unhandled; the desktop catch exits nonzero. Unobserved task exceptions are recorded without changing runtime policy. A native MessageBox reports the actual crash-log path and exception message even if Avalonia cannot construct a dialog. Native handles remain behind Yoake.Native. Verification modes suppress modal reporting so CI cannot mistake a crash dialog for a healthy process.

## Packaged verification

```
Yoake.exe --verify-ui-startup <report-path> --profile-directory <isolated-directory> --diagnostics-directory <log-directory>
```

This uses normal Avalonia desktop startup, the real MainWindow and its model. It requires opened/loaded/visible state, a native platform window, nonzero client size, actual header layout, ten dispatcher ticks, a realized subtitle row, initialized selection, custom-control models, toolbar commands and no Avalonia error-level startup messages. It inserts/undoes a row through the registry to exercise ListBox templates, restores a clean document, and shuts down through desktop lifetime. Success requires a PASS report and exit zero. An independent 25-second watchdog detects a missing window/blocked dispatcher; the runner also enforces 40 seconds.

`ci/smoke_windows.ps1` runs clean, exact legacy, damaged, null-collection and invalid-JSON profiles, then an expected bootstrap failure that must produce a full fatal report and nonzero exit. Profiles are isolated. Reports/logs upload even on failure. Native dependency, NativeAOT, packaging/runtime closure and packaged editor/provider checks remain enabled.

This exercises a real native desktop lifecycle, without Avalonia.Headless or a fake window. Hosted runs avoid stealing foreground focus; visibility is checked through Avalonia/native platform state. It does not prove human-visible composition, input/IME, snap/caption interactions, every GPU driver, DPI configuration or audio-device behavior. Those require ordinary-desktop validation. Downloaded packaged verifiers can run without local compilation.
