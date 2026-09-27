#pragma once

#include <QtCore/QMetaObject>
#include <QtCore/QPointer>
#include <QtCore/QRectF>
#include <QtGui/QImage>
#include <QtQuick/QQuickItem>

namespace yoake::ui { class VideoViewport; }

namespace yoake::media { class MediaSession; }

namespace yoake::ui {

class VideoFrameItem : public QQuickItem {
    Q_OBJECT
    Q_PROPERTY(yoake::media::MediaSession *session READ session WRITE setSession NOTIFY sessionChanged)
    Q_PROPERTY(yoake::ui::VideoViewport *viewport READ viewport WRITE setViewport NOTIFY viewportChanged)
    Q_PROPERTY(QRectF contentRect READ contentRect NOTIFY contentRectChanged)

public:
    explicit VideoFrameItem(QQuickItem *parent = nullptr);

    [[nodiscard]] media::MediaSession *session() const { return m_session.data(); }
    [[nodiscard]] VideoViewport *viewport() const { return m_viewport.data(); }
    [[nodiscard]] QRectF contentRect() const { return m_contentRect; }
    void setSession(media::MediaSession *session);
    void setViewport(VideoViewport *viewport);

signals:
    void sessionChanged();
    void viewportChanged();
    void contentRectChanged();

protected:
    void geometryChange(const QRectF &newGeometry, const QRectF &oldGeometry) override;
    QSGNode *updatePaintNode(QSGNode *oldNode, UpdatePaintNodeData *) override;

private:
    void refresh();
    void updateContentRect();

    QPointer<media::MediaSession> m_session;
    QPointer<VideoViewport> m_viewport;
    QMetaObject::Connection m_frameConnection;
    QMetaObject::Connection m_destroyedConnection;
    QMetaObject::Connection m_viewportConnection;
    QImage m_image;
    QRectF m_contentRect;
    bool m_textureDirty = true;
};

} // namespace yoake::ui
