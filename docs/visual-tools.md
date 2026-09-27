# Visual typesetting workspace

## Coordinate spaces

`VideoViewport` is the shared mapping for the fitted source frame, the ASS
script rectangle, and Qt Quick logical pixels. A mouse point is local to the
video viewport in logical UI pixels. Qt Quick applies display DPI when it
composites the scene, so visual tools must not multiply input coordinates or
handle sizes by the device pixel ratio.

The fit scale is derived only from viewport size and source-video size. User
content zoom and pan are separate state layered over that fit. Script/video
mapping also accounts for differing script and source resolutions. The same
map drives video presentation, subtitle-preview placement, tool handles,
hit-testing, snapping, and edit deltas.

Resizing preserves the source point under the old viewport center. Pan and zoom
do not touch the ASS event. Middle drag, Ctrl-wheel, Shift-wheel, horizontal
wheel, and pinch gestures update only viewport state. Reset View restores
content zoom 1 and zero pan.

## Tool lifecycle and edits

The document owns one `VisualToolManager` and one viewport state. Tool
descriptors carry a stable id, icon, name, tooltip, command id, and factory id;
the horizontal toolbar is generated from these descriptors. The manager routes
pointer and keyboard input, publishes screen-space overlay features, and asks
the ASS editing helper for tag-preserving changes. Overlay features use fixed
logical-pixel handle sizes while their subtitle positions stay in script space.

A visual drag captures the active event text, previews each pointer update
through the subtitle model and Mangetsu revision signal, then commits one
`QUndoCommand` on pointer release. Escape restores the captured text without a
history entry. Geometry Shift captures selected events by stable ID, previews
the same script-space delta on each selected line, and commits or cancels the
whole group together. Other tools edit only the active line. A tool switch
commits an active drag. Navigation is handled by the viewport before visual
tools receive normal edit gestures.

`ass::VisualTags` scans override blocks and tag boundaries, including balanced
parenthesized arguments. It replaces only the selected tag span and retains
unknown and unrelated override tags. It reads the line's active position,
movement, rotation, scale, and clip tags; absent position uses parsed style
alignment, script resolution, and event/style margins. The source style and
unknown ASS records stay untouched by the read-only geometry metadata pass.

## Vector paths

Vector clips and active `\p` drawings share the same path-node editor. Drawing
paths are located from override-mode boundaries and are kept between their
existing `\pN`/`\p0` switches. New drawings are wrapped in a local `\p1`/`\p0`
pair at the line end. Drawing-mode coordinates are positioned relative to the
line's effective script-space anchor.

Vector clips retain their drawing scale and inverse state. Vector clips and
active `\\p` drawings use the shared `ass::VectorPath` model for move, line, and
cubic commands, endpoints, and Bézier controls. Editing serializes that model
while `VisualTags` preserves surrounding override tags and subtitle text. Ctrl
toggles nodes, Shift adds nodes, and selected nodes drag together. Deleting a
line endpoint removes its segment; deleting a Bézier control converts the
segment to a line, while deleting its endpoint removes the curve. Insertion
splits line or cubic segments in place, and selected segments can convert
between line and cubic Bézier. Control handles have a distinct overlay style.
Freehand samples in screen space; the smoothed mode uses screen-space
Douglas–Peucker simplification so tolerance does not change with content zoom.
Spline commands and malformed or unknown paths remain read-only and retain
their source syntax.

Registered custom tools implement the `VisualTool` event and overlay hooks.
The built-in tools still use `VisualToolManager` handlers; migrating their
semantics behind individual `VisualTool` implementations remains unfinished.
`\pgrd` appears in preservation tests and is recognized
by the syntax highlighter; the migration inventory still lists its gradient
editor as future work, and this checkout has no active parameter specification.
Current `\distort`, `\perspective`, `\ct`, warp, and gradient semantics are not
described by an active Yoake parser/specification, so those tags remain
preserved as text without invented visual controls.

## Current bounds and renderer notes

Scale handles use Qt font metrics and style scale to estimate one line's
unrotated text bounds. Per-glyph Mangetsu layout bounds are not exposed by the
current renderer boundary, so this box is a guide rather than an exact renderer
measurement. The visual editor intentionally remains independent of Mangetsu's
rendering internals; a future renderer-neutral layout metrics API can replace
the estimate without changing tool geometry or tag edits.

The present application has one attached video pane per document. The
transform is per document and can be shared with another viewport, but the UI
does not yet expose detached-video windows or per-window pan/zoom state.

Effective placement reads direct positioning and alignment tags; it does not
evaluate style resets or transformed tag state. Wheel behavior is not yet
preference-configurable. The C++ custom-tool factory is an extension point,
not a loaded plugin system.
