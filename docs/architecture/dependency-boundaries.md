# Dependency boundaries

- Core must not reference Avalonia or native multimedia libraries.
- UI may reference Core/Native contracts but never FFmpeg/FFMS2/libass structs or raw handles.
- App is the explicit composition root; no reflection-based DI container.
- FFMS2 remains the intended frame-accurate editing provider unless measurements reveal a concrete reason to change it.
- FFmpeg is complementary: inspection, audio/special decode, conversion, future hardware paths. It is not a second independent timeline authority.
- SoundTouch belongs to audio processing, not decode or output.
- Mangetsu/libassmod belongs behind subtitle-rendering APIs.
- Automation should target Aegisub 3.2.2 behavior first and remain separable from UI objects so a later out-of-process host is possible.
