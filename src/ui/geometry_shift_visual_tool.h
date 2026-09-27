#pragma once

#include "ui/visual_tool_base.h"

#include <QtCore/QHash>
#include <QtCore/QPointF>
#include <QtCore/QStringList>
#include <QtCore/QVector>

namespace yoake::ui {

class GeometryShiftTool final : public VisualToolBase {
public:
    GeometryShiftTool(app::DocumentContext *, VideoViewport *);
    [[nodiscard]] QVariantMap descriptor() const override;
    [[nodiscard]] QVariantList contextOptions() const override;
    void setOption(const QString &, const QVariant &) override;
    void pointerDown(const QPointF &, int) override;
    void pointerMove(const QPointF &, int) override;
    void pointerUp(const QPointF &, int) override;
    void keyDown(int, int) override;
    void cancelOperation() override;
    void commitOperation() override;
protected:
    void buildOverlay(QVariantList &) const override;
private:
    [[nodiscard]] QString shiftedText(const ass::Event &, QPointF) const;
    void clearGesture();
    QVector<ass::Event> m_events;
    QStringList m_eventIds;
    QHash<QString, bool> m_components;
    QPointF m_startScreen;
    QPointF m_startScript;
    QPointF m_anchor;
    bool m_dragging = false;
};

void registerGeometryShiftTool(class VisualToolManager &manager);

} // namespace yoake::ui
