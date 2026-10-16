# Position motion tracking

Video → Track motion opens a modal authoring session for the active dialogue.
Navigate within the line's video-frame range, set a reference, drag a rectangular
pattern around a textured object and adjust the separate orange search rectangle
with its width/height controls. A click corrects the pattern center. Track forward
and backward from the reference; scrubbing shows the measured path and confidence.
On failure, correct that frame and restart in either direction. Restart replaces
that direction's samples while retaining the opposite section. Reset discards all
samples and uses the current frame as the new reference. Apply requires a complete
successful range and returns to the main Mangetsu preview.

`MotionPatternTracker` is a dependency-free managed normalized cross-correlation
matcher over real BGRA pixels. A bounded sampled reference patch, coarse search,
candidate refinement and subpixel peak estimation keep the initial position-only
workflow small and NativeAOT compatible. Flat patterns are rejected; confidence
below 0.65 stops tracking instead of inventing/interpolating missing observations.
Large appearance changes, occlusion, rotation or zoom require correction/reseeding.

`MotionTrackingSession` holds a reference-counted video-only lease on the existing
`DocumentMedia`, uses its `IVideoProvider.GetFrame` and main-video frame map, and
retains only measured points, the sampled template and one working frame buffer.
There is no extra decoder, persistent frame cache or playback clock. Decode/match
work runs on a worker, with cancellation checks throughout search. Fast (default)
throttles preview publication; Viewable publishes each measured frame and yields
40 ms. Neither mode changes the matching inputs, search or results. Stop cancels
only tracking work. Closing cancels/awaits work and releases the lease; it does not
dispose the document's video or audio. The main timeline pauses when opening the
dialog; frame inspection inside the dialog does not seek that timeline.

Stable `video/motion-track` and `motion-track/*` IDs own all semantic actions.
Pointer handlers hold a local rectangle until release; capture loss/Escape discard
the uncommitted gesture. Numeric edits, navigation and buttons invoke the same
registry. The window retains native decorations. The session owns no document
mutation: Apply checks document/provider/line identity and the document revision,
then uses the existing `SubtitleEditor` and `UndoManager` for one operation.

`MotionTrackApply` preserves original ASS bytes and all dialogue fields, inserting
one relative position layer after the leading authored overrides. Displacements
are relative to the chosen reference and scaled from video pixels to PlayRes.
Positive screen Y becomes negative relative Mangetsu Y. Actual video timestamps
become event-relative transform times. Joint X/Y fitting has a 0.35 script-pixel
error bound and retains the reference anchor. Nearly static motion adds nothing;
linear movement collapses to long intervals. No rotation tags are generated.
This uses Mangetsu relative `\pos` inside `\t`; standard libass is not a fallback.
Competing authored position animation or inline position changes are explicitly
rejected. Reapply/revert ownership management, scale, rotation, clips, advanced
cleanup and After Effects export are outside this milestone; undo removes an
applied layer exactly before retracking.

Behavioral reference: [Aegisub Toshi-ban](https://github.com/amanosatosi/Aegisub_Toshi-ban/tree/b5d188f4d570468857a7731488df3c5a9a9c8037),
branch `codex/39-mode`, inspected paths `src/dialog_motion_track.cpp`,
`src/motion_tracking/motion_track_engine.cpp`, `motion_track_apply.cpp`,
`motion_track_types.h` and `docs/native-motion-apply.md`. Yoake's managed matcher,
Avalonia session and structured ASS application are original implementations of
the practical workflow; no upstream code/assets or OpenCV dependency are copied.

Focused core tests cover synthetic moving decoded frames in both directions,
confidence failures, cancellation, relative signs/reference, compact fitting,
unknown tags/fields and exact undo/redo. Real-device object tracking, real IME,
integrated-GPU responsiveness and Windows DPI remain manual QA.
