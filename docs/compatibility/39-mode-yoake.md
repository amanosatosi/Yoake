# 39 Mode continuation

The reference is the user-provided `C:\aegisub source\Aegisub_Toshi-ban-release` archive. It has no assumed Git identity. Its `docs/39-mode.md`, `src/audio_timing_39.cpp`, `src/timing39_session_setup.cpp`, and `libaegisub` timing39 model describe the separate mode. The user has now explicitly requested it alongside, not instead of, Toshiki K-Timing.

## Initial capture foundation

`Timing39Capture` is a session-local model of the reference's two lanes. F/J and D/K have independent ownership; repeated keydowns are ignored, takeover closes the previous block, releasing the old key does not release its replacement, gaps are retained, and stop closes held notes. Callers supply absolute media timestamps. Countdown, ready, capture, results and discard transitions do not touch a document or create a playback clock. Backward timestamps are refused. Core regressions cover ownership, simultaneous lanes, absolute timestamps, countdown cancellation and stop.

The initial partitioner produces line-local copies with raw indices and strict majority ownership, flags clipped sung blocks for review and leaves the full recording unchanged. Resolving competing overlapping targets remains future work.

This is only a foundation; it is not a usable 39 Mode yet. The remaining work includes document-owned target snapshots and session lifetime; central playback/countdown and real keyboard routing; overlap review; Japanese/ruby/romaji reading analysis, candidate matching and confidence; review, corrections and retakes; structured, atomic karaoke serialization and undo; a usable results UI; and packaged integration verification. No 39 Mode button or completed-parity claim should be added until a usable capture-to-review-to-commit workflow exists. Preserve Toshiki mode, unknown ASS syntax, tab ownership, IME, cancellation and the single timeline throughout.
