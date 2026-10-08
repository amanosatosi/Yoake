# Visual typesetting audit

Starting branch head: `6420f301f31d511e264791d5adfc67ee140a6928`.
Local `agents.md` and the user's deletion of `luna.md` are unrelated changes.

Reference inspected: amanosatosi/Aegisub_Toshi-ban
`28b5156df94cb2538d373db503445d2b07466396`, visual_tool base, cross,
drag, rotatez, rotatexy, scale, clip, vector_clip implementations/headers,
video_display, toolbar, audio_box/display, and dialog_colorpicker.
Interaction ideas are adapted to Avalonia; wx/OpenGL implementation is not ported.

Mangetsu reference: amanosatosi/libassmod `mangetsu`,
`db7023f2fd97fbdf2186647d3b47281f647babea`, ass_parse.c,
ass_render.c, docs/distort-tag.md and docs/clip-transforms.md.
Distortion is bilinear in normalized source-outline bounds, P1/P2/P3/P0 order.
Six-slot legacy tags stay untouched until an intentional distortion edit.
Clip translation uses script coordinates; relative Y has author-facing inverted
direction. Clip scale is distinct from the integer vector drawing scale.

Existing strengths: rollback-by-default undo, explicit document lifetimes,
command dispatch, draft preview precedence, FFMS2 frame timestamps, lossless tag
splicing and a recolorable SVG pipeline. The starting overlay has only a single
position feature and numeric clip points, with presentation and edits coupled.
It lacks tool lifetimes, move/origin handles, transforms and vector topology.
Color strips have Fluent Slider thumbs; screen sampling is blind and one-shot.
AudioSize is incorrectly bound to height; zoom buttons recenter at playback
time, and a layout callback overwrites the sash position from saved height.

The new contract separates tool-local rendering/hit testing/selection from the
host's coordinate mapping and capture lifecycle. Command-owned mutations are
derived from immutable gesture baselines and share one undo transaction across
the visible selection. Renderer previews continue to use transaction text.
