# Media and analysis timeline

FFMS2 remains the media/index/frame authority. The provider reads frame PTS and track time base from the indexed video track and retains managed timestamps for frame navigation, including VFR. Neighbor stepping uses those timestamps rather than approximating `1/fps`. Native ABI definitions are checked against FFMS2 5.0's pinned `include/ffms.h`. Duration uses the final frame timestamp plus its interval, combined with audio duration.

Playback uses waveOut's played-sample clock when audio exists and a monotonic Stopwatch only for video without audio. The audio display, seek controls, timing commands, video frames and subtitle preview consume the same media time. Selecting or editing a line does not let playback steal grid selection. Play-current-line stops at the line's end; pause and seek cancel the prior playback generation.

FFMS indexing uses its progress callback with an AOT-compatible unmanaged function pointer and a GCHandle containing the cancellation token. The handle is released after synchronous indexing returns. Background job ownership and cancellation belong to the document state; obsolete jobs cannot publish another document's result.

Waveform analysis sequentially reads the complete audio in chunks and stores 10 ms peak buckets, avoiding sparse samples that miss speech. Reads release the native audio lock between chunks. The control aggregates visible buckets per pixel and draws neighboring timings, selected start/end, and the central playback cursor. Click seeks, boundary drags change start/end, Shift+drag moves the range, wheel scrolls and Ctrl+wheel zooms. Each gesture is one rollback-by-default transaction.

Spectrogram analysis is a separate viewport provider producing bounded BGRA tiles. A small radix-2 FFT and Hann window require no dependency or runtime code generation. It is a basic linear-frequency decibel display, with at most four cached viewport tiles. Analysis runs through background jobs with cancellation; scrolling/zooming cancels obsolete requests. Whole-film spectrum arrays are not allocated. Waveform and spectrum share the timing/interaction overlay rather than acquiring separate clocks.

Video decode and Mangetsu rendering run behind a sequential background request gate, with generation checks before/after decode. The caller may supply an owned reusable pixel buffer; the provider fills it before returning. The UI reuses its bitmap for unchanged dimensions and signals Image invalidation after upload. Subtitle source is serialized once per revision, previews are debounced during gestures, and Mangetsu's renderer/font state survives track updates. The existing native API currently replaces a track on document revisions; event-level native patching is not claimed.

GPU-backed frame storage, reusable decoder-native buffers, advanced spectrum scales, persistent media-index caches and audio processing are later provider work. No parallel FFmpeg timeline is added.
