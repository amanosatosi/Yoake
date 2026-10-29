#include "motion_track_engine.h"
#include <algorithm>
#include <cstring>
#include <exception>

extern "C" {
struct YoakeMotionMarker { double x, y, size, search_size, rotation; };
struct YoakeMotionResult { YoakeMotionMarker marker; double confidence; int state; };

// All data is caller-owned. No C++ exception or native object crosses this ABI.
__declspec(dllexport) int yoake_motion_track(const unsigned char* from, int from_pitch,
    const unsigned char* to, int to_pitch, int width, int height,
    const YoakeMotionMarker* pattern, const YoakeMotionMarker* search,
    int frame, int mode, double threshold, int normalize, int prepass,
    YoakeMotionResult* output) noexcept {
    try {
        if (!from || !to || !pattern || !search || !output || width <= 0 || height <= 0 ||
            width > 32768 || height > 32768 || from_pitch < width * 4 || to_pitch < width * 4 ||
            mode < 0 || mode > 3) return 0;
        auto image = [width, height](const unsigned char* source, int pitch) {
            motion_tracking::MotionTrackImage result;
            result.width = width; result.height = height; result.pitch = width * 4;
            result.bgra.resize(static_cast<size_t>(result.pitch) * height);
            for (int y = 0; y < height; ++y)
                std::memcpy(result.bgra.data() + static_cast<size_t>(y) * result.pitch,
                    source + static_cast<size_t>(y) * pitch, result.pitch);
            return result;
        };
        auto marker = [](const YoakeMotionMarker& m) {
            return motion_tracking::MotionTrackMarker{m.x, m.y, m.size, m.search_size, m.rotation};
        };
        motion_tracking::MotionTrackSettings settings;
        settings.mode = static_cast<motion_tracking::MotionTrackMode>(mode);
        settings.correlation_threshold = threshold; settings.brightness_normalize = normalize != 0;
        settings.prepass = prepass != 0;
        auto result = motion_tracking::MotionTrackEngine{}.TrackFrame(image(from, from_pitch), image(to, to_pitch),
            marker(*pattern), marker(*search), frame, settings);
        output->marker = {result.marker.cx, result.marker.cy, result.marker.size, result.marker.search_size, result.marker.rotation_deg};
        output->confidence = result.frame.confidence; output->state = static_cast<int>(result.frame.state);
        return 1;
    } catch (...) { return 0; }
}
}
