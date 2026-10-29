#include <cmath>
#include <vector>
#include <cstdio>

extern "C" {
struct YoakeMotionMarker { double x, y, size, search_size, rotation; };
struct YoakeMotionResult { YoakeMotionMarker marker; double confidence; int state; };
__declspec(dllimport) int yoake_motion_track(const unsigned char*, int, const unsigned char*, int, int, int,
    const YoakeMotionMarker*, const YoakeMotionMarker*, int, int, double, int, int, YoakeMotionResult*) noexcept;
}

int main() {
    constexpr int width = 128, height = 96, pitch = width * 4;
    auto frame = [=](int dx, int dy) {
        std::vector<unsigned char> result(pitch * height, 15);
        for (int y = 0; y < height; ++y) for (int x = 0; x < width; ++x) {
            int u = x - dx - 40, v = y - dy - 40;
            auto value = static_cast<unsigned char>(std::abs(u) <= 20 && std::abs(v) <= 20
                ? 120 + 45 * std::sin(u * .51) + 40 * std::cos(v * .43) : 15);
            for (int c = 0; c < 3; ++c) result[y * pitch + x * 4 + c] = value;
            result[y * pitch + x * 4 + 3] = 255;
        }
        return result;
    };
    auto first = frame(0, 0), second = frame(4, 3);
    YoakeMotionMarker marker{40, 40, 32, 80, 0}; YoakeMotionResult result{};
    if (!yoake_motion_track(first.data(), pitch, second.data(), pitch, width, height, &marker, &marker,
        1, 0, .75, 1, 1, &result) || result.state == 4 || std::abs(result.marker.x - 44) > 1 || std::abs(result.marker.y - 43) > 1) {
        std::fprintf(stderr, "OpenCV tracking failed: %.2f %.2f, confidence %.2f, state %d\n", result.marker.x, result.marker.y, result.confidence, result.state);
        return 1;
    }
    std::vector<unsigned char> blank(pitch * height, 0);
    if (!yoake_motion_track(first.data(), pitch, blank.data(), pitch, width, height, &marker, &marker,
        1, 0, .75, 1, 1, &result) || result.state != 4) {
        std::fprintf(stderr, "Missing pattern should be reported as lost\n");
        return 1;
    }
    return 0;
}
