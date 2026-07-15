# Yoake foundation architecture

Yoake is a Qt Quick subtitle editor with one independent `DocumentContext` per
tab. Qt owns the application and presentation layers. FFMS2 and its pinned
FFmpeg chain own indexed media access. Mangetsu is the only subtitle preview
renderer.

## Ownership

```text
Application
├─ DocumentManager                         application-wide tab registry
│  ├─ DocumentContext                      one per real tab
│  │  ├─ ASS Document + SubtitleModel
│  │  ├─ selection + QUndoStack + state IDs
│  │  ├─ MediaSession
│  │  │  ├─ FfmsVideoWorker                one controlled video/index lane
│  │  │  ├─ FfmsAudioWorker                independent playback and waveform lanes
│  │  │  ├─ FfmsSpectrumWorker             on-demand FFT tile/cache lane
│  │  │  ├─ FrameTimeMap                   indexed VFR source-frame timeline
│  │  │  ├─ WaveformModel                  aggregated numeric cache levels
│  │  │  └─ QAudioSink                     PCM output only; never decoding
│  │  └─ MangetsuSession                   independent renderer + serial lane
│  └─ DocumentContext …
└─ ThemeManager                            named immutable semantic palette
```

There is no global document storage. Inactive tabs retain their own document,
selection, undo history, indexed sources, waveform, playback, and renderer.
Closing a tab stops and joins only its owned worker lanes.

## Indexed media boundary

`MediaSession` is the GUI-thread coordinator. It does not call FFMS2 synchronously.
The video worker owns `FFMS_Index`, `FFMS_VideoSource`, conversion state, and
frame requests. Persistent playback, waveform, and spectrum lanes each own a
separate `FFMS_Index`/`FFMS_AudioSource` pair. Long waveform generation or FFT
tile work therefore cannot starve short-range playback or interactive video
seeking.

Opening a source performs these operations off the GUI thread:

1. discover video and audio tracks through the FFMS2 indexer;
2. load the application cache index when present;
3. validate it with `FFMS_IndexBelongsToFile`;
4. discard and rebuild a mismatched index;
5. index every video and audio track and atomically publish the completed index;
6. create the selected video source and build a timestamp entry for every real
   source frame from `FFMS_FrameInfo::PTS` and `FFMS_TrackTimeBase`; and
7. configure FFMS2 conversion to BGRA for the Qt Quick presentation boundary.

The cache key is based on the canonical source path. Source validity remains an
FFMS2 decision, not a filename/mtime guess. A failed or cancelled indexing pass
never replaces a valid completed index.

`FrameTimeMap` is the only frame/time authority. It supports:

- timestamp to source-frame lookup by binary search over indexed timecodes;
- exact start/end timestamps for frame N;
- previous/next stepping by source-frame identity; and
- VFR detection as metadata, without changing the mapping algorithm.

Source FPS is exposed only as descriptive metadata. No code derives frame
identity from `milliseconds * fps`, and there is no default FPS.

## Request generations and responsiveness

Each source open/close/track change advances the media generation. Explicit
seeks advance a cancellation epoch; playback-clock requests advance a sequence
inside that epoch. This permits one sequential decode already in flight to
finish while coalescing its pending successor, without allowing a pre-seek
frame to cross the cancellation boundary. Spectrum viewport requests use the
same atomic stale-result pattern. Expensive decoding cannot be interrupted
inside FFmpeg, but old-source/old-seek/old-viewport results are rejected.

The relevant identity is:

```text
{ DocumentContext, mediaGeneration, frameRequestId, sourceFrame,
  exactFrameStart, subtitleRevision, mangetsuClientId, mangetsuRequestId }
```

Workers are persistent and owned; Yoake does not create a thread per seek. A
rapid 100 → 500 seek can finish decoding 100, but frame 100 cannot overwrite
the accepted request for 500.

## Qt Quick presentation and Mangetsu

`VideoFrameItem` paints only the latest accepted FFMS2 `QImage`. It keeps the
decode/presentation seam explicit, so a future scene-graph texture upload can
replace the current single CPU copy without changing source ownership or QML.

`SubtitleOverlayItem` is separate from video ownership. It asks its tab's
`MangetsuSession` to render at `MediaSession.displayedFrameStartMs`: the exact
timestamp represented by the currently displayed source frame. Subtitle edits,
geometry changes, document changes, and frame changes advance its request ID.
An old document/frame/subtitle result cannot replace current overlay state.

