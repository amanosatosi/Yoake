# Myanmar diagnostics

The independent Windows workflow exercises four upstream font binaries, one per process. The font manifest records SHA-256, face names, OpenType scripts and versions. The independent HarfBuzz reference uses 32 DIP normalized coordinates with Unicode codepoint clusters; Avalonia reports its own cluster indices and DIP offsets. All current samples are BMP, but run-relative cluster origins and itemization must still be accounted for. Differences in HarfBuzz versions, language and shaping features are diagnostic, not automatically regressions.

UI probes use the production fallback configuration, UI font resource setter, ASS editor and grid controls, plus stock TextBlock and TextBox, in a minimal real desktop window. They cover default, named selection, reset, re-selection, partial selection and text editing. This is not the full application or its NativeAOT build. The direct Avalonia shaper uses the loaded face; the named UI path may resolve a different face, which is reported.

Mangetsu uses the production C# wrapper, the exact font embedded in an ASS track, and full native logs plus raster output. It does not expose internal glyph IDs through the public render API; use raster comparisons and native logs, not claims of glyph-level parity. Different alignment/rasterization and font sizing mean screenshots are qualitative. A zero-ink image is reported as a diagnostic rather than a passed shaping test.

To run locally against your own font, build the project and run:
`dotnet run --project tools/FontDiagnostics -c Release -- path/to/font.ttf samples.txt output ui`
For the Mangetsu mode place the matching native DLL beside the executable. Only reports and rendered previews are uploaded by CI, not raw font binaries or embedded-font ASS files.
