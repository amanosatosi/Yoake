#pragma once

#include <QtCore/QMetaObject>
#include <QtCore/QPointer>
#include <QtCore/QRectF>
#include <QtQuick/QQuickPaintedItem>

namespace yoake::media { class MediaSession; }

namespace yoake::ui {

class VideoFrameItem : public QQuickPaintedItem {
    Q_OBJECT
    Q_PROPERTY(yoake::media::MediaSession *session READ session WRITE setSession NOTIFY sessionChanged)
    Q_PROPERTY(QRectF contentRect READ contentRect NOTIFY contentRectChanged)

public:
    explicit VideoFrameItem(QQuickItem *parent = nullptr);

    void paint(QPainter *painter) override;
    [[nodiscard]] media::MediaSession *session() const { return m_session.data(); }
    [[nodiscard]] QRectF contentRect() const { return m_contentRect; }
    void setSession(media::MediaSession *session);

signals:
    void sessionChanged();
    void contentRectChanged();

protected:
    void geometryChange(const QRectF &newGeometry, const QRectF &oldGeometry) override;

private:
    void refresh();
    void updateContentRect();

    QPointer<media::MediaSession> m_session;
    QMetaObject::Connection m_frameConnection;
    QMetaObject::Connection m_destroyedConnection;
    QRectF m_contentRect;
};

} // namespace yoake::ui
