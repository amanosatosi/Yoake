# First-editor limits

The implemented editor targets ordinary ASS work. The following limits remain explicit:

- Legacy code-page import is deferred; UTF-8/16/32 are preserved, and invalid UTF-8 without a recognized BOM is refused.
- Position edits support numeric pos/move and origins, including frame-timed endpoints and visible multi-selection. Relative/expression positions and animated position transforms remain preserved text edits. Scalar visual tools edit the initial static run while retaining later runs and transform payloads.
- Clip tools support rectangles and vector topology, including cubic curves, splitting, conversion, point/box selection and freehand/smoothing. Mangetsu clip offsets/scales/animation are mapped separately from integer drawing scale. Unsupported drawing syntax remains intact and text-editable. Distort source bounds are a raster estimate of shaped outlines; complex mixed-run/animated layout needs manual matching QA (see architecture/visual-typesetting.md).
- Splitting is grapheme/override aware and repeats leading override blocks, but does not retime karaoke syllables or animation transforms.
- Styles use grouped semantic controls, a searchable exact-name font picker, RGB/alpha color picker, 3×3 alignment and a persistent local Style Library. Unknown/future fields remain editable under Advanced. Library edits have independent histories; library collection creation/deletion itself is not undoable.
- ASS highlighting caches one event and rescans it after text changes. Formatting analyzes common static overrides and inherited styles; transforms and unknown tags are preserved rather than interpreted. Reset inserts ASS reset semantics without stripping arbitrary override syntax. Interactive OS IME and color/font picker acceptance remains a manual release check.
- The functional spectrogram is a bounded basic linear-frequency FFT view. Log/mel scales, channel mixing options, adaptive analysis resolution and disk caches are deferred.
- Media index caching across application launches, recovery/autosave, playback speed processing and separate external-audio attachment to an existing video session are deferred. Opening an audio file replaces that document's current media session; other tabs are unaffected.
- Track updates reuse the Mangetsu renderer but replace its ASS track, because the integration currently exposes whole-track load/update. No event-level renderer patch optimization is claimed.
- Manual hardware/UI validation is outstanding until the release smoke checklist has been performed on a normal laptop. CI's small native fixtures cannot establish long-film performance or real device synchronization.

No empty tools are advertised for `.asa`, automation, tracking, gradients, phone-chat or other experimental systems. The source, command, transaction, analysis and provider boundaries remain available for those additions.
