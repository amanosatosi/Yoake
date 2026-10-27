# Yoake agent rules

Yoake is a native subtitle authoring application.

Do not trade architecture, editing ergonomics, or data integrity for a demo that merely looks implemented.

## Non-negotiable architecture

1. **NativeAOT stays green.**
   Do not add runtime code generation, `Reflection.Emit`, arbitrary `Assembly.Load`, dynamic managed plugin DLL loading, or reflection-heavy serialization that breaks trimming/AOT.

2. **Do not casually add dependencies.**
   Before adding one, justify its purpose, license, maintenance status, NativeAOT/trimming behavior, runtime/binary cost, and why .NET/Avalonia cannot reasonably solve the problem already.

3. **Commands own semantic actions.**
   Buttons, menus, context menus, hotkeys, and future command-palette entries invoke stable command IDs.
   UI event handlers may handle presentation/input plumbing, but must not become the only implementation of application behavior.

4. **All editor mutations participate in undo.**
   Logical actions produce logical undo operations.
   Continuous gestures such as drags are one transaction.
   Transactions roll back unless explicitly committed.
   Esc, pointer-capture loss, cancellation, and exceptions must not leave partial edits.

5. **ASS editing is structured and lossless.**
   Never implement ASS changes using global regex replacement or naive backslash splitting.
   Preserve unknown tags, fields, sections, ordering, and syntax that Yoake does not understand.
   Mangetsu/custom syntax must survive unrelated edits unchanged.

6. **No global current-document state.**
   Work through explicit workspace/document/session lifetimes.
   Tabs must remain independent in document state, media, renderer state, and undo history.

7. **Native state stays behind provider boundaries.**
   Do not spread FFMS2 handles, FFmpeg structures, Mangetsu/libass pointers, Win32 handles, or other provider-native state through application/UI layers.

8. **There is one media timeline authority.**
   FFmpeg, FFMS2, audio output, video rendering, and future media providers must not maintain competing playback clocks.
   Follow the central timeline architecture documented by the repository.

9. **Expensive work must not block the UI.**
   Indexing, waveform generation, spectrogram generation, media analysis, scanning, and heavy renderer work must be cancellable/background-capable.
   Obsolete work should be cancelled rather than allowed to pile up.

10. **Complex scripts and IME are first-class.**
    Never assume a UTF-16 code unit is a user-perceived character.
    Preserve grapheme-cluster behavior, shaping, combining marks, Japanese IME, Burmese and other complex scripts.
    Do not enable invariant globalization.

11. **Keep platform behavior native.**
    Custom/integrated Windows chrome must preserve snap layouts, caption/system buttons, maximize/restore, resize hit-testing, DPI behavior, multi-monitor behavior, and the system menu.
    Do not replace this with a fake borderless-window implementation.

12. **Async command failures are UI-safe.**
    Report failures through centralized logging/error reporting.
    `ICommand.Execute` must not leak exceptions.
    UI notifications such as `CanExecuteChanged` must occur through Avalonia's UI dispatcher.

## Product / editor behavior

13. **Do not call scaffolding a completed subsystem.**
    A button, interface, placeholder panel, or TODO-backed path does not mean a feature is implemented.

14. **Judge UI work by the workflow, not by the existence of controls.**
    When matching mature subtitle-editor functionality, inspect how Aegisub/Toshi-ban, Kainote, or another stated reference actually behaves.
    Do not reduce a requested workflow to the smallest control that technically satisfies its name.

    Examples:
    - "font picker" means fonts are actually discoverable/browsable, not just a TextBox that accepts a font name;
    - "color picker" means a practical authoring color workflow, not merely RGB sliders;
    - "Styles Manager" means useful style-management operations, not merely CRUD buttons;
    - "waveform" must be useful for subtitle timing, not merely render some amplitude data.

