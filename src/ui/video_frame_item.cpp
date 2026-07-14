#include "ui/video_frame_item.h"

#include "media/media_session.h"

#include <QtGui/QPainter>

namespace yoake::ui {

VideoFrameItem::VideoFrameItem(QQuickItem *parent) : QQuickPaintedItem(parent)
{
    setAntialiasing(false);
}

void VideoFrameItem::paint(QPainter *painter)
{
    if (m_session && !m_session->frameImage().isNull()) {
        painter->setRenderHint(QPainter::SmoothPixmapTransform, false);
        painter->drawImage(m_contentRect, m_session->frameImage());
    }
}

void VideoFrameItem::setSession(media::MediaSession *session)
{
    if (m_session == session)
        return;
    disconnect(m_frameConnection);
    disconnect(m_destroyedConnection);
    m_session = session;
    if (m_session) {
        m_frameConnection = connect(m_session, &media::MediaSession::frameImageChanged,
            this, &VideoFrameItem::refresh);
        m_destroyedConnection = connect(m_session, &QObject::destroyed, this, [this] {
            m_session = nullptr;
            refresh();
            emit sessionChanged();
        });
    }
    refresh();
    emit sessionChanged();
}

void VideoFrameItem::geometryChange(const QRectF &newGeometry, const QRectF &oldGeometry)
{
    QQuickPaintedItem::geometryChange(newGeometry, oldGeometry);
    if (newGeometry.size() != oldGeometry.size())
        updateContentRect();
}

void VideoFrameItem::refresh()
{
    updateContentRect();
    update();
}

void VideoFrameItem::updateContentRect()
{
    QRectF next;
    if (m_session && !m_session->frameImage().isNull()) {
        QSizeF size = m_session->frameImage().size();
        size.scale(boundingRect().size(), Qt::KeepAspectRatio);
        next = QRectF(QPointF((width() - size.width()) / 2.0,
                              (height() - size.height()) / 2.0), size);
    }
    if (next == m_contentRect)
        return;
    m_contentRect = next;
    emit contentRectChanged();
}

} // namespace yoake::ui
