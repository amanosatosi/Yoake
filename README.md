# Yoake

Yoake is a Qt Quick subtitle editor being built from the workflows and edge-case
knowledge of Aegisub Toshi-ban. It is a new application rather than a wxWidgets
UI port.

The current foundation provides:

- one independently owned document context per tab;
- ASS loading, loss-conscious serialization, stable-ID row selection, and per-tab undo;
- a virtualized editing grid with row clipboard, insert/duplicate/delete/join/split/move,
  bulk timing, comment, and per-document Find/Replace commands;
- an active-line editor for text, timing, margins, style, actor, effect, layer, and comments,
  with ASS/Mangetsu-aware syntax coloring;
- named, semantic JSON themes;
- per-document FFMS2 indexed video/audio sessions with asynchronous cached
  waveform and spectrum views;
- per-document Original K-Timing sessions with draggable boundaries and audition;
- a per-document Mangetsu renderer session with stale-render rejection; and
- Windows-first build and test validation in GitHub Actions.

See [the architecture notes](docs/architecture.md) and
[the migration inventory](docs/toshi-ban-migration.md) for scope and staging.

## Build

Yoake requires Qt 6.8 or newer with Qt Quick, Qt Quick Controls, Qt Multimedia
for PCM output only, and Qt Concurrent. Normal validation is performed in GitHub Actions because the
development machine is intentionally not used for full builds.

```powershell
cmake -S . -B build -G Ninja -DCMAKE_BUILD_TYPE=Release
cmake --build build
ctest --test-dir build --output-on-failure
```

Mangetsu is loaded dynamically as `mangetsu.dll` on Windows,
`libmangetsu.so` on Linux, or `libmangetsu.dylib` on macOS. Yoake does not
silently substitute another ASS renderer when Mangetsu is unavailable.

Toshiki K-Timing remains a distinct next-stage timing policy; the implemented
Original mode is not used as a disguised substitute for it.
