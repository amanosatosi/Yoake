#pragma once

#include "ui/visual_tool_base.h"

#include <QtCore/QPointF>
#include <QtCore/QStringView>

namespace yoake::ui {

class DragVisualTool : public VisualToolBase {
public:
    using VisualToolBase::VisualToolBase;
    void pointerDown(const QPointF &screenPoint, int modifiers) override;
    void pointerMove(const QPointF &screenPoint, int modifiers) override;
    void pointerUp(const QPointF &screenPoint, int modifiers) override;
    void activeLineChanged() override;
    void deactivate() override;
    void keyDown(int key, int modifiers) override;
    void commitOperation() override;
    void cancelOperation() override;

protected:
    [[nodiscard]] QString selectedFeature() const { return m_selectedFeature; }
    [[nodiscard]] QString dragRole() const { return m_dragRole; }
    [[nodiscard]] QPointF dragStartScreen() const { return m_dragStartScreen; }
    [[nodiscard]] QPointF dragStartScript() const { return m_dragStartScript; }
    [[nodiscard]] QPointF dragStartHandle() const { return m_dragStartHandle; }
    [[nodiscard]] QString originalText() const { return m_originalText; }
    [[nodiscard]] bool isDragging() const { return m_dragging; }
    [[nodiscard]] virtual QString roleForPointer(const QVariantList &features, const QString &featureId,
                                                 QPointF point, int modifiers) const = 0;
    [[nodiscard]] virtual QPointF handleForPointer(const QVariantList &features, const QString &featureId,
                                                  const QString &role, QPointF point) const;
    [[nodiscard]] virtual QString editedText(QPointF screenPoint, int modifiers) const = 0;
    [[nodiscard]] virtual bool isClickOperation(QStringView role) const { Q_UNUSED(role); return false; }
    virtual void completeClickOperation(QPointF screenPoint, int modifiers);
    virtual void resetToolGesture() {}
    void updateDragPreview(QPointF screenPoint, int modifiers);

private:
    void clearDragState();
    QVariantList m_dragFeatures;
    QString m_selectedFeature;
    QString m_dragRole;
    QString m_originalText;
    QPointF m_dragStartScreen;
    QPointF m_dragStartScript;
    QPointF m_dragStartHandle;
    bool m_dragging = false;
};

class CrosshairTool final : public VisualToolBase {
public:
    CrosshairTool(app::DocumentContext *, VideoViewport *);
    [[nodiscard]] QVariantMap descriptor() const override;
    void pointerDown(const QPointF &, int) override;
    void pointerMove(const QPointF &, int) override;
    void pointerUp(const QPointF &, int) override;
    void pointerLeave() override;
protected:
    void buildOverlay(QVariantList &features) const override;
private:
    QPointF m_pointer{-1000, -1000};
};

class PositionTool final : public DragVisualTool {
public:
    PositionTool(app::DocumentContext *, VideoViewport *);
    [[nodiscard]] QVariantMap descriptor() const override;
    [[nodiscard]] QVariantList contextOptions() const override;
    void setOption(const QString &, const QVariant &) override;
protected:
    void buildOverlay(QVariantList &features) const override;
    [[nodiscard]] QString roleForPointer(const QVariantList &, const QString &, QPointF, int) const override;
    [[nodiscard]] QPointF handleForPointer(const QVariantList &, const QString &, const QString &, QPointF) const override;
    [[nodiscard]] QString editedText(QPointF, int) const override;
};

class MoveTool final : public DragVisualTool {
public:
    MoveTool(app::DocumentContext *, VideoViewport *);
    [[nodiscard]] QVariantMap descriptor() const override;
    [[nodiscard]] QVariantList contextOptions() const override;
    void setOption(const QString &, const QVariant &) override;
protected:
    void buildOverlay(QVariantList &features) const override;
    [[nodiscard]] QString roleForPointer(const QVariantList &, const QString &, QPointF, int) const override;
    [[nodiscard]] QPointF handleForPointer(const QVariantList &, const QString &, const QString &, QPointF) const override;
    [[nodiscard]] QString editedText(QPointF, int) const override;
private:
    QPointF m_movePointA;
    qint64 m_moveTimeA = 0;
    bool m_hasMovePointA = false;
};

class RotateZTool final : public DragVisualTool {
public:
    RotateZTool(app::DocumentContext *, VideoViewport *);
    [[nodiscard]] QVariantMap descriptor() const override;
    [[nodiscard]] QVariantList contextOptions() const override;
    void setOption(const QString &, const QVariant &) override;
protected:
    void buildOverlay(QVariantList &features) const override;
    [[nodiscard]] QString roleForPointer(const QVariantList &, const QString &, QPointF, int) const override;
    [[nodiscard]] QPointF handleForPointer(const QVariantList &, const QString &, const QString &, QPointF) const override;
    [[nodiscard]] QString editedText(QPointF, int) const override;
private:
    QPointF m_rotationPointA;
    bool m_hasRotationPointA = false;
};

class RotateXYTool final : public DragVisualTool {
public:
    RotateXYTool(app::DocumentContext *, VideoViewport *);
    [[nodiscard]] QVariantMap descriptor() const override;
    [[nodiscard]] QVariantList contextOptions() const override;
    void setOption(const QString &, const QVariant &) override;
protected:
    void buildOverlay(QVariantList &features) const override;
    [[nodiscard]] QString roleForPointer(const QVariantList &, const QString &, QPointF, int) const override;
    [[nodiscard]] QString editedText(QPointF, int) const override;
private:
    QString m_axisFilter;
};

class ScaleTool final : public DragVisualTool {
public:
    ScaleTool(app::DocumentContext *, VideoViewport *);
    [[nodiscard]] QVariantMap descriptor() const override;
    [[nodiscard]] QVariantList contextOptions() const override;
    void setOption(const QString &, const QVariant &) override;
protected:
    void buildOverlay(QVariantList &features) const override;
    [[nodiscard]] QString roleForPointer(const QVariantList &, const QString &, QPointF, int) const override;
    [[nodiscard]] QPointF handleForPointer(const QVariantList &, const QString &, const QString &, QPointF) const override;
    [[nodiscard]] QString editedText(QPointF, int) const override;
    [[nodiscard]] bool isClickOperation(QStringView role) const override;
    void completeClickOperation(QPointF, int) override;
private:
    QString m_scaleAxis{QStringLiteral("both")};
    bool m_scaleRectMode = false;
};

class RectClipTool final : public DragVisualTool {
public:
    RectClipTool(app::DocumentContext *, VideoViewport *);
    [[nodiscard]] QVariantMap descriptor() const override;
    [[nodiscard]] QVariantList contextOptions() const override;
    void setOption(const QString &, const QVariant &) override;
protected:
    void buildOverlay(QVariantList &features) const override;
    [[nodiscard]] QString roleForPointer(const QVariantList &, const QString &, QPointF, int) const override;
    [[nodiscard]] QPointF handleForPointer(const QVariantList &, const QString &, const QString &, QPointF) const override;
    [[nodiscard]] QString editedText(QPointF, int) const override;
};

// Registered factory entry point used by VisualToolManager. It intentionally
// uses the same descriptor/factory API as application-provided tools.
void registerBuiltInVisualTools(class VisualToolManager &manager);

} // namespace yoake::ui
