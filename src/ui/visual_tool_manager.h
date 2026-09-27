#pragma once

#include "ui/visual_tool.h"

#include <QtCore/QHash>
#include <QtCore/QObject>
#include <QtCore/QPointer>
#include <QtCore/QVariantList>

namespace yoake::app { class DocumentContext; }
namespace yoake::ui { class VideoViewport; }

namespace yoake::ui {

// Registry, lifecycle owner, and input/overlay router for built-in and
// extension visual tools. Geometry and ASS editing live in VisualTool objects.
class VisualToolManager final : public QObject {
    Q_OBJECT
    Q_PROPERTY(QString activeToolId READ activeToolId WRITE setActiveToolId NOTIFY activeToolChanged)
    Q_PROPERTY(QVariantList toolDescriptors READ toolDescriptors NOTIFY toolsChanged)
    Q_PROPERTY(QVariantList contextOptions READ contextOptions NOTIFY contextOptionsChanged)
    Q_PROPERTY(QVariantList overlayFeatures READ overlayFeatures NOTIFY overlayChanged)
    Q_PROPERTY(QString coordinateLabel READ coordinateLabel NOTIFY overlayChanged)

public:
    VisualToolManager(app::DocumentContext *document, VideoViewport *viewport, QObject *parent = nullptr);

    QString activeToolId() const { return m_activeToolId; }
    QVariantList toolDescriptors() const { return m_tools; }
    QVariantList contextOptions() const;
    QVariantList overlayFeatures() const { return m_features; }
    QString coordinateLabel() const { return m_coordinateLabel; }

    Q_INVOKABLE void setActiveToolId(const QString &toolId);
    Q_INVOKABLE void setOption(const QString &optionId, const QVariant &value = true);
    Q_INVOKABLE void pointerDown(qreal x, qreal y, int button, int modifiers);
    Q_INVOKABLE void pointerMove(qreal x, qreal y, int buttons, int modifiers);
    Q_INVOKABLE void pointerUp(qreal x, qreal y, int button, int modifiers);
    Q_INVOKABLE void pointerLeave();
    Q_INVOKABLE bool wheel(qreal x, qreal y, qreal deltaX, qreal deltaY, int modifiers);
    Q_INVOKABLE void keyDown(int key, int modifiers);
    Q_INVOKABLE void cancelOperation();
    Q_INVOKABLE void commitOperation();
    bool registerToolFactory(const QVariantMap &descriptor, VisualToolFactory factory);

signals:
    void activeToolChanged();
    void toolsChanged();
    void contextOptionsChanged();
    void overlayChanged();

private:
    [[nodiscard]] std::shared_ptr<VisualTool> tool(const QString &id) const;
    [[nodiscard]] std::shared_ptr<VisualTool> activeTool() const { return tool(m_activeToolId); }
    void rebuildOverlay();

    QPointer<app::DocumentContext> m_document;
    QPointer<VideoViewport> m_viewport;
    QHash<QString, VisualToolFactory> m_factories;
    mutable QHash<QString, std::shared_ptr<VisualTool>> m_instances;
    QVariantList m_tools;
    QVariantList m_features;
    QString m_activeLineId;
    QString m_activeToolId{QStringLiteral("position")};
    QString m_coordinateLabel;
};

} // namespace yoake::ui