Mangetsu is loaded only as `mangetsu.dll` beside Yoake (or from an explicit
absolute `YOAKE_MANGETSU_LIBRARY`). Missing symbols are fatal to preview. Yoake
does not fall back to libass, VSFilter, or another renderer.

## Indexed audio, waveform, and spectrum

FFMS2 decodes the selected audio track at its real indexed sample rate and
normalizes only the channel/sample representation to stereo signed 16-bit PCM.
FFMS2 does not support arbitrary output-rate conversion, so Yoake never forces
a track to 48 kHz. `QAudioSink` sends that PCM to the operating system; it does
not open, decode, seek, or index media.

Sample/time conversion includes the FFMS2 audio timeline origin. Playback is a
range operation even for ordinary continuous play. Repeating a karaoke split
stops the current sink, maps the same start/end to source samples, and requests
the range again. This is the foundation for repeated Toshiki auditioning.

Waveform work runs incrementally on the audio lane. The worker emits min/max
peaks rather than samples or QML items. `WaveformModel` builds aggregate cache
levels and `samplesForRange(start, end, pixels)` returns only useful visible
data. A timing-line zoom therefore consumes higher-resolution cached peaks
without rendering one item per audio sample.

Spectrum mode uses Hann-windowed FFT analysis on its own worker. A zoom-selected
hop/FFT level produces 128 perceptually curved frequency bands per cached tile;
time is horizontal, frequency is vertical, and theme-mapped energy is color.
The worker retains a bounded 96 MB LRU of numeric energy tiles and renders no
more than 1024 viewport columns. Small scrolls reuse tiles, high timing zoom uses
the detailed level, coarse overviews sample sparsely, and QML receives only a
completed viewport image. Timing selections, playback cursors, subtitle ranges,
and karaoke boundaries remain a shared overlay above either visualization.

## Native dependency and portable-build policy

`third_party/versions.json` records immutable dependency commits. Windows CI:

- builds dynamic FFmpeg 7.1.1 libraries from the pinned vcpkg port;
- builds FFMS2 5.0 as `ffms2.dll` against that exact dynamic prefix;
- builds the pinned Mangetsu fork as `mangetsu.dll`, with pinned static
  FreeType/FriBidi/HarfBuzz helpers;
- compiles Yoake with MSVC and Qt 6.8.3;
- runs unit tests, `windeployqt`, ABI/export checks, and a packaged startup
  smoke test; and
- uploads `Yoake-Windows-x64-portable.zip` with actual upstream licenses.

Qt Multimedia remains linked only for `QAudioSink`. Packaging removes its
FFmpeg decoder plugin and rejects any FFmpeg ABI DLL outside Yoake's expected
`avcodec-61`, `avformat-61`, `avutil-59`, `swresample-5`, and `swscale-8`
runtime set.

## Document and ASS boundaries

Every implemented subtitle mutation enters through `DocumentContext` and a
`QUndoCommand`. Separate saved/current state IDs keep asynchronous save, undo,
and branching edits loss-safe. QML receives UUID-scoped model rows, never raw
worker/provider pointers.

The current `ass::Document` is a conservative compatibility adapter. It edits
canonical UTF-8 ASS v4+ events while preserving unknown sections, styles,
attachments, extradata, project metadata, and Mangetsu syntax. It rejects
unsupported SSA/reordered formats instead of corrupting them. Extracting the
full mature Aegisub ASS core remains a later migration behind the same boundary.

## Current foundation status

Implemented and CI-validated:

- independent Qt Quick document tabs, themes, models, undo, and loss-safe I/O;
- FFMS2 track discovery, reusable validated indexes, exact indexed source-frame
  access, VFR time mapping, rapid-seek stale-result rejection, and frame stepping;
- FFMS2 indexed audio, random range access, native-rate Qt PCM output, scalable
  asynchronous waveform levels, and cached spectrogram tiles;
- Original K-Timing range audition and commit flow;
- Mangetsu-only asynchronous overlay rendering at accepted source-frame time;
  and
- a pinned, dependency-checked, license-bearing portable Windows ZIP pipeline.

Still later work: the full Aegisub ASS/style/attachment core, broader editing
commands, embedded-font/tag-image Mangetsu support, Toshiki K-Timing policy,
autosave/recovery, preferences, and shortcut editing. OCR and Automation/Lua
remain deliberately excluded.
