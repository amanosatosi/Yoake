#pragma once

#include <QtCore/QMetaObject>
#include <QtCore/QPointer>
#include <QtCore/QRectF>
#include <QtGui/QImage>
#include <QtQuick/QQuickItem>

namespace yoake::media { class MediaSession; }

namespace yoake::ui {

class VideoFrameItem : public QQuickItem {
    Q_OBJECT
    Q_PROPERTY(yoake::media::MediaSession *session READ session WRITE setSession NOTIFY sessionChanged)
    Q_PROPERTY(QRectF contentRect READ contentRect NOTIFY contentRectChanged)

public:
    explicit VideoFrameItem(QQuickItem *parent = nullptr);

    [[nodiscard]] media::MediaSession *session() const { return m_session.data(); }
    [[nodiscard]] QRectF contentRect() const { return m_contentRect; }
    void setSession(media::MediaSession *session);

signals:
    void sessionChanged();
    void contentRectChanged();

protected:
    void geometryChange(const QRectF &newGeometry, const QRectF &oldGeometry) override;
    QSGNode *updatePaintNode(QSGNode *oldNode, UpdatePaintNodeData *) override;

private:
    void refresh();
    void updateContentRect();

    QPointer<media::MediaSession> m_session;
    QMetaObject::Connection m_frameConnection;
    QMetaObject::Connection m_destroyedConnection;
    QImage m_image;
    QRectF m_contentRect;
    bool m_textureDirty = true;
};

} // namespace yoake::ui
