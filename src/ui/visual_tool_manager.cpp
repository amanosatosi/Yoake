#include "ui/visual_tool_manager.h"

#include "app/document_context.h"
#include "media/media_session.h"
#include "ui/builtin_visual_tools.h"
#include "ui/geometry_shift_visual_tool.h"
#include "ui/vector_path_visual_tools.h"
#include "ui/video_viewport.h"

#include <QtCore/Qt>

#include <algorithm>
#include <utility>

namespace yoake::ui {

VisualToolManager::VisualToolManager(app::DocumentContext *document, VideoViewport *viewport, QObject *parent)
    : QObject(parent), m_document(document), m_viewport(viewport)
{
    registerBuiltInVisualTools(*this);
    registerVectorVisualTools(*this);
    registerGeometryShiftTool(*this);

    if (m_document) {
        m_activeLineId = m_document->lines()->activeId();
        connect(m_document, &app::DocumentContext::activeLineChanged, this, [this] {
            const QString activeLineId = m_document ? m_document->lines()->activeId() : QString{};
            if (activeLineId != m_activeLineId) {
                m_activeLineId = activeLineId;
                if (const auto active = activeTool()) active->activeLineChanged();
            }
            emit contextOptionsChanged();
            rebuildOverlay();
        });
        connect(m_document, &app::DocumentContext::rendererRevisionChanged, this, [this] {
            emit contextOptionsChanged();
            rebuildOverlay();
        });
        if (m_document->media())
            connect(m_document->media(), &media::MediaSession::positionChanged, this, &VisualToolManager::rebuildOverlay);
    }
    if (m_viewport) {
        connect(m_viewport, &VideoViewport::transformChanged, this, [this] {
            // A gesture uses a fixed script-space baseline. Cancel it if the
            // viewport changes mid-drag, so no transaction can be stranded.
            if (const auto active = activeTool()) active->cancelOperation();
            rebuildOverlay();
        });
    }

    if (const auto initial = activeTool()) initial->activate();
    rebuildOverlay();
}

QVariantList VisualToolManager::contextOptions() const
{
    if (const auto active = activeTool())
        return active->contextOptions();
    return {};
}

std::shared_ptr<VisualTool> VisualToolManager::tool(const QString &id) const
{
    const auto existing = m_instances.constFind(id);
    if (existing != m_instances.cend())
        return existing.value();
    const auto factory = m_factories.constFind(id);
    if (factory == m_factories.cend())
        return {};
    std::shared_ptr<VisualTool> created = factory.value()(m_document.data(), m_viewport.data());
    if (!created || created->descriptor().value(QStringLiteral("id")).toString() != id)
        return {};
    m_instances.insert(id, created);
    return created;
}

void VisualToolManager::setActiveToolId(const QString &id)
{
    if (id == m_activeToolId || !m_factories.contains(id))
        return;
    const auto next = tool(id);
    if (!next)
        return;
    if (const auto previous = activeTool()) {
        previous->commitOperation();
        previous->deactivate();
    }
    m_activeToolId = id;
    next->activate();
    emit activeToolChanged();
    emit contextOptionsChanged();
    rebuildOverlay();
}

bool VisualToolManager::registerToolFactory(const QVariantMap &descriptor, VisualToolFactory factory)
{
    const QString id = descriptor.value(QStringLiteral("id")).toString().trimmed();
    if (id.isEmpty() || descriptor.value(QStringLiteral("factory")).toString().trimmed().isEmpty()
        || descriptor.value(QStringLiteral("name")).toString().isEmpty()
        || descriptor.value(QStringLiteral("icon")).toString().isEmpty()
        || descriptor.value(QStringLiteral("tooltip")).toString().isEmpty()
        || descriptor.value(QStringLiteral("commandId")).toString().isEmpty()
        || !factory || m_factories.contains(id))
        return false;
    m_factories.insert(id, std::move(factory));
    m_tools.push_back(descriptor);
    emit toolsChanged();
    return true;
}

void VisualToolManager::setOption(const QString &id, const QVariant &value)
{
    if (const auto active = activeTool()) {
        active->setOption(id, value);
        emit contextOptionsChanged();
        rebuildOverlay();
    }
}

void VisualToolManager::pointerDown(qreal x, qreal y, int button, int modifiers)
{
    if (button != Qt::LeftButton) return;
    if (const auto active = activeTool()) {
        active->pointerDown(QPointF(x, y), modifiers);
        rebuildOverlay();
    }
}

void VisualToolManager::pointerMove(qreal x, qreal y, int buttons, int modifiers)
{
    Q_UNUSED(buttons);
    if (const auto active = activeTool()) {
        active->pointerMove(QPointF(x, y), modifiers);
        rebuildOverlay();
    }
}

void VisualToolManager::pointerUp(qreal x, qreal y, int button, int modifiers)
{
    if (button != Qt::LeftButton) return;
    if (const auto active = activeTool()) {
        active->pointerUp(QPointF(x, y), modifiers);
        emit contextOptionsChanged();
        rebuildOverlay();
    }
}

void VisualToolManager::pointerLeave()
{
    if (const auto active = activeTool()) {
        active->pointerLeave();
        rebuildOverlay();
    }
}

bool VisualToolManager::wheel(qreal x, qreal y, qreal deltaX, qreal deltaY, int modifiers)
{
    const auto active = activeTool();
    if (!active || !active->wheel(QPointF(x, y), QPointF(deltaX, deltaY), modifiers, 0))
        return false;
    rebuildOverlay();
    return true;
}

void VisualToolManager::keyDown(int key, int modifiers)
{
    if (const auto active = activeTool())
        active->keyDown(key, modifiers);
    if (key == Qt::Key_Escape) {
        cancelOperation();
        return;
    }
    if ((modifiers & Qt::ControlModifier) && (modifiers & Qt::AltModifier)
        && ((key >= Qt::Key_1 && key <= Qt::Key_9) || key == Qt::Key_0)) {
        const int index = key == Qt::Key_0 ? 9 : key - Qt::Key_1;
        if (index >= 0 && index < m_tools.size())
            setActiveToolId(m_tools.at(index).toMap().value(QStringLiteral("id")).toString());
        return;
    }
    emit contextOptionsChanged();
    rebuildOverlay();
}

void VisualToolManager::cancelOperation()
{
    if (const auto active = activeTool()) active->cancelOperation();
    rebuildOverlay();
}

void VisualToolManager::commitOperation()
{
    if (const auto active = activeTool()) active->commitOperation();
    rebuildOverlay();
}

void VisualToolManager::rebuildOverlay()
{
    m_features.clear();
    m_coordinateLabel.clear();
    if (!m_document || !m_viewport || m_viewport->videoSize().isEmpty()) {
        emit overlayChanged();
        return;
    }
    if (const auto active = activeTool()) {
        m_features = active->renderOverlay();
        m_coordinateLabel = active->coordinateLabel();
    }
    emit overlayChanged();
}

} // namespace yoake::ui
