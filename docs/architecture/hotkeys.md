# Hotkey contexts

Hotkey resolution is contextual, not widget-global. Contexts already enumerate Always, Default, Video, Audio, Subtitle Grid, Subtitle Edit, Visual Tools, K-Timing, 39 Mode, Translation, and Styling.

The resolver rejects duplicate gestures inside one context. Active contexts are ordered; the most specific active context wins, with Always as fallback. Future persistence/import should map gestures to stable command IDs rather than callbacks.

Future rebinding, multiple gestures per command, conflict UI, import/export, and Aegisub hotkey import must all translate persisted gestures into these command/context identities rather than bypassing the resolver.
