# Visual typesetting

`VisualOverlayControl` owns Avalonia pointer capture, focus, modifier updates,
letterbox/video/script mapping, and invalidation. `IVisualTool` implementations
own rendering, hit testing, hover affordances, selected points and interpretation
of a captured gesture. The registry chooses tools and vector subtools with stable
`video/tool/*` and `video/vector/*` IDs. The primary selector is a compact vertical rail beside video. Contextual options
collapse beneath navigation/playback; help is available on the canvas/options
tooltips and the existing command-bar status area without a permanent helper row. The overlay paints a transparent input
surface across the video, including the no-clip state. Packaged checks assert
actual input hit targets before raising pointer events.

`VisualToolContext` exposes selected visible dialogue lines and coordinate maps.
Comments and lines outside the current frame are excluded. `VisualLine` contains
resolved defaults, current move position, origin, transform and frame-relative time.
Position standby uses style/inline alignment and asymmetric margins. Start/end
features retain their identity separately from the current-frame position marker.
Endpoint edits associate timing with the current frame. pos-to-move initializes
one current-frame interval; move-to-pos retains the original start endpoint.
Quick positioning shifts both endpoints and explicit origins by a shared delta.

`video/visual/update` computes a complete batch from immutable gesture baselines
before mutating any line, inside the editor's rollback-by-default transaction.
Each update replaces current transaction text through SubtitleEditor; draft
preview precedence stays with gesture text until commit/cancel. No renderer is
created per pointer update. Capture loss, Esc, seek, selection/tool/document
changes and failures cancel the transaction. One selected batch is one undo item.

Core tag edits use balanced structured source ranges. Unknown syntax and
transform payloads survive. Scalar tools edit the initial run's static property;
later runs remain independent. Rotation applies a common delta across selection;
scale applies a common ratio to retain differing line scales. Ctrl snaps rotation
to 30 degrees and scaling to 25 percent; Shift constrains; Alt preserves aspect.
Position Alt-drag explicitly creates origin. Switching tools alone never does.

Clip geometry uses an integer drawing scale plus a separate script-space mapping
for `clippos` and `clips`. Static/relative offsets, animation and resets resolve
for overlay display. Translating clips with these state tags appends an explicit
relative clippos after the existing animated state; it does not bake vector points or rewrite animation payloads.
Scaled rectangle resizing solves both raw corners from their desired displayed
positions. Vector topology edits compensate for the changed painted-bounds scale
center with a relative clip offset, keeping untouched points fixed at the current frame.
Rectangular misses never convert vectors. Vector topology is m/n/l/b, with ASS
s/p/c splines evaluated as cubics for editing. Point moves, line/cubic conversion,
De Casteljau splitting, removal and sampled/freehand smoothing are deterministic.
Unknown drawings remain text-editable and are never guessed into a new mask.

Distort uses P0/P1/P2/P3 in memory and serializes Mangetsu's P1/P2/P3/P0 eight-slot
order. Legacy six-slot text remains intact until an explicit edit. Source bounds
are measured in a detached neutral-geometry Mangetsu track on a background job,
using a provider-owned reusable renderer. Header/styles are captured once per
request and each selected event gets an immutable source snapshot; parsing,
neutralizing paint/geometry and shaping run off the UI thread. A bounded provider
cache reuses identical measurements across gestures. Measurements are cached during drags;
normalized corner pins project through the line transform. Projection uses
Mangetsu's Z/X/Y order and signed axes, with distance 20000/64 layout pixels;
explicit LayoutRes or the provider's decoded storage size defines that domain. This is a raster
estimate of shaped outline bounds (up to a pixel), not native outline metadata.
Multiple differently styled distortion units, animated geometry and unusual
Mangetsu layout/anchor extensions need manual sign-matching QA; no exact native
outline-metadata API is currently exported. Mangetsu remains authoritative for
actual bilinear warping. No perspective tag is emitted.

Audio horizontal zoom is logarithmic over 0.02–3600 seconds per viewport and
preserves the pointer/current visible cursor or viewport center. Amplitude uses
a cubic control mapping (0.008–8), independently of playback volume. Linked
amplitude maps control position into the output provider's supported 0–1
attenuation range; it does not invent amplification support. The link, zoom,
amplitude, volume/mute and independent sash height are persisted. Ordinary wheel
pans, horizontal wheel pans naturally, Ctrl+wheel zooms at the pointer, and
Shift+wheel changes amplitude. Wheel gestures do not compete with marker drags.

Color hue and alpha use a dedicated keyboard/capture input control with a thin
black/white marker over gradients. The screen dropper captures the pointer and
samples physical desktop coordinates into a live 7x7 magnifier. Drag release or
latched click accepts; Escape/capture loss cancels. Neighbor pixels remain
selectable afterward; RGB sampling never changes ASS transparency.

Workspace spacing follows semantic groups: 2–4 DIP within related controls,
10–12 DIP between groups and 6-DIP pane insets. Splitters define pane boundaries;
repeated VIDEO/AUDIO/EDIT headers and bordered cards do not consume workspace.
Metadata groups Comment/Style/Edit Style separately from Actor, Effect and Layer.
Timing/margins and B-I-U-S/font/colors/reset have distinct group gaps. Visual
primary tools occupy the vertical rail; only contextual controls use a bottom
row. Frame status shares playback horizontal space. The default split reserves
at least 600 DIP for video input and audio timing independently. Audio has labelled 32-DIP columns with 10-DIP gaps and a
separator from the visualizer. Grid rows are fixed at 26 DIP, with shaped-text height and containment checks
for Japanese, Burmese, Arabic, Latin, emoji and combining marks. The 20,000-row
fixture verifies recycling and virtualization.
CI configures a temporary 1920x1080 desktop to verify the actual default
1440x900 window, in addition to narrower reflow and render-DPI captures. Default
workspace assertions protect video, waveform, multiline text and grid space.

Audio status shares the playback row. Fresh profiles allocate 180 DIP to the
audio pane, including 120 DIP of waveform at 1440x900, while saved splitter heights
remain respected (160-DIP minimum). The reclaimed status row and 20-DIP default
rebalance preserve video/grid dimensions and at least 140 DIP of ASS text. The
packaged media probe additionally checks the fitted decoded picture, rather than
only its surrounding overlay bounds.

Vector Line/Bicubic hover rendering appends to a copy of the parsed topology
and renders its complete closed contour, replacing the old closing edge. It
uses the same append/save and clip-mapping compensation as the eventual edit.
Cubic handles and the prospective closing segment remain visible. Hover never
mutates document text. Packaged captures assert preview/commit topology equality.
