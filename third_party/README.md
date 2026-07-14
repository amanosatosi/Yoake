# Yoake native dependencies

`versions.json` is the single provenance record for Yoake's native media and
subtitle-rendering stack. CI resolves no dependency from a moving `main` or
`master` ref.

The Windows chain is deliberately:

```text
Yoake -> FFMS2 5.0 DLL -> FFmpeg 7.1.1 DLLs
Yoake -> Mangetsu DLL -> statically linked FreeType/FriBidi/HarfBuzz helpers
Yoake -> Qt 6.8.3 (UI and platform PCM output only)
```

FFmpeg is installed with the pinned vcpkg baseline and only the `avcodec`,
`avformat`, `avutil`, `swresample`, and `swscale` library features needed by
FFMS2. FFMS2 is compiled from its pinned source commit as a DLL against that
dynamic FFmpeg prefix. Mangetsu is compiled from the pinned fork commit as a
separate DLL. Its font stack is pinned and linked statically so the portable
directory has one unambiguous renderer runtime.

Qt Multimedia is not a decode backend. Yoake links it only for `QAudioSink`.
Packaging removes Qt's FFmpeg media plugin and rejects every FFmpeg ABI DLL
other than the pinned Yoake stack.

CI copies the actual upstream license files into `THIRD-PARTY-LICENSES` in the
portable ZIP. The build scripts do not synthesize or paraphrase license text.
