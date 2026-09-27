#include "ui/builtin_visual_tools.h"

#include "app/document_context.h"
#include "ass/visual_tags.h"
#include "media/media_session.h"
#include "models/subtitle_model.h"
#include "ui/video_viewport.h"
#include "ui/visual_tool_manager.h"

#include <QtCore/QLineF>
#include <QtCore/QtMath>

#include <algorithm>
#include <cmath>
#include <utility>

namespace yoake::ui {
namespace {

QVariantMap toolDescriptor(QString id, QString title, QString icon, QString tip, QString command, QString factory)
{
    return {{QStringLiteral("id"), std::move(id)}, {QStringLiteral("name"), std::move(title)},
        {QStringLiteral("icon"), std::move(icon)}, {QStringLiteral("tooltip"), std::move(tip)},
        {QStringLiteral("commandId"), std::move(command)}, {QStringLiteral("factory"), std::move(factory)},
        {QStringLiteral("capabilities"), QStringList{QStringLiteral("ass-override"), QStringLiteral("script-geometry")}}};
}

} // namespace

void DragVisualTool::pointerDown(const QPointF &screenPoint, int modifiers)
{
    clearSnapGuides();
    setCurrentPointer(screenPoint);
    if (!document() || !viewport())
        return;
    m_dragFeatures = renderOverlay();
    m_selectedFeature = hitPoint(m_dragFeatures, screenPoint);
    m_dragRole = roleForPointer(m_dragFeatures, m_selectedFeature, screenPoint, modifiers);
    if (m_dragRole.isEmpty())
        return;
    m_dragStartScreen = screenPoint;
    m_dragStartScript = toScript(screenPoint);
    m_dragStartHandle = handleForPointer(m_dragFeatures, m_selectedFeature, m_dragRole, screenPoint);
    m_originalText = activeText();
    m_dragging = true;
}

void DragVisualTool::pointerMove(const QPointF &screenPoint, int modifiers)
{
    setCurrentPointer(screenPoint);
    if (m_dragging && !isClickOperation(m_dragRole))
        updateDragPreview(screenPoint, modifiers);
}

void DragVisualTool::pointerUp(const QPointF &screenPoint, int modifiers)
{
    setCurrentPointer(screenPoint);
    if (!m_dragging)
        return;
    if (isClickOperation(m_dragRole))
        completeClickOperation(screenPoint, modifiers);
    else
        commitOperation();
    clearDragState();
}

void DragVisualTool::activeLineChanged()
{
    VisualToolBase::activeLineChanged();
    m_selectedFeature.clear();
    m_dragFeatures.clear();
}

void DragVisualTool::deactivate()
{
    VisualToolBase::deactivate();
    m_selectedFeature.clear();
    m_dragFeatures.clear();
}

void DragVisualTool::keyDown(int key, int modifiers)
{
    if (key != Qt::Key_Left && key != Qt::Key_Right && key != Qt::Key_Up && key != Qt::Key_Down)
        return;
    if (m_selectedFeature.isEmpty() || !viewport())
        return;
    const QVariantList features = renderOverlay();
    const QVariantMap selected = feature(features, m_selectedFeature);
    if (selected.value(QStringLiteral("kind")).toString() != QStringLiteral("point"))
        return;
    const QPointF start(selected.value(QStringLiteral("x")).toReal(), selected.value(QStringLiteral("y")).toReal());
    const QString role = roleForPointer(features, m_selectedFeature, start, modifiers);
    if (role.isEmpty() || isClickOperation(role))
        return;
    const qreal amount = (modifiers & Qt::ShiftModifier) ? 10.0 : 1.0;
    QPointF delta;
    if (key == Qt::Key_Left) delta.setX(-amount);
    if (key == Qt::Key_Right) delta.setX(amount);
    if (key == Qt::Key_Up) delta.setY(-amount);
    if (key == Qt::Key_Down) delta.setY(amount);
    setCurrentPointer(start);
    m_dragRole = role;
    m_dragStartScreen = start;
    m_dragStartScript = toScript(start);
    m_dragStartHandle = handleForPointer(features, m_selectedFeature, role, start);
    m_originalText = activeText();
    m_dragging = true;
    updateDragPreview(start + viewport()->scriptDeltaToScreenDelta(delta), modifiers);
    commitOperation();
}

void DragVisualTool::commitOperation()
{
    VisualToolBase::commitOperation();
    clearDragState();
}

void DragVisualTool::cancelOperation()
{
    VisualToolBase::cancelOperation();
    clearDragState();
}

QPointF DragVisualTool::handleForPointer(const QVariantList &features, const QString &featureId,
                                         const QString &, QPointF point) const
{
    const QVariantMap handle = feature(features, featureId);
    return QPointF(handle.value(QStringLiteral("scriptX"), toScript(point).x()).toReal(),
                   handle.value(QStringLiteral("scriptY"), toScript(point).y()).toReal());
}

void DragVisualTool::completeClickOperation(QPointF, int)
{
}

void DragVisualTool::updateDragPreview(QPointF screenPoint, int modifiers)
{
    if (QLineF(screenPoint, m_dragStartScreen).length() < 0.75)
        return;
    if (!hasTextEdit() && !beginTextEdit())
        return;
    m_guides.clear();
    (void)snap(toScript(screenPoint), modifiers);
    previewTextEdit(editedText(screenPoint, modifiers));
}

void DragVisualTool::clearDragState()
{
    m_dragging = false;
    m_dragFeatures.clear();
    m_dragRole.clear();
    m_originalText.clear();
    m_dragStartScreen = {};
    m_dragStartScript = {};
    m_dragStartHandle = {};
    resetToolGesture();
}

CrosshairTool::CrosshairTool(app::DocumentContext *document, VideoViewport *viewport)
    : VisualToolBase(document, viewport)
{
}

QVariantMap CrosshairTool::descriptor() const
{
    return toolDescriptor(QStringLiteral("crosshair"), tr("Crosshair"), QStringLiteral("＋"),
        tr("Inspect script and video pixel coordinates"), QStringLiteral("visual.crosshair"), QStringLiteral("CrosshairTool"));
}

void CrosshairTool::pointerMove(const QPointF &point, int)
{
    setCurrentPointer(point);
    m_pointer = point;
}

void CrosshairTool::pointerDown(const QPointF &point, int modifiers)
{
    pointerMove(point, modifiers);
}

void CrosshairTool::pointerUp(const QPointF &point, int modifiers)
{
    pointerMove(point, modifiers);
}

void CrosshairTool::pointerLeave()
{
    m_pointer = QPointF(-1000.0, -1000.0);
    setCurrentPointer(m_pointer);
}

void CrosshairTool::buildOverlay(QVariantList &features) const
{
    const QPointF script = toScript(m_pointer);
    const QPointF video = viewport()->screenToVideo(m_pointer);
    setCoordinateLabel(QStringLiteral("Script %1 · Video %2 px").arg(
        QStringLiteral("%1, %2").arg(ass::VisualTags::formatNumber(script.x()), ass::VisualTags::formatNumber(script.y())),
        QStringLiteral("%1, %2").arg(ass::VisualTags::formatNumber(video.x()), ass::VisualTags::formatNumber(video.y()))));
    if (!viewport()->displayedVideoRect().contains(m_pointer))
        return;
    features.push_back(QVariantMap{{QStringLiteral("id"), QStringLiteral("crosshair")},
        {QStringLiteral("kind"), QStringLiteral("crosshair")}, {QStringLiteral("x"), m_pointer.x()},
        {QStringLiteral("y"), m_pointer.y()}, {QStringLiteral("color"), QStringLiteral("#ffffffbb")}});
    features.push_back(QVariantMap{{QStringLiteral("id"), QStringLiteral("coordinate-label")},
        {QStringLiteral("kind"), QStringLiteral("label")}, {QStringLiteral("x"), m_pointer.x() + 12},
        {QStringLiteral("y"), m_pointer.y() + 12}, {QStringLiteral("label"), coordinateLabel()}});
}

PositionTool::PositionTool(app::DocumentContext *document, VideoViewport *viewport)
    : DragVisualTool(document, viewport)
{
}

QVariantMap PositionTool::descriptor() const
{
    return toolDescriptor(QStringLiteral("position"), tr("Position"), QStringLiteral("⌖"),
        tr("Place or translate the active subtitle"), QStringLiteral("visual.position"), QStringLiteral("PositionTool"));
}

QVariantList PositionTool::contextOptions() const
{
    QVariantList options;
    for (int alignment = 1; alignment <= 9; ++alignment)
        options.push_back(QVariantMap{{QStringLiteral("id"), QStringLiteral("align-%1").arg(alignment)},
            {QStringLiteral("label"), QString::number(alignment)}, {QStringLiteral("value"), true}});
    return options;
}

void PositionTool::setOption(const QString &optionId, const QVariant &)
{
    if (!document() || !optionId.startsWith(QStringLiteral("align-")))
        return;
    const int alignment = optionId.mid(6).toInt();
    if (alignment < 1 || alignment > 9)
        return;
    const QString original = activeText();
    const ass::Event event = activeEvent();
    const auto style = document()->styleForName(event.style);
    const auto explicitAlignment = ass::VisualTags::number(original, u"an");
    const int oldAlignment = explicitAlignment.value_or(style.alignment);
    const qreal scaleX = ass::VisualTags::number(original, u"fscx").value_or(100.0);
    const qreal scaleY = ass::VisualTags::number(original, u"fscy").value_or(100.0);
    const QSizeF bounds = estimateSubtitleBounds(original, style, scaleX, scaleY);
    const QPointF oldAnchor = effectivePosition(event);
    const auto horizontalOffset = [&bounds](int value) {
        const int column = (value - 1) % 3;
        return column == 0 ? 0.0 : column == 1 ? bounds.width() / 2.0 : bounds.width();
    };
    const auto verticalOffset = [&bounds](int value) {
        const int row = (value - 1) / 3;
        return row == 2 ? 0.0 : row == 1 ? bounds.height() / 2.0 : bounds.height();
    };
    const QPointF topLeft(oldAnchor.x() - horizontalOffset(oldAlignment), oldAnchor.y() - verticalOffset(oldAlignment));
    const QPointF newAnchor(topLeft.x() + horizontalOffset(alignment), topLeft.y() + verticalOffset(alignment));
    const QPointF delta = newAnchor - oldAnchor;
    QString updated = original;
    if (const auto move = ass::VisualTags::move(updated)) {
        auto shifted = *move;
        shifted.start += delta;
        shifted.end += delta;
        updated = ass::VisualTags::setMove(updated, shifted);
    } else if (ass::VisualTags::point(original, u"pos")) {
        updated = ass::VisualTags::setPoint(updated, u"pos", newAnchor);
    } else if (!qFuzzyIsNull(delta.x()) || !qFuzzyIsNull(delta.y())) {
        updated = ass::VisualTags::setPoint(updated, u"pos", newAnchor);
    }
    if (const auto origin = ass::VisualTags::point(updated, u"org"))
        updated = ass::VisualTags::setPoint(updated, u"org", *origin + delta);
    if (alignment != oldAlignment && (explicitAlignment || alignment != style.alignment))
        updated = ass::VisualTags::setNumber(updated, u"an", alignment);
    editTextOnce(updated);
}

void PositionTool::buildOverlay(QVariantList &features) const
{
    const ass::Event event = activeEvent();
    const QString text = event.text;
    if (const auto move = ass::VisualTags::move(text)) {
        addLine(features, QStringLiteral("move-path"), move->start, move->end);
        addPoint(features, QStringLiteral("move-start"), move->start, tr("Start"), QStringLiteral("translate"));
        addPoint(features, QStringLiteral("move-end"), move->end, tr("End"), QStringLiteral("translate"));
        return;
    }
    const QPointF anchor = effectivePosition(event);
    addPoint(features, QStringLiteral("position"), anchor, tr("Position"), QStringLiteral("position"), true);
    const int alignment = ass::VisualTags::number(text, u"an").value_or(document()->styleForName(event.style).alignment);
    const int column = (alignment - 1) % 3;
    if (column == 0)
        addLine(features, QStringLiteral("alignment-guide"), anchor, QPointF(0, anchor.y()));
    else if (column == 2)
        addLine(features, QStringLiteral("alignment-guide"), anchor, QPointF(viewport()->scriptSize().width(), anchor.y()));
}

QString PositionTool::roleForPointer(const QVariantList &features, const QString &featureId, QPointF, int) const
{
    if (featureId.isEmpty())
        return QStringLiteral("position");
    const QVariantMap selected = feature(features, featureId);
    return selected.value(QStringLiteral("role")).toString();
}

QPointF PositionTool::handleForPointer(const QVariantList &features, const QString &featureId,
                                       const QString &role, QPointF point) const
{
    if (role == QStringLiteral("position"))
        return effectivePosition(activeEvent());
    if (role == QStringLiteral("translate")) {
        const auto move = ass::VisualTags::move(activeText());
        if (move)
            return featureId == QStringLiteral("move-start") ? move->start : move->end;
    }
    return DragVisualTool::handleForPointer(features, featureId, role, point);
}

QString PositionTool::editedText(QPointF screenPoint, int modifiers) const
{
    const QPointF delta = toScript(screenPoint) - dragStartScript();
    QString text = originalText();
    if (dragRole() == QStringLiteral("position")) {
        if (const auto move = ass::VisualTags::move(text)) {
            auto translated = *move;
            translated.start += delta;
            translated.end += delta;
            return ass::VisualTags::setMove(text, translated);
        }
        return ass::VisualTags::setPoint(text, u"pos", snap(dragStartHandle() + delta, modifiers));
    }
    const auto move = ass::VisualTags::move(text);
    if (!move)
        return text;
    auto translated = *move;
    const QPointF shift = snap(dragStartHandle() + delta, modifiers) - dragStartHandle();
    if (selectedFeature() == QStringLiteral("move-start"))
        translated.start += shift;
    else
        translated.end += shift;
    return ass::VisualTags::setMove(text, translated);
}

MoveTool::MoveTool(app::DocumentContext *document, VideoViewport *viewport) : DragVisualTool(document, viewport) {}

QVariantMap MoveTool::descriptor() const
{
    return toolDescriptor(QStringLiteral("move"), tr("Move"), QStringLiteral("↗"),
        tr("Edit both endpoints of the subtitle move"), QStringLiteral("visual.move"), QStringLiteral("MoveTool"));
}

QVariantList MoveTool::contextOptions() const
{
    return {QVariantMap{{QStringLiteral("id"), QStringLiteral("capture-a")},
                {QStringLiteral("label"), m_hasMovePointA ? tr("Point A ✓") : tr("Set point A")}, {QStringLiteral("value"), true}},
            QVariantMap{{QStringLiteral("id"), QStringLiteral("capture-b")},
                {QStringLiteral("label"), tr("Set point B")}, {QStringLiteral("value"), true}}};
}

void MoveTool::setOption(const QString &optionId, const QVariant &)
{
    if (!document() || !viewport())
        return;
    if (optionId == QStringLiteral("capture-a")) {
        if (!viewport()->displayedVideoRect().contains(currentPointer()))
            return;
        m_movePointA = toScript(currentPointer());
        m_moveTimeA = document()->media()->displayedFrameStartMs();
        m_hasMovePointA = true;
    } else if (optionId == QStringLiteral("capture-b")) {
        if (!m_hasMovePointA || !viewport()->displayedVideoRect().contains(currentPointer()))
            return;
        const ass::Event event = activeEvent();
        const QPointF pointB = toScript(currentPointer());
        const qreal duration = static_cast<qreal>(std::max<qint64>(0, event.endMs - event.startMs));
        const qreal timeA = std::clamp<qreal>(m_moveTimeA - event.startMs, 0.0, duration);
        const qreal timeB = std::clamp<qreal>(document()->media()->displayedFrameStartMs() - event.startMs, 0.0, duration);
        ass::VisualTags::Move move{m_movePointA, pointB, timeA, timeB};
        if (timeA > timeB) {
            std::swap(move.start, move.end);
            std::swap(move.startMs, move.endMs);
        }
        const QString updated = ass::VisualTags::setMove(
            ass::VisualTags::removeTag(activeText(), u"pos"), move);
        editTextOnce(updated);
        m_hasMovePointA = false;
    }
}

void MoveTool::buildOverlay(QVariantList &features) const
{
    const ass::Event event = activeEvent();
    const auto move = ass::VisualTags::move(event.text);
    const QPointF anchor = effectivePosition(event);
    if (!move) {
        addPoint(features, QStringLiteral("move-origin"), anchor, tr("Click and drag to create a move"), QStringLiteral("create"));
        return;
    }
    addLine(features, QStringLiteral("move-path"), move->start, move->end);
    addPoint(features, QStringLiteral("move-start"), move->start, tr("Start"), QStringLiteral("move-start"));
    addPoint(features, QStringLiteral("move-end"), move->end, tr("End"), QStringLiteral("move-end"));
    const qint64 duration = std::max<qint64>(0, event.endMs - event.startMs);
    const qreal startMs = move->startMs.value_or(0.0);
    const qreal endMs = move->endMs.value_or(static_cast<qreal>(duration));
    const qreal now = document()->media()->displayedFrameStartMs() - event.startMs;
    const qreal amount = endMs <= startMs ? 0.0 : qBound<qreal>(0.0, (now - startMs) / (endMs - startMs), 1.0);
    addPoint(features, QStringLiteral("move-current"), move->start + (move->end - move->start) * amount,
        tr("Current"), {}, true);
    addLine(features, QStringLiteral("move-direction"), move->start, move->start + (move->end - move->start) * 0.25);
}

QString MoveTool::roleForPointer(const QVariantList &features, const QString &featureId, QPointF, int) const
{
    if (featureId.isEmpty())
        return QStringLiteral("create");
    return feature(features, featureId).value(QStringLiteral("role")).toString();
}

QPointF MoveTool::handleForPointer(const QVariantList &features, const QString &featureId,
                                   const QString &role, QPointF point) const
{
    if (const auto move = ass::VisualTags::move(activeText())) {
        if (role == QStringLiteral("move-start")) return move->start;
        if (role == QStringLiteral("move-end")) return move->end;
    }
    return DragVisualTool::handleForPointer(features, featureId, role, point);
}

QString MoveTool::editedText(QPointF screenPoint, int) const
{
    const QPointF end = toScript(screenPoint);
    if (dragRole() == QStringLiteral("create")) {
        const ass::Event event = activeEvent();
        const qreal duration = static_cast<qreal>(std::max<qint64>(0, event.endMs - event.startMs));
        const ass::VisualTags::Move move{dragStartScript(), end, 0.0, duration};
        return ass::VisualTags::setMove(ass::VisualTags::removeTag(originalText(), u"pos"), move);
    }
    auto move = ass::VisualTags::move(originalText());
    if (!move)
        return originalText();
    if (dragRole() == QStringLiteral("move-start")) move->start = end;
    else if (dragRole() == QStringLiteral("move-end")) move->end = end;
    return ass::VisualTags::setMove(originalText(), *move);
}

RotateZTool::RotateZTool(app::DocumentContext *document, VideoViewport *viewport) : DragVisualTool(document, viewport) {}

QVariantMap RotateZTool::descriptor() const
{
    return toolDescriptor(QStringLiteral("rotate-z"), tr("Rotate Z"), QStringLiteral("⟳"),
        tr("Rotate around the subtitle origin"), QStringLiteral("visual.rotateZ"), QStringLiteral("RotateZTool"));
}

QVariantList RotateZTool::contextOptions() const
{
    return {QVariantMap{{QStringLiteral("id"), QStringLiteral("angle-a")},
                {QStringLiteral("label"), m_hasRotationPointA ? tr("Angle point A ✓") : tr("Pick angle point A")},
                {QStringLiteral("value"), true}},
            QVariantMap{{QStringLiteral("id"), QStringLiteral("angle-b")},
                {QStringLiteral("label"), tr("Pick angle point B")}, {QStringLiteral("value"), true}}};
}

void RotateZTool::setOption(const QString &optionId, const QVariant &)
{
    if (!document() || !viewport() || !viewport()->displayedVideoRect().contains(currentPointer()))
        return;
    if (optionId == QStringLiteral("angle-a")) {
        m_rotationPointA = toScript(currentPointer());
        m_hasRotationPointA = true;
    } else if (optionId == QStringLiteral("angle-b") && m_hasRotationPointA) {
        const QPointF pointB = toScript(currentPointer());
        qreal angle = qRadiansToDegrees(std::atan2(pointB.y() - m_rotationPointA.y(), pointB.x() - m_rotationPointA.x()));
        while (angle > 180.0) angle -= 360.0;
        while (angle < -180.0) angle += 360.0;
        editTextOnce(ass::VisualTags::setNumber(activeText(), u"frz", angle));
        m_hasRotationPointA = false;
    }
}

void RotateZTool::buildOverlay(QVariantList &features) const
{
    const ass::Event event = activeEvent();
    const QPointF anchor = effectivePosition(event);
    const QPointF origin = ass::VisualTags::point(event.text, u"org").value_or(anchor);
    const qreal angle = ass::VisualTags::number(event.text, u"frz").value_or(0.0);
    const QPointF center = toScreen(origin);
    constexpr qreal radius = 46.0;
    features.push_back(QVariantMap{{QStringLiteral("id"), QStringLiteral("rotation-ring")},
        {QStringLiteral("kind"), QStringLiteral("ring")}, {QStringLiteral("x"), center.x()},
        {QStringLiteral("y"), center.y()}, {QStringLiteral("radius"), radius}});
    const qreal radians = qDegreesToRadians(angle);
    const QPointF ray = toScript(center + QPointF(std::cos(radians) * radius, std::sin(radians) * radius));
    addLine(features, QStringLiteral("rotation-ray"), origin, ray);
    addPoint(features, QStringLiteral("rotation-origin"), origin, tr("Origin"),
        ass::VisualTags::point(event.text, u"org") ? QStringLiteral("origin") : QString{});
    addPoint(features, QStringLiteral("rotation-handle"), ray,
        QStringLiteral("%1°").arg(ass::VisualTags::formatNumber(angle)), QStringLiteral("rotation"));
}

QString RotateZTool::roleForPointer(const QVariantList &features, const QString &featureId, QPointF, int) const
{
    return feature(features, featureId).value(QStringLiteral("role")).toString();
}

QPointF RotateZTool::handleForPointer(const QVariantList &features, const QString &featureId,
                                      const QString &role, QPointF point) const
{
    if (role == QStringLiteral("origin"))
        return ass::VisualTags::point(activeText(), u"org").value_or(effectivePosition(activeEvent()));
    return DragVisualTool::handleForPointer(features, featureId, role, point);
}

QString RotateZTool::editedText(QPointF screenPoint, int modifiers) const
{
    if (dragRole() == QStringLiteral("origin")) {
        const QPointF delta = toScript(screenPoint) - dragStartScript();
        return ass::VisualTags::setPoint(originalText(), u"org", snap(dragStartHandle() + delta, modifiers));
    }
    const QPointF origin = ass::VisualTags::point(originalText(), u"org").value_or(effectivePosition(activeEvent()));
    const QPointF a = dragStartScript() - origin;
    const QPointF b = toScript(screenPoint) - origin;
    const qreal initialAngle = std::atan2(a.y(), a.x());
    const qreal currentAngle = std::atan2(b.y(), b.x());
    qreal value = ass::VisualTags::number(originalText(), u"frz").value_or(0.0)
        + qRadiansToDegrees(currentAngle - initialAngle);
    while (value > 180.0) value -= 360.0;
    while (value < -180.0) value += 360.0;
    return ass::VisualTags::setNumber(originalText(), u"frz", value);
}

RotateXYTool::RotateXYTool(app::DocumentContext *document, VideoViewport *viewport) : DragVisualTool(document, viewport) {}

QVariantMap RotateXYTool::descriptor() const
{
    return toolDescriptor(QStringLiteral("rotate-xy"), tr("Rotate X/Y"), QStringLiteral("⤢"),
        tr("Edit X and Y rotations"), QStringLiteral("visual.rotateXY"), QStringLiteral("RotateXYTool"));
}

QVariantList RotateXYTool::contextOptions() const
{
    return {QVariantMap{{QStringLiteral("id"), QStringLiteral("x")},
                {QStringLiteral("label"), m_axisFilter == QStringLiteral("x") ? tr("X axis ✓") : tr("X axis")},
                {QStringLiteral("value"), true}},
            QVariantMap{{QStringLiteral("id"), QStringLiteral("y")},
                {QStringLiteral("label"), m_axisFilter == QStringLiteral("y") ? tr("Y axis ✓") : tr("Y axis")},
                {QStringLiteral("value"), true}}};
}

void RotateXYTool::setOption(const QString &optionId, const QVariant &)
{
    if (optionId == QStringLiteral("x") || optionId == QStringLiteral("y"))
        m_axisFilter = optionId;
}

void RotateXYTool::buildOverlay(QVariantList &features) const
{
    const QPointF center = toScreen(effectivePosition(activeEvent()));
    const qreal frx = ass::VisualTags::number(activeText(), u"frx").value_or(0.0);
    const qreal fry = ass::VisualTags::number(activeText(), u"fry").value_or(0.0);
    features.push_back(QVariantMap{{QStringLiteral("id"), QStringLiteral("axis-x")},
        {QStringLiteral("kind"), QStringLiteral("screen-line")}, {QStringLiteral("x"), center.x()},
        {QStringLiteral("y"), center.y()}, {QStringLiteral("x2"), center.x()},
        {QStringLiteral("y2"), center.y() - 46}});
    features.push_back(QVariantMap{{QStringLiteral("id"), QStringLiteral("axis-y")},
        {QStringLiteral("kind"), QStringLiteral("screen-line")}, {QStringLiteral("x"), center.x()},
        {QStringLiteral("y"), center.y()}, {QStringLiteral("x2"), center.x() + 46},
        {QStringLiteral("y2"), center.y()}});
    addScreenPoint(features, QStringLiteral("rotate-x"), center + QPointF(0, -54),
        QStringLiteral("X %1°").arg(ass::VisualTags::formatNumber(frx)), QStringLiteral("rotate-x"));
    addScreenPoint(features, QStringLiteral("rotate-y"), center + QPointF(54, 0),
        QStringLiteral("Y %1°").arg(ass::VisualTags::formatNumber(fry)), QStringLiteral("rotate-y"));
}

QString RotateXYTool::roleForPointer(const QVariantList &features, const QString &featureId, QPointF, int) const
{
    return feature(features, featureId).value(QStringLiteral("role")).toString();
}

QString RotateXYTool::editedText(QPointF screenPoint, int) const
{
    const QString role = dragRole();
    if ((m_axisFilter == QStringLiteral("x") && role == QStringLiteral("rotate-y"))
        || (m_axisFilter == QStringLiteral("y") && role == QStringLiteral("rotate-x")))
        return originalText();
    const qreal change = role == QStringLiteral("rotate-x")
        ? (dragStartScreen().y() - screenPoint.y()) * 0.4
        : (screenPoint.x() - dragStartScreen().x()) * 0.4;
    const QStringView tag = role == QStringLiteral("rotate-x") ? u"frx" : u"fry";
    return ass::VisualTags::setNumber(originalText(), tag,
        ass::VisualTags::number(originalText(), tag).value_or(0.0) + change);
}

ScaleTool::ScaleTool(app::DocumentContext *document, VideoViewport *viewport) : DragVisualTool(document, viewport) {}

QVariantMap ScaleTool::descriptor() const
{
    return toolDescriptor(QStringLiteral("scale"), tr("Scale"), QStringLiteral("□"),
        tr("Scale width and height"), QStringLiteral("visual.scale"), QStringLiteral("ScaleTool"));
}

QVariantList ScaleTool::contextOptions() const
{
    QVariantList options;
    for (const auto &[id, label] : QVector<QPair<QString, QString>>{{QStringLiteral("both"), tr("Both")},
             {QStringLiteral("x"), tr("Width")}, {QStringLiteral("y"), tr("Height")}})
        options.push_back(QVariantMap{{QStringLiteral("id"), id},
            {QStringLiteral("label"), id == m_scaleAxis ? label + QStringLiteral(" ✓") : label}, {QStringLiteral("value"), true}});
    options.push_back(QVariantMap{{QStringLiteral("id"), QStringLiteral("fit-rect")},
        {QStringLiteral("label"), m_scaleRectMode ? tr("Draw target ✓") : tr("Fit to rectangle")}, {QStringLiteral("value"), true}});
    return options;
}

void ScaleTool::setOption(const QString &optionId, const QVariant &)
{
    if (optionId == QStringLiteral("fit-rect")) m_scaleRectMode = !m_scaleRectMode;
    else if (optionId == QStringLiteral("x") || optionId == QStringLiteral("y") || optionId == QStringLiteral("both")) m_scaleAxis = optionId;
}

void ScaleTool::buildOverlay(QVariantList &features) const
{
    const ass::Event event = activeEvent();
    const QString text = event.text;
    const qreal sx = ass::VisualTags::number(text, u"fscx").value_or(100.0);
    const qreal sy = ass::VisualTags::number(text, u"fscy").value_or(100.0);
    const auto style = document()->styleForName(event.style);
    const QSizeF bounds = estimateSubtitleBounds(text, style, sx, sy);
    const qreal width = std::max<qreal>(48.0, bounds.width());
    const qreal height = std::max<qreal>(24.0, bounds.height());
    const int align = ass::VisualTags::number(text, u"an").value_or(style.alignment);
    const QPointF anchor = effectivePosition(event);
    const qreal left = align % 3 == 1 ? anchor.x() : align % 3 == 2 ? anchor.x() - width / 2 : anchor.x() - width;
    const qreal top = align >= 7 ? anchor.y() : align >= 4 ? anchor.y() - height / 2 : anchor.y() - height;
    addRect(features, QStringLiteral("scale-bounds"), QRectF(left, top, width, height));
    addPoint(features, QStringLiteral("scale-x"), QPointF(left + width, top + height / 2),
        QStringLiteral("%1% W").arg(ass::VisualTags::formatNumber(sx)), QStringLiteral("scale-x"));
    addPoint(features, QStringLiteral("scale-y"), QPointF(left + width / 2, top),
        QStringLiteral("%1% H").arg(ass::VisualTags::formatNumber(sy)), QStringLiteral("scale-y"));
    addPoint(features, QStringLiteral("scale-both"), QPointF(left + width, top),
        QStringLiteral("%1 × %2").arg(ass::VisualTags::formatNumber(sx), ass::VisualTags::formatNumber(sy)), QStringLiteral("scale-both"));
    if (isDragging() && dragRole() == QStringLiteral("scale-target-rect"))
        addRect(features, QStringLiteral("scale-target"), QRectF(dragStartScript(), toScript(currentPointer())).normalized());
}

QString ScaleTool::roleForPointer(const QVariantList &features, const QString &featureId, QPointF, int) const
{
    if (m_scaleRectMode)
        return QStringLiteral("scale-target-rect");
    return feature(features, featureId).value(QStringLiteral("role")).toString();
}

QPointF ScaleTool::handleForPointer(const QVariantList &features, const QString &featureId,
                                    const QString &role, QPointF point) const
{
    if (role == QStringLiteral("scale-target-rect"))
        return toScript(point);
    return DragVisualTool::handleForPointer(features, featureId, role, point);
}

QString ScaleTool::editedText(QPointF screenPoint, int modifiers) const
{
    QString text = originalText();
    const qreal oldX = ass::VisualTags::number(text, u"fscx").value_or(100.0);
    const qreal oldY = ass::VisualTags::number(text, u"fscy").value_or(100.0);
    const qreal factorX = std::exp((screenPoint.x() - dragStartScreen().x()) / 130.0);
    const qreal factorY = std::exp((dragStartScreen().y() - screenPoint.y()) / 130.0);
    const bool lock = (modifiers & Qt::ShiftModifier) != 0;
    if (dragRole() == QStringLiteral("scale-x")) {
        if (m_scaleAxis == QStringLiteral("y")) return text;
        text = ass::VisualTags::setNumber(text, u"fscx", qBound<qreal>(1.0, oldX * factorX, 1000.0));
        if (lock) text = ass::VisualTags::setNumber(text, u"fscy", qBound<qreal>(1.0, oldY * factorX, 1000.0));
    } else if (dragRole() == QStringLiteral("scale-y")) {
        if (m_scaleAxis == QStringLiteral("x")) return text;
        text = ass::VisualTags::setNumber(text, u"fscy", qBound<qreal>(1.0, oldY * factorY, 1000.0));
        if (lock) text = ass::VisualTags::setNumber(text, u"fscx", qBound<qreal>(1.0, oldX * factorY, 1000.0));
    } else {
        if (m_scaleAxis != QStringLiteral("y"))
            text = ass::VisualTags::setNumber(text, u"fscx", qBound<qreal>(1.0, oldX * (lock ? std::sqrt(factorX * factorY) : factorX), 1000.0));
        if (m_scaleAxis != QStringLiteral("x"))
            text = ass::VisualTags::setNumber(text, u"fscy", qBound<qreal>(1.0, oldY * (lock ? std::sqrt(factorX * factorY) : factorY), 1000.0));
    }
    return text;
}

bool ScaleTool::isClickOperation(QStringView role) const
{
    return role == u"scale-target-rect";
}

void ScaleTool::completeClickOperation(QPointF screenPoint, int modifiers)
{
    const QRectF target = QRectF(dragStartScript(), toScript(screenPoint)).normalized();
    const bool aspectLock = (modifiers & Qt::ShiftModifier) != 0;
    const bool needsWidth = m_scaleAxis != QStringLiteral("y") || aspectLock;
    const bool needsHeight = m_scaleAxis != QStringLiteral("x") || aspectLock;
    if ((needsWidth && target.width() < 1.0) || (needsHeight && target.height() < 1.0)) {
        m_scaleRectMode = false;
        return;
    }
    const ass::Event event = activeEvent();
    const QString original = event.text;
    const auto style = document()->styleForName(event.style);
    const qreal oldX = ass::VisualTags::number(original, u"fscx").value_or(100.0);
    const qreal oldY = ass::VisualTags::number(original, u"fscy").value_or(100.0);
    const QSizeF currentBounds = estimateSubtitleBounds(original, style, oldX, oldY);
    const qreal factorX = target.width() / currentBounds.width();
    const qreal factorY = target.height() / currentBounds.height();
    const qreal uniform = std::min(factorX, factorY);
    QString updated = original;
    if (m_scaleAxis != QStringLiteral("y"))
        updated = ass::VisualTags::setNumber(updated, u"fscx", qBound<qreal>(1.0, oldX * (aspectLock ? uniform : factorX), 1000.0));
    if (m_scaleAxis != QStringLiteral("x"))
        updated = ass::VisualTags::setNumber(updated, u"fscy", qBound<qreal>(1.0, oldY * (aspectLock ? uniform : factorY), 1000.0));
    editTextOnce(updated);
    m_scaleRectMode = false;
}

RectClipTool::RectClipTool(app::DocumentContext *document, VideoViewport *viewport) : DragVisualTool(document, viewport) {}

QVariantMap RectClipTool::descriptor() const
{
    return toolDescriptor(QStringLiteral("clip"), tr("Rectangular Clip"), QStringLiteral("▭"),
        tr("Create or edit a rectangular clip"), QStringLiteral("visual.clip"), QStringLiteral("RectClipTool"));
}

QVariantList RectClipTool::contextOptions() const
{
    const auto clip = ass::VisualTags::clip(activeText());
    return {QVariantMap{{QStringLiteral("id"), QStringLiteral("invert")},
        {QStringLiteral("label"), clip && clip->inverse ? tr("Inverse ✓") : tr("Invert")},
        {QStringLiteral("value"), true}}};
}

void RectClipTool::setOption(const QString &optionId, const QVariant &)
{
    if (optionId != QStringLiteral("invert"))
        return;
    auto clip = ass::VisualTags::clip(activeText());
    if (!clip)
        return;
    clip->inverse = !clip->inverse;
    editTextOnce(ass::VisualTags::setClip(activeText(), *clip));
}

void RectClipTool::buildOverlay(QVariantList &features) const
{
    const QPointF anchor = effectivePosition(activeEvent());
    const auto clip = ass::VisualTags::clip(activeText());
    if (clip && clip->rectangle) {
        addRect(features, QStringLiteral("clip-bounds"), clip->bounds);
        const QRectF rect = clip->bounds.normalized();
        addPoint(features, QStringLiteral("clip-nw"), rect.topLeft(), {}, QStringLiteral("clip-nw"));
        addPoint(features, QStringLiteral("clip-ne"), rect.topRight(), {}, QStringLiteral("clip-ne"));
        addPoint(features, QStringLiteral("clip-sw"), rect.bottomLeft(), {}, QStringLiteral("clip-sw"));
        addPoint(features, QStringLiteral("clip-se"), rect.bottomRight(), {}, QStringLiteral("clip-se"));
        addPoint(features, QStringLiteral("clip-n"), QPointF(rect.center().x(), rect.top()), {}, QStringLiteral("clip-n"));
        addPoint(features, QStringLiteral("clip-s"), QPointF(rect.center().x(), rect.bottom()), {}, QStringLiteral("clip-s"));
        addPoint(features, QStringLiteral("clip-w"), QPointF(rect.left(), rect.center().y()), {}, QStringLiteral("clip-w"));
        addPoint(features, QStringLiteral("clip-e"), QPointF(rect.right(), rect.center().y()), {}, QStringLiteral("clip-e"));
        addPoint(features, QStringLiteral("clip-body"), rect.center(), clip->inverse ? tr("Inverse clip") : tr("Clip"), QStringLiteral("clip-body"));
    } else if (!clip) {
        addPoint(features, QStringLiteral("clip-create"), toScript(currentPointer()), tr("Drag to create clip"), QStringLiteral("clip-create"));
    } else {
        addPoint(features, QStringLiteral("clip-read-only"), anchor, tr("Use Vector Clip to edit this path"));
    }
}

QString RectClipTool::roleForPointer(const QVariantList &features, const QString &featureId, QPointF point, int) const
{
    const auto clip = ass::VisualTags::clip(activeText());
    if (clip && !clip->rectangle)
        return {};
    if (!featureId.isEmpty())
        return feature(features, featureId).value(QStringLiteral("role")).toString();
    for (const QVariant &entry : features) {
        const QVariantMap item = entry.toMap();
        if (item.value(QStringLiteral("id")).toString() == QStringLiteral("clip-bounds")
            && QRectF(item.value(QStringLiteral("x")).toReal(), item.value(QStringLiteral("y")).toReal(),
                      item.value(QStringLiteral("width")).toReal(), item.value(QStringLiteral("height")).toReal()).contains(point))
            return QStringLiteral("clip-body");
    }
    return QStringLiteral("clip-create");
}

QPointF RectClipTool::handleForPointer(const QVariantList &features, const QString &featureId,
                                       const QString &role, QPointF point) const
{
    if (role == QStringLiteral("clip-body"))
        return ass::VisualTags::clip(activeText())->bounds.normalized().center();
    return DragVisualTool::handleForPointer(features, featureId, role, point);
}

QString RectClipTool::editedText(QPointF screenPoint, int modifiers) const
{
    Q_UNUSED(modifiers);
    const QPointF script = toScript(screenPoint);
    const QPointF delta = script - dragStartScript();
    QString text = originalText();
    auto clip = ass::VisualTags::clip(text);
    if (clip && !clip->rectangle)
        return text;
    if (!clip) {
        ass::VisualTags::Clip value;
        value.rectangle = true;
        value.bounds = QRectF(dragStartScript(), script).normalized();
        return ass::VisualTags::setClip(text, value);
    }
    QRectF rect = clip->bounds.normalized();
    const QString role = dragRole();
    if (role == QStringLiteral("clip-body")) rect.translate(delta);
    else if (role == QStringLiteral("clip-nw")) rect.setTopLeft(rect.topLeft() + delta);
    else if (role == QStringLiteral("clip-ne")) rect.setTopRight(rect.topRight() + delta);
    else if (role == QStringLiteral("clip-sw")) rect.setBottomLeft(rect.bottomLeft() + delta);
    else if (role == QStringLiteral("clip-se")) rect.setBottomRight(rect.bottomRight() + delta);
    else if (role == QStringLiteral("clip-n")) rect.setTop(rect.top() + delta.y());
    else if (role == QStringLiteral("clip-s")) rect.setBottom(rect.bottom() + delta.y());
    else if (role == QStringLiteral("clip-w")) rect.setLeft(rect.left() + delta.x());
    else if (role == QStringLiteral("clip-e")) rect.setRight(rect.right() + delta.x());
    else rect = QRectF(dragStartScript(), script).normalized();
    clip->bounds = rect.normalized();
    return ass::VisualTags::setClip(text, *clip);
}

void registerBuiltInVisualTools(VisualToolManager &manager)
{
    manager.registerToolFactory(CrosshairTool(nullptr, nullptr).descriptor(),
        [](app::DocumentContext *document, VideoViewport *viewport) { return std::make_shared<CrosshairTool>(document, viewport); });
    manager.registerToolFactory(PositionTool(nullptr, nullptr).descriptor(),
        [](app::DocumentContext *document, VideoViewport *viewport) { return std::make_shared<PositionTool>(document, viewport); });
    manager.registerToolFactory(MoveTool(nullptr, nullptr).descriptor(),
        [](app::DocumentContext *document, VideoViewport *viewport) { return std::make_shared<MoveTool>(document, viewport); });
    manager.registerToolFactory(RotateZTool(nullptr, nullptr).descriptor(),
        [](app::DocumentContext *document, VideoViewport *viewport) { return std::make_shared<RotateZTool>(document, viewport); });
    manager.registerToolFactory(RotateXYTool(nullptr, nullptr).descriptor(),
        [](app::DocumentContext *document, VideoViewport *viewport) { return std::make_shared<RotateXYTool>(document, viewport); });
    manager.registerToolFactory(ScaleTool(nullptr, nullptr).descriptor(),
        [](app::DocumentContext *document, VideoViewport *viewport) { return std::make_shared<ScaleTool>(document, viewport); });
    manager.registerToolFactory(RectClipTool(nullptr, nullptr).descriptor(),
        [](app::DocumentContext *document, VideoViewport *viewport) { return std::make_shared<RectClipTool>(document, viewport); });
}

} // namespace yoake::ui
