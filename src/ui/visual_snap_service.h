#pragma once

#include <QtCore/QPointF>
#include <QtCore/QVariantList>

namespace yoake::ui { class VideoViewport; }

namespace yoake::ui {

// Screen-pixel tolerance is intentional: snapping feels the same at every
// content zoom. Targets are returned as script-space coordinates plus abstract
// guide descriptors so tools can share snapping without owning overlay paint.
class VisualSnapService final {
public:
    struct Result {
        QPointF point;
        QVariantList guides;
    };

    explicit VisualSnapService(const VideoViewport *viewport = nullptr, qreal tolerance = 8.0);
    void setViewport(const VideoViewport *viewport) { m_viewport = viewport; }
    [[nodiscard]] Result snap(const QPointF &scriptPoint, bool bypass = false) const;

private:
    const VideoViewport *m_viewport = nullptr;
    qreal m_tolerance = 8.0;
};

} // namespace yoake::ui