15. **Preserve desktop-editor density and responsiveness.**
    Yoake is not a mobile/web application.
    Avoid oversized controls, unnecessary empty panels, excessive padding, and layouts whose size changes unpredictably with language/font fallback.
    Test real workflows at ordinary laptop sizes and Windows DPI scaling.

16. **Live authoring feedback matters.**
    Changes affecting rendered subtitles should update Mangetsu preview promptly where practical.
    Do not force unnecessary explicit commit steps merely because the internal model uses drafts.
    Undo boundaries and persistent commits may remain transactional while preview/editing stays live.

## Assets and third-party reference material

17. **Functional icons remain real SVG source assets.**
    Keep application identity artwork separate from functional icons.
    Functional artwork should remain theme-aware and recolorable.

18. **Compatible upstream assets may be reused when provenance is recorded.**
    Do not avoid useful Aegisub/Toshi-ban functional assets merely to redraw worse substitutes.
    When an asset is copied or adapted, record:
    - source repository;
    - exact path;
    - source commit;
    - applicable license;
    - modifications;
    - required attribution.

    Extend Yoake's SVG conversion pipeline deliberately when needed rather than silently losing transforms, strokes, fill rules, or semantic layers.

19. **Do not silently change native dependencies.**
    `ci/build_dependencies.ps1` and `third_party/*` are authoritative for acquisition/versioning.
    Keep build/cache trees separate from packaged runtime staging.
    Windows portable native DLLs belong under the established runtime layout.

## CI and validation

20. **Do not disable or weaken a failing test merely to make CI green.**
    Fix the regression.
    If a real platform limitation exists, document it explicitly.

21. **Keep regression tests that expose real bugs.**
    Stronger packaged/UI verification is intentional.
    If a new assertion uncovers an actual layout, timing, state, or interaction bug, fix the application instead of weakening the assertion.

22. **Do not perform large local builds unless explicitly requested.**
    GitHub Actions is the build/test authority for native dependencies, NativeAOT publishing, and packaging.
    Source inspection and lightweight checks are fine.
    Do not rebuild FFmpeg/FFMS2/Mangetsu locally as part of ordinary agent work.

## CI / submission

23. **GitHub Actions is the build and test authority.**
    Do not compile, publish, package, or rebuild native dependencies locally unless explicitly requested.

    Push changes and use the repository's GitHub Actions workflows for:
    - managed build/tests;
    - NativeAOT validation;
    - packaged startup/UI verification;
    - native/provider verification;
    - source-policy checks;
    - generated-asset consistency checks.

24. **Do not claim a change is complete until the relevant CI jobs pass.**
    If CI exposes a real regression, fix the implementation and rerun CI.
    Do not disable, weaken, or remove checks merely to obtain a green run.

25. **Lightweight source-only tooling is allowed when useful.**
    Source inspection, formatting, generators, static checks, and other operations which do not compile Yoake or rebuild native dependencies are fine.
	
Update architecture documentation when a real subsystem boundary or contract changes.

## Sustainable Agent Work

- Treat failures as diagnostic information, never as personal failure or pressure to demonstrate success.
- An incomplete but honest result is better than a fabricated success or risky workaround.
- After two failed attempts using the same approach, reassess the root cause and consider a different strategy. Do not blindly repeat commands.
- Distinguish code defects from environmental problems, CI outages, resource exhaustion, and missing dependencies.
- When blocked, preserve valid work, document the blocker, and stop unnecessary retries.
- Never weaken tests, silently remove requirements, or modify unrelated code just to obtain a passing result.
- Do not hide uncertainty or claim verification without evidence.
- Prefer small, reversible changes and clear checkpoints during long tasks.
- A task may finish as completed, partially completed, or blocked. All three are legitimate outcomes when reported accurately.
- Maintain a calm, professional working style. Focus on evidence and the next useful action, not urgency or self-criticism.

## Before finishing

Report:

- what was implemented;
- tests added or updated;
- GitHub Actions status;
- remaining manual QA;
- anything intentionally deferred.