# Media / timeline model

There will be one timeline authority exposed to the editor. It owns frame identity, timestamps, VFR mapping, frame bounds/duration, keyframes, and track metadata.

FFMS2 is the planned primary frame-accurate edit/video provider. Direct FFmpeg may perform specialized decode, inspection, conversions, audio work, and future hardware decode, but UI code must not ask both libraries competing questions about frame timing.

Before M2 playback, introduce a `VideoFrame` abstraction with width, height, pixel format, stride, color metadata, frame/timestamp identity, storage kind, lifetime and ownership. The first backend may be CPU-backed, but the API must permit later GPU-backed frames without forcing repeated full-frame copies.

Audio is three responsibilities: decoder -> processor -> output. SoundTouch is a processor. Clock ownership must be explicit before synchronization is implemented.
