#include "ui/video_frame_item.h"

#include "media/media_session.h"

#include <QtQuick/QQuickWindow>
#include <QtQuick/QSGSimpleTextureNode>
#include <QtQuick/QSGTexture>

namespace yoake::ui {

VideoFrameItem::VideoFrameItem(QQuickItem *parent) : QQuickItem(parent)
{
    setFlag(ItemHasContents, true);
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
    QQuickItem::geometryChange(newGeometry, oldGeometry);
    if (newGeometry.size() != oldGeometry.size())
        updateContentRect();
}

QSGNode *VideoFrameItem::updatePaintNode(QSGNode *oldNode, UpdatePaintNodeData *)
{
    if (m_image.isNull()) {
        delete oldNode;
        m_textureDirty = false;
        return nullptr;
    }
    auto *node = static_cast<QSGSimpleTextureNode *>(oldNode);
    if (!node) {
        node = new QSGSimpleTextureNode;
        node->setOwnsTexture(true);
    }

    if (m_textureDirty) {
        QSGTexture *oldTexture = node->texture();
        QSGTexture *newTexture = window() ? window()->createTextureFromImage(m_image) : nullptr;
        node->setOwnsTexture(false);
        node->setTexture(newTexture);
        node->setOwnsTexture(true);
        delete oldTexture;
        m_textureDirty = false;
    }
    node->setRect(m_contentRect);
    node->setFiltering(QSGTexture::Linear);
    return node;
}

void VideoFrameItem::refresh()
{
    m_image = m_session ? m_session->frameImage() : QImage{};
    m_textureDirty = true;
    updateContentRect();
    update();
}

void VideoFrameItem::updateContentRect()
{
    QRectF next;
    if (!m_image.isNull()) {
        QSizeF size = m_image.size();
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
