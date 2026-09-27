#pragma once

#include "ui/visual_tool_base.h"

#include <QtCore/QVector>

namespace yoake::ass { class VectorPath; }

namespace yoake::ui {

// Shared interaction/selection implementation for ASS vector clips and \\p
// drawings. Storage-specific parsing and serialization are isolated behind
// loadPath()/storePath(); both tools edit the same VectorPath model.
class VectorPathEditor : public VisualToolBase {
public:
    VectorPathEditor(app::DocumentContext *, VideoViewport *);
    [[nodiscard]] QVariantList contextOptions() const override;
    void activate() override;
    void deactivate() override;
    void activeLineChanged() override;
    void pointerDown(const QPointF &, int) override;
    void pointerMove(const QPointF &, int) override;
    void pointerUp(const QPointF &, int) override;
    void pointerLeave() override;
    void keyDown(int, int) override;
    void setOption(const QString &, const QVariant &) override;
    void cancelOperation() override;
    void commitOperation() override;
protected:
    struct PathSnapshot {
        QString text;
        QString pathText;
        int scale = 1;
        QPointF anchor;
        bool exists = false;
        bool editable = false;
    };

    void buildOverlay(QVariantList &features) const override;
    [[nodiscard]] virtual PathSnapshot loadPathSnapshot(const QString &text) const = 0;
    [[nodiscard]] virtual QString storePath(const PathSnapshot &, const ass::VectorPath &) const = 0;
    [[nodiscard]] virtual bool supportsInverseClip() const { return false; }
    [[nodiscard]] virtual QString invertedPathText(const QString &text) const { return text; }
    [[nodiscard]] virtual QString toolName() const = 0;
    [[nodiscard]] virtual QString icon() const = 0;
    [[nodiscard]] virtual QString tooltip() const = 0;
    [[nodiscard]] virtual QString commandId() const = 0;
    [[nodiscard]] virtual QString factoryId() const = 0;
private:
    [[nodiscard]] QPointF pathPointToScript(const PathSnapshot &, QPointF) const;
    [[nodiscard]] QPointF scriptToPathPoint(const PathSnapshot &, QPointF) const;
    [[nodiscard]] QString hitFeatureAt(QPointF point, const QVariantList &features) const;
    [[nodiscard]] QString textForNodeDrag(QPointF screenPoint, int modifiers) const;
    [[nodiscard]] QString textForPathDrag(QPointF screenPoint) const;
    void appendPoint(QPointF scriptPoint, bool bezier, bool insert);
    void finishFreehand();
    void deleteSelectedNodes();
    void convertSelectedSegment(bool toBezier);
    void clearGesture();

    QString m_contextOption;
    QString m_selectedFeature;
    QSet<int> m_selectedNodes;
    int m_dragNode = -1;
    QString m_dragRole;
    QPointF m_pointer{-1000, -1000};
    QPointF m_dragStartScreen;
    QPointF m_dragStartScript;
    QPointF m_dragStartHandle;
    QString m_dragText;
    QString m_dragOriginalText;
    QVector<QPointF> m_freehand;
    QPointF m_freehandLast;
    QVector<QPointF> m_bezierPoints;
    bool m_dragging = false;
    bool m_freehandDrawing = false;
};

class VectorClipTool final : public VectorPathEditor {
public:
    VectorClipTool(app::DocumentContext *, VideoViewport *);
    [[nodiscard]] QVariantMap descriptor() const override;
protected:
    [[nodiscard]] PathSnapshot loadPathSnapshot(const QString &) const override;
    [[nodiscard]] QString storePath(const PathSnapshot &, const ass::VectorPath &) const override;
    [[nodiscard]] bool supportsInverseClip() const override { return true; }
    [[nodiscard]] QString invertedPathText(const QString &) const override;
    [[nodiscard]] QString toolName() const override;
    [[nodiscard]] QString icon() const override;
    [[nodiscard]] QString tooltip() const override;
    [[nodiscard]] QString commandId() const override;
    [[nodiscard]] QString factoryId() const override;
};

class DrawingTool final : public VectorPathEditor {
public:
    DrawingTool(app::DocumentContext *, VideoViewport *);
    [[nodiscard]] QVariantMap descriptor() const override;
protected:
    [[nodiscard]] PathSnapshot loadPathSnapshot(const QString &) const override;
    [[nodiscard]] QString storePath(const PathSnapshot &, const ass::VectorPath &) const override;
    [[nodiscard]] QString toolName() const override;
    [[nodiscard]] QString icon() const override;
    [[nodiscard]] QString tooltip() const override;
    [[nodiscard]] QString commandId() const override;
    [[nodiscard]] QString factoryId() const override;
};

void registerVectorVisualTools(class VisualToolManager &manager);

} // namespace yoake::ui
