#include "ui/subtitle_overlay_item.h"

#include "app/document_context.h"
#include "renderer/mangetsu_session.h"

#include <QtQuick/QQuickWindow>
#include <QtQuick/QSGSimpleTextureNode>
#include <QtQuick/QSGTexture>

#include <atomic>

namespace yoake::ui {
namespace {
std::atomic<quint64> nextClientId{1};
}

SubtitleOverlayItem::SubtitleOverlayItem(QQuickItem *parent)
    : QQuickItem(parent), m_clientId(nextClientId.fetch_add(1))
{
    setFlag(ItemHasContents, true);
    m_coalesceTimer.setSingleShot(true);
    m_coalesceTimer.setInterval(16);
    connect(&m_coalesceTimer, &QTimer::timeout, this, &SubtitleOverlayItem::renderNow);
}

void SubtitleOverlayItem::setDocument(app::DocumentContext *document)
{
    if (m_document == document)
        return;
    ++m_requestId;
    m_coalesceTimer.stop();
    disconnect(m_documentConnection);
    disconnect(m_destroyedConnection);
    disconnect(m_rendererConnection);
    m_document = document;
    m_cachedScript.clear();
    clearPresentation();
    if (m_document) {
        m_cachedScript = m_document->rendererSnapshot();
        m_documentConnection = connect(m_document, &app::DocumentContext::rendererRevisionChanged,
            this, [this] {
                if (m_document)
                    m_cachedScript = m_document->rendererSnapshot();
                scheduleRender();
            });
        m_destroyedConnection = connect(m_document, &QObject::destroyed, this, [this] {
            ++m_requestId;
            m_coalesceTimer.stop();
            m_document = nullptr;
            m_cachedScript.clear();
            clearPresentation();
            emit documentChanged();
        });
        const auto session = m_document->rendererSession();
        m_rendererConnection = connect(session.get(), &renderer::MangetsuSession::renderReady,
            this, [this](quint64 clientId, quint64 requestId, const QImage &image, const QString &error) {
                if (clientId != m_clientId || requestId != m_requestId)
                    return;
                m_image = image;
                m_textureDirty = true;
                m_errorString = error;
                m_rendering = false;
                emit errorStringChanged();
                emit renderingChanged();
                update();
            });
    }
    emit documentChanged();
    scheduleRender();
}

void SubtitleOverlayItem::setTimeMs(qint64 timeMs)
{
    if (m_timeMs == timeMs)
        return;
    m_timeMs = timeMs;
    emit timeMsChanged();
    scheduleRender();
}

void SubtitleOverlayItem::geometryChange(const QRectF &newGeometry, const QRectF &oldGeometry)
{
    QQuickItem::geometryChange(newGeometry, oldGeometry);
    if (newGeometry.size().toSize() != oldGeometry.size().toSize()) {
        scheduleRender();
        update();
    }
}

QSGNode *SubtitleOverlayItem::updatePaintNode(QSGNode *oldNode, UpdatePaintNodeData *)
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
    node->setRect(boundingRect());
    node->setFiltering(QSGTexture::Linear);
    return node;
}

void SubtitleOverlayItem::scheduleRender()
{
    ++m_requestId;
    if (m_document)
        m_coalesceTimer.start();
}

void SubtitleOverlayItem::renderNow()
{
    if (!m_document || width() < 1.0 || height() < 1.0) {
        clearPresentation();
        return;
    }
    if (!m_rendering) {
        m_rendering = true;
        emit renderingChanged();
    }
    const auto session = m_document->rendererSession();
    session->requestRender(m_clientId, m_requestId, m_cachedScript,
        QSize(qRound(width()), qRound(height())), m_timeMs);
}

void SubtitleOverlayItem::clearPresentation()
{
    const bool wasRendering = m_rendering;
    const bool hadError = !m_errorString.isEmpty();
    m_image = {};
    m_textureDirty = true;
    m_rendering = false;
    m_errorString.clear();
    if (wasRendering)
        emit renderingChanged();
    if (hadError)
        emit errorStringChanged();
    update();
}

} // namespace yoake::ui
