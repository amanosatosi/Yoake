#pragma once

#include <QtCore/QPointF>
#include <QtCore/QString>
#include <QtCore/QVariant>
#include <QtCore/QVariantList>
#include <QtCore/QVariantMap>

#include <functional>
#include <memory>

namespace yoake::app { class DocumentContext; }
namespace yoake::ui { class VideoViewport; }

namespace yoake::ui {

// Renderer-neutral interface for custom geometry tools. Pointer positions are
// Qt Quick logical pixels; tools map geometry through the shared viewport and
// mutate ASS only through DocumentContext's edit/undo boundary.
class VisualTool {
public:
    virtual ~VisualTool() = default;
    [[nodiscard]] virtual QVariantMap descriptor() const = 0;
    [[nodiscard]] virtual QVariantList contextOptions() const { return {}; }
    [[nodiscard]] virtual QVariantList renderOverlay() const = 0;
    virtual void activate() {}
    virtual void deactivate() {}
    virtual void pointerDown(const QPointF &, int) {}
    virtual void pointerMove(const QPointF &, int) {}
    virtual void pointerUp(const QPointF &, int) {}
    virtual void pointerLeave() {}
    virtual bool wheel(const QPointF &, const QPointF &, int, int) { return false; }
    virtual void keyDown(int, int) {}
    virtual void setOption(const QString &, const QVariant &) {}
    virtual void cancelOperation() {}
    virtual void commitOperation() {}
};

using VisualToolFactory = std::function<std::shared_ptr<VisualTool>(
    app::DocumentContext *, VideoViewport *)>;

} // namespace yoake::ui
