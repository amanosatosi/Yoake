#pragma once

#include <QtCore/QObject>
#include <QtCore/QPointF>
#include <QtCore/QRectF>
#include <QtCore/QSizeF>

namespace yoake::ui {

// Authoritative map between source-video pixels, ASS script pixels, and the
// logical-pixel coordinates used by Qt Quick input and scene-graph items.
// Qt Quick already applies device-pixel scaling at presentation time, so this
// class deliberately never multiplies pointer positions by devicePixelRatio.
class VideoViewport final : public QObject {
    Q_OBJECT
    Q_PROPERTY(QSizeF viewportSize READ viewportSize WRITE setViewportSize NOTIFY transformChanged)
    Q_PROPERTY(QSizeF videoSize READ videoSize WRITE setVideoSize NOTIFY transformChanged)
    Q_PROPERTY(QSizeF scriptSize READ scriptSize WRITE setScriptSize NOTIFY transformChanged)
    Q_PROPERTY(QRectF displayedVideoRect READ displayedVideoRect NOTIFY transformChanged)
    Q_PROPERTY(QRectF fittedVideoRect READ fittedVideoRect NOTIFY transformChanged)
    Q_PROPERTY(qreal fitScale READ fitScale NOTIFY transformChanged)
    Q_PROPERTY(qreal contentZoom READ contentZoom NOTIFY transformChanged)
    Q_PROPERTY(QPointF panOffset READ panOffset NOTIFY transformChanged)

public:
    explicit VideoViewport(QObject *parent = nullptr);

    QSizeF viewportSize() const { return m_viewportSize; }
    QSizeF videoSize() const { return m_videoSize; }
    QSizeF scriptSize() const { return m_scriptSize; }
    QRectF displayedVideoRect() const;
    QRectF fittedVideoRect() const;
    qreal fitScale() const;
    qreal contentZoom() const { return m_contentZoom; }
    QPointF panOffset() const { return m_pan; }
    qreal contentScale() const { return fitScale() * m_contentZoom; }

    void setViewportSize(const QSizeF &size);
    void setVideoSize(const QSizeF &size);
    void setScriptSize(const QSizeF &size);

    Q_INVOKABLE QPointF videoToScreen(const QPointF &videoPoint) const;
    Q_INVOKABLE QPointF screenToVideo(const QPointF &screenPoint) const;
    Q_INVOKABLE QPointF scriptToScreen(const QPointF &scriptPoint) const;
    Q_INVOKABLE QPointF screenToScript(const QPointF &screenPoint) const;
    Q_INVOKABLE QPointF screenDeltaToScriptDelta(const QPointF &screenDelta) const;
    Q_INVOKABLE QPointF scriptDeltaToScreenDelta(const QPointF &scriptDelta) const;

    Q_INVOKABLE void zoomAt(const QPointF &screenAnchor, qreal factor);
    Q_INVOKABLE void zoomTo(const QPointF &screenAnchor, qreal zoom);
    Q_INVOKABLE void beginAnchoredZoom(const QPointF &screenAnchor);
    Q_INVOKABLE void setAnchoredZoom(qreal zoom);
    Q_INVOKABLE void updateAnchoredZoom(const QPointF &screenAnchor, qreal zoom);
    Q_INVOKABLE void endAnchoredZoom();
    Q_INVOKABLE void panBy(const QPointF &screenDelta);
    Q_INVOKABLE void resetView();

    void beginPan(const QPointF &screenPoint);
    void updatePan(const QPointF &screenPoint);
    void endPan();

signals:
    void transformChanged();

private:
    QPointF viewportCenter() const;
    void clampPan();
    void announceTransform();

    QSizeF m_viewportSize;
    QSizeF m_videoSize;
    QSizeF m_scriptSize{1920.0, 1080.0};
    QPointF m_pan;
    QPointF m_panStart;
    QPointF m_panPointerStart;
    QPointF m_zoomAnchor;
    QPointF m_zoomAnchorVideoPoint;
    qreal m_contentZoom = 1.0;
    bool m_panning = false;
    bool m_anchoredZoom = false;
};

} // namespace yoake::ui
