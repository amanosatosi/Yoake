#pragma once

#include "ass/ass_event.h"
#include "ui/visual_snap_service.h"
#include "ui/visual_tool.h"

#include <QtCore/QHash>
#include <QtCore/QObject>
#include <QtCore/QPointF>
#include <QtCore/QRectF>
#include <QtCore/QPointer>
#include <QtCore/QSet>
#include <QtCore/QVariantList>
#include <QtCore/QVariantMap>

namespace yoake::app { class DocumentContext; }
namespace yoake::ui { class VideoViewport; }

namespace yoake::ui {

// Owns tool descriptors, active selection, custom-tool factories, and the
// shared overlay/hit-test surface. Standard-tool semantics currently remain in
// this class; all ASS edits still cross DocumentContext's undo boundary.
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
    void rebuildOverlay();
    void addPoint(QString id, QPointF scriptPoint, QString label = {}, QString role = {}, bool selected = false,
                  QString nodeType = {});
    void addLine(QString id, QPointF start, QPointF end, QString role = {});
    void addRect(QString id, QRectF rect, QString role = {});
    QString hitFeature(const QPointF &point) const;
    QVariantMap feature(const QString &id) const;
    void beginDrag(const QString &role, const QPointF &screenPoint, int modifiers);
    void updateDrag(const QPointF &screenPoint, int modifiers);
    QString editedText(const QPointF &screenPoint, int modifiers) const;
    QPointF effectivePosition(const ass::Event &event) const;
    QString shiftedTextForEvent(const ass::Event &event, QString text, const QPointF &delta) const;
    QPointF snapPoint(QPointF point, int modifiers, QVariantList *guides = nullptr) const;
    void finishFreehand();
    void appendVectorPoint(const QPointF &scriptPoint, bool bezier, bool insert = false);
    bool deleteSelectedVectorPoint();
    bool convertSelectedVectorSegment(bool toBezier);
    void commitScaleRectangle(const QPointF &scriptEnd, int modifiers);
    std::shared_ptr<VisualTool> customTool(const QString &toolId) const;

    QPointer<app::DocumentContext> m_document;
    QPointer<VideoViewport> m_viewport;
    VisualSnapService m_snapService;
    QHash<QString, VisualToolFactory> m_customFactories;
    QHash<QString, std::shared_ptr<VisualTool>> m_customTools;
    QVariantList m_tools;
    QVariantList m_features;
    QVariantList m_guides;
    QString m_activeToolId{QStringLiteral("position")};
    QString m_contextOption;
    QVariant m_contextOptionValue;
    QString m_dragRole;
    QString m_selectedFeature;
    QString m_coordinateLabel;
    QString m_originalText;
    QString m_dragText;
    QPointF m_pointer;
    QPointF m_dragStartScreen;
    QPointF m_dragStartScript;
    QPointF m_dragStartHandle;
    QPointF m_freehandLast;
    QVector<QPointF> m_freehand;
    QVector<QPointF> m_bezierPoints;
    QVector<ass::Event> m_shiftEvents;
    QStringList m_shiftEventIds;
    QPointF m_movePointA;
    QPointF m_rotationPointA;
    qint64 m_moveTimeA = 0;
    QString m_scaleAxis{QStringLiteral("both")};
    int m_dragModifiers = 0;
    int m_selectedVectorPoint = -1;
    QSet<int> m_selectedVectorPoints;
    bool m_dragging = false;
    bool m_transactionStarted = false;
    bool m_freehandDrawing = false;
    bool m_hasMovePointA = false;
    bool m_hasRotationPointA = false;
    bool m_scaleRectMode = false;
    QHash<QString, bool> m_shiftComponents;
};

} // namespace yoake::ui
