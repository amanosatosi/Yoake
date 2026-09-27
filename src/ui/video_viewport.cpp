#include "ui/video_viewport.h"

#include <QtCore/QtMath>

#include <algorithm>
#include <cmath>

namespace yoake::ui {

namespace {
constexpr qreal minimumZoom = 0.125;
constexpr qreal maximumZoom = 10.0;
}

VideoViewport::VideoViewport(QObject *parent) : QObject(parent) {}

QRectF VideoViewport::displayedVideoRect() const
{
    const QSizeF scaled = m_videoSize * contentScale();
    if (scaled.isEmpty())
        return {};
    const QPointF origin = viewportCenter() + m_pan - QPointF(scaled.width() / 2.0, scaled.height() / 2.0);
    return QRectF(origin, scaled);
}

QRectF VideoViewport::fittedVideoRect() const
{
    const QSizeF fitted = m_videoSize * fitScale();
    if (fitted.isEmpty())
        return {};
    return QRectF(QPointF((m_viewportSize.width() - fitted.width()) / 2.0,
                          (m_viewportSize.height() - fitted.height()) / 2.0), fitted);
}

qreal VideoViewport::fitScale() const
{
    if (m_viewportSize.width() <= 0.0 || m_viewportSize.height() <= 0.0
        || m_videoSize.width() <= 0.0 || m_videoSize.height() <= 0.0)
        return 0.0;
    return std::min(m_viewportSize.width() / m_videoSize.width(),
                    m_viewportSize.height() / m_videoSize.height());
}

void VideoViewport::setViewportSize(const QSizeF &size)
{
    const QSizeF normalized(std::max(0.0, size.width()), std::max(0.0, size.height()));
    if (m_viewportSize == normalized)
        return;

    // Keep the source point formerly under the viewport center at the new
    // viewport center. Pan is stored in logical pixels and zoom is untouched.
    QPointF centerVideo;
    const bool preserveCenter = fitScale() > 0.0;
    if (preserveCenter)
        centerVideo = screenToVideo(viewportCenter());
    m_viewportSize = normalized;
    if (preserveCenter && fitScale() > 0.0) {
        const QPointF center = viewportCenter();
        const qreal scale = contentScale();
        m_pan = center - center - QPointF((centerVideo.x() - m_videoSize.width() / 2.0) * scale,
                                         (centerVideo.y() - m_videoSize.height() / 2.0) * scale);
    }
    clampPan();
    announceTransform();
}

void VideoViewport::setVideoSize(const QSizeF &size)
{
    const QSizeF normalized(std::max(0.0, size.width()), std::max(0.0, size.height()));
    if (m_videoSize == normalized)
        return;
    m_videoSize = normalized;
    clampPan();
    announceTransform();
}

void VideoViewport::setScriptSize(const QSizeF &size)
{
    const QSizeF normalized(size.width() > 0.0 ? size.width() : 1920.0,
                            size.height() > 0.0 ? size.height() : 1080.0);
    if (m_scriptSize == normalized)
        return;
    m_scriptSize = normalized;
    announceTransform();
}

QPointF VideoViewport::videoToScreen(const QPointF &point) const
{
    return displayedVideoRect().topLeft() + point * contentScale();
}

QPointF VideoViewport::screenToVideo(const QPointF &point) const
{
    const qreal scale = contentScale();
    return scale > 0.0 ? (point - displayedVideoRect().topLeft()) / scale : QPointF{};
}

QPointF VideoViewport::scriptToScreen(const QPointF &point) const
{
    if (m_scriptSize.width() <= 0.0 || m_scriptSize.height() <= 0.0)
        return {};
    return videoToScreen(QPointF(point.x() * m_videoSize.width() / m_scriptSize.width(),
                                point.y() * m_videoSize.height() / m_scriptSize.height()));
}

QPointF VideoViewport::screenToScript(const QPointF &point) const
{
    const QPointF video = screenToVideo(point);
    return m_videoSize.width() > 0.0 && m_videoSize.height() > 0.0
        ? QPointF(video.x() * m_scriptSize.width() / m_videoSize.width(),
                  video.y() * m_scriptSize.height() / m_videoSize.height())
        : QPointF{};
}

QPointF VideoViewport::screenDeltaToScriptDelta(const QPointF &delta) const
{
    const qreal scale = contentScale();
    return scale > 0.0 && m_videoSize.width() > 0.0 && m_videoSize.height() > 0.0
        ? QPointF(delta.x() * m_scriptSize.width() / (scale * m_videoSize.width()),
                  delta.y() * m_scriptSize.height() / (scale * m_videoSize.height()))
        : QPointF{};
}

QPointF VideoViewport::scriptDeltaToScreenDelta(const QPointF &delta) const
{
    return QPointF(delta.x() * contentScale() * m_videoSize.width() / m_scriptSize.width(),
                   delta.y() * contentScale() * m_videoSize.height() / m_scriptSize.height());
}

void VideoViewport::zoomAt(const QPointF &anchor, qreal factor)
{
    if (!qIsFinite(factor) || factor <= 0.0)
        return;
    zoomTo(anchor, m_contentZoom * factor);
}

void VideoViewport::zoomTo(const QPointF &anchor, qreal zoom)
{
    if (fitScale() <= 0.0 || !qIsFinite(zoom))
        return;
    const QPointF videoPoint = screenToVideo(anchor);
    m_contentZoom = qBound(minimumZoom, zoom, maximumZoom);
    const qreal scale = contentScale();
    m_pan = anchor - viewportCenter()
        - QPointF((videoPoint.x() - m_videoSize.width() / 2.0) * scale,
                  (videoPoint.y() - m_videoSize.height() / 2.0) * scale);
    clampPan();
    announceTransform();
}

void VideoViewport::beginAnchoredZoom(const QPointF &anchor)
{
    m_anchoredZoom = true;
    m_zoomAnchor = anchor;
    m_zoomAnchorVideoPoint = screenToVideo(anchor);
}

void VideoViewport::setAnchoredZoom(qreal zoom)
{
    updateAnchoredZoom(m_zoomAnchor, zoom);
}

void VideoViewport::updateAnchoredZoom(const QPointF &screenAnchor, qreal zoom)
{
    if (!m_anchoredZoom || fitScale() <= 0.0 || !qIsFinite(zoom))
        return;
    m_contentZoom = qBound(minimumZoom, zoom, maximumZoom);
    const qreal scale = contentScale();
    m_pan = screenAnchor - viewportCenter()
        - QPointF((m_zoomAnchorVideoPoint.x() - m_videoSize.width() / 2.0) * scale,
                  (m_zoomAnchorVideoPoint.y() - m_videoSize.height() / 2.0) * scale);
    clampPan();
    announceTransform();
}

void VideoViewport::endAnchoredZoom()
{
    m_anchoredZoom = false;
}

void VideoViewport::panBy(const QPointF &delta)
{
    if (delta.isNull())
        return;
    m_pan += delta;
    clampPan();
    announceTransform();
}

void VideoViewport::resetView()
{
    if (m_contentZoom == 1.0 && m_pan.isNull())
        return;
    m_contentZoom = 1.0;
    m_pan = {};
    m_panning = false;
    m_anchoredZoom = false;
    announceTransform();
}

void VideoViewport::beginPan(const QPointF &screenPoint)
{
    m_panning = true;
    m_panStart = m_pan;
    m_panPointerStart = screenPoint;
}

void VideoViewport::updatePan(const QPointF &screenPoint)
{
    if (!m_panning)
        return;
    m_pan = m_panStart + screenPoint - m_panPointerStart;
    clampPan();
    announceTransform();
}

void VideoViewport::endPan()
{
    m_panning = false;
}

QPointF VideoViewport::viewportCenter() const
{
    return QPointF(m_viewportSize.width() / 2.0, m_viewportSize.height() / 2.0);
}

void VideoViewport::clampPan()
{
    if (m_viewportSize.isEmpty() || m_videoSize.isEmpty())
        return;
    const QSizeF content = m_videoSize * contentScale();
    // With a zoomed-in image, allow an edge to pass the viewport center by
    // 3/4 of the viewport. When zoomed out, cap pan so a visible strip of the
    // small frame always remains available to grab and recover.
    const auto limit = [](qreal contentExtent, qreal viewportExtent) {
        const qreal margin = contentExtent >= viewportExtent
            ? viewportExtent * 0.75 : std::min(viewportExtent * 0.25, contentExtent * 0.5);
        return std::abs(contentExtent - viewportExtent) * 0.5 + margin;
    };
    const qreal maxX = limit(content.width(), m_viewportSize.width());
    const qreal maxY = limit(content.height(), m_viewportSize.height());
    m_pan.setX(qBound(-maxX, m_pan.x(), maxX));
    m_pan.setY(qBound(-maxY, m_pan.y(), maxY));
}

void VideoViewport::announceTransform()
{
    emit transformChanged();
}

} // namespace yoake::ui
