#include "ui/spectrum_item.h"

#include "media/media_session.h"

#include <QtQuick/QQuickWindow>
#include <QtQuick/QSGSimpleTextureNode>
#include <QtQuick/QSGTexture>

namespace yoake::ui {

SpectrumItem::SpectrumItem(QQuickItem *parent) : QQuickItem(parent)
{
    setFlag(ItemHasContents, true);
    m_requestTimer.setSingleShot(true);
    m_requestTimer.setInterval(24);
    connect(&m_requestTimer, &QTimer::timeout, this, &SpectrumItem::requestNow);
}

void SpectrumItem::setSession(media::MediaSession *session)
{
    if (m_session == session)
        return;
    disconnect(m_imageConnection);
    disconnect(m_metadataConnection);
    disconnect(m_destroyedConnection);
    m_session = session;
    if (m_session) {
        m_imageConnection = connect(m_session, &media::MediaSession::spectrumImageChanged,
            this, &SpectrumItem::refreshImage);
        m_metadataConnection = connect(m_session, &media::MediaSession::metadataChanged,
            this, &SpectrumItem::scheduleRequest);
        m_destroyedConnection = connect(m_session, &QObject::destroyed, this, [this] {
            m_session = nullptr;
            refreshImage();
            emit sessionChanged();
        });
    }
    refreshImage();
    scheduleRequest();
    emit sessionChanged();
}

void SpectrumItem::setStartMs(qint64 value)
{
    if (m_startMs == value)
        return;
    m_startMs = value;
    emit startMsChanged();
    scheduleRequest();
}

void SpectrumItem::setEndMs(qint64 value)
{
    if (m_endMs == value)
        return;
    m_endMs = value;
    emit endMsChanged();
    scheduleRequest();
}

void SpectrumItem::setLowColor(const QColor &value)
{
    if (m_lowColor == value)
        return;
    m_lowColor = value;
    emit colorsChanged();
    scheduleRequest();
}

void SpectrumItem::setMidColor(const QColor &value)
{
    if (m_midColor == value)
        return;
    m_midColor = value;
    emit colorsChanged();
    scheduleRequest();
}

void SpectrumItem::setHighColor(const QColor &value)
{
    if (m_highColor == value)
        return;
    m_highColor = value;
    emit colorsChanged();
    scheduleRequest();
}

void SpectrumItem::setActive(bool value)
{
    if (m_active == value)
        return;
    m_active = value;
    emit activeChanged();
    if (m_active)
        scheduleRequest();
    update();
}

void SpectrumItem::geometryChange(const QRectF &newGeometry, const QRectF &oldGeometry)
{
    QQuickItem::geometryChange(newGeometry, oldGeometry);
    if (newGeometry.size().toSize() != oldGeometry.size().toSize()) {
        scheduleRequest();
        update();
    }
}

void SpectrumItem::scheduleRequest()
{
    if (m_active)
        m_requestTimer.start();
}

void SpectrumItem::requestNow()
{
    if (!m_active || !m_session || m_endMs <= m_startMs || width() < 1.0 || height() < 1.0)
        return;
    m_session->requestSpectrumViewport(m_startMs, m_endMs,
        qRound(width()), qRound(height()), m_lowColor, m_midColor, m_highColor);
}

void SpectrumItem::refreshImage()
{
    m_image = m_session ? m_session->spectrumImage() : QImage{};
    m_textureDirty = true;
    update();
}

QSGNode *SpectrumItem::updatePaintNode(QSGNode *oldNode, UpdatePaintNodeData *)
{
    if (!m_active || m_image.isNull()) {
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
    node->setRect(boundingRect());
    node->setFiltering(QSGTexture::Linear);
    return node;
}

} // namespace yoake::ui
