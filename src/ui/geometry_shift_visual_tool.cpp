#include "ui/geometry_shift_visual_tool.h"

#include "app/document_context.h"
#include "ass/vector_path.h"
#include "ass/visual_tags.h"
#include "models/subtitle_model.h"
#include "ui/visual_tool_manager.h"

#include <QtCore/QLineF>
#include <QtCore/QUuid>
#include <QtCore/QtMath>

#include <algorithm>
#include <cmath>
#include <utility>

namespace yoake::ui {
namespace {

qreal drawingFactor(int scale)
{
    return std::pow(2.0, std::clamp(scale, 1, 100) - 1);
}

QVariantMap shiftDescriptor()
{
    return {{QStringLiteral("id"), QStringLiteral("shift")}, {QStringLiteral("name"), QObject::tr("Geometry Shift")},
        {QStringLiteral("icon"), QStringLiteral("✥")}, {QStringLiteral("tooltip"), QObject::tr("Translate the line's spatial tags together")},
        {QStringLiteral("commandId"), QStringLiteral("visual.shift")}, {QStringLiteral("factory"), QStringLiteral("GeometryShiftTool")},
        {QStringLiteral("capabilities"), QStringList{QStringLiteral("ass-override"), QStringLiteral("script-geometry")}}};
}

} // namespace

GeometryShiftTool::GeometryShiftTool(app::DocumentContext *document, VideoViewport *viewport)
    : VisualToolBase(document, viewport)
{
}

QVariantMap GeometryShiftTool::descriptor() const { return shiftDescriptor(); }

QVariantList GeometryShiftTool::contextOptions() const
{
    QVariantList options;
    const QString text = activeText();
    const auto add = [this, &options](const QString &id, const QString &label) {
        const bool enabled = m_components.value(id, true);
        options.push_back(QVariantMap{{QStringLiteral("id"), id},
            {QStringLiteral("label"), label + (enabled ? QStringLiteral(" ✓") : QString{})},
            {QStringLiteral("value"), enabled}});
    };
    const auto move = ass::VisualTags::move(text);
    if (move) {
        add(QStringLiteral("shift-move-start"), tr("Move start"));
        add(QStringLiteral("shift-move-end"), tr("Move end"));
    }
    if (!move || ass::VisualTags::point(text, u"pos"))
        add(QStringLiteral("shift-pos"), tr("Position"));
    if (ass::VisualTags::point(text, u"org"))
        add(QStringLiteral("shift-org"), tr("Origin"));
    if (ass::VisualTags::clip(text))
        add(QStringLiteral("shift-clip"), tr("Clip"));
    if (ass::VisualTags::drawing(text))
        add(QStringLiteral("shift-drawing"), tr("Drawing"));
    return options;
}

void GeometryShiftTool::setOption(const QString &id, const QVariant &value)
{
    if (id.startsWith(QStringLiteral("shift-")))
        m_components.insert(id, value.toBool());
}

void GeometryShiftTool::buildOverlay(QVariantList &features) const
{
    const ass::Event event = activeEvent();
    const QPointF anchor = effectivePosition(event);
    addPoint(features, QStringLiteral("shift-origin"), anchor,
        tr("Drag to shift position, move, origin, and clips"), QStringLiteral("shift"));
    if (const auto move = ass::VisualTags::move(event.text))
        addLine(features, QStringLiteral("move-path"), move->start, move->end);
    if (const auto clip = ass::VisualTags::clip(event.text); clip && clip->rectangle)
        addRect(features, QStringLiteral("clip-bounds"), clip->bounds);
}

void GeometryShiftTool::pointerDown(const QPointF &point, int)
{
    clearSnapGuides();
    setCurrentPointer(point);
    if (!document() || !viewport() || hitPoint(renderOverlay(), point).isEmpty())
        return;
    const ass::Event active = activeEvent();
    m_events.clear();
    m_eventIds.clear();
    const auto selection = document()->lines()->selectionSnapshot();
    for (int row = 0; row < document()->lines()->rowCount(); ++row) {
        const ass::Event *event = document()->lines()->eventAt(row);
        if (!event || !selection.selectedIds.contains(event->id))
            continue;
        m_events.push_back(*event);
        m_eventIds.push_back(event->id.toString(QUuid::WithoutBraces));
    }
    if (m_events.isEmpty()) {
        m_events.push_back(active);
        m_eventIds.push_back(active.id.toString(QUuid::WithoutBraces));
    }
    m_startScreen = point;
    m_startScript = toScript(point);
    m_anchor = effectivePosition(active);
    m_dragging = true;
}

void GeometryShiftTool::pointerMove(const QPointF &point, int modifiers)
{
    setCurrentPointer(point);
    if (!m_dragging || QLineF(point, m_startScreen).length() < 0.75)
        return;
    if (!hasTextEdit()) {
        const bool grouped = m_eventIds.size() > 1;
        if (!(grouped ? beginTextEditGroup(m_eventIds) : beginTextEdit()))
            return;
    }
    const QPointF delta = toScript(point) - m_startScript;
    const QPointF shiftedAnchor = snap(m_anchor + delta, modifiers);
    const QPointF shift = shiftedAnchor - m_anchor;
    if (m_eventIds.size() > 1) {
        QVariantMap edits;
        for (const ass::Event &event : std::as_const(m_events))
            edits.insert(event.id.toString(QUuid::WithoutBraces), shiftedText(event, shift));
        previewTextEditGroup(edits);
    } else {
        previewTextEdit(shiftedText(m_events.front(), shift));
    }
}

void GeometryShiftTool::pointerUp(const QPointF &point, int modifiers)
{
    pointerMove(point, modifiers);
    if (!m_dragging)
        return;
    commitOperation();
    clearGesture();
}

void GeometryShiftTool::keyDown(int key, int modifiers)
{
    if (key == Qt::Key_Escape) {
        cancelOperation();
        return;
    }
    if (!m_dragging && (key == Qt::Key_Left || key == Qt::Key_Right || key == Qt::Key_Up || key == Qt::Key_Down)) {
        const qreal step = (modifiers & Qt::ShiftModifier) ? 10.0 : 1.0;
        QPointF delta;
        if (key == Qt::Key_Left) delta.setX(-step);
        if (key == Qt::Key_Right) delta.setX(step);
        if (key == Qt::Key_Up) delta.setY(-step);
        if (key == Qt::Key_Down) delta.setY(step);
        const ass::Event event = activeEvent();
        if (beginTextEdit()) {
            previewTextEdit(shiftedText(event, delta));
            commitOperation();
        }
    }
}

void GeometryShiftTool::cancelOperation()
{
    VisualToolBase::cancelOperation();
    clearGesture();
}

void GeometryShiftTool::commitOperation()
{
    VisualToolBase::commitOperation();
    clearGesture();
}

QString GeometryShiftTool::shiftedText(const ass::Event &event, QPointF shift) const
{
    QString text = event.text;
    const auto move = ass::VisualTags::move(text);
    if (move) {
        auto edited = *move;
        if (m_components.value(QStringLiteral("shift-move-start"), true)) edited.start += shift;
        if (m_components.value(QStringLiteral("shift-move-end"), true)) edited.end += shift;
        text = ass::VisualTags::setMove(text, edited);
    }
    const auto position = ass::VisualTags::point(text, u"pos");
    if ((!move || position) && m_components.value(QStringLiteral("shift-pos"), true))
        text = ass::VisualTags::setPoint(text, u"pos", position.value_or(effectivePosition(event)) + shift);
    if (const auto origin = ass::VisualTags::point(text, u"org"); origin
        && m_components.value(QStringLiteral("shift-org"), true))
        text = ass::VisualTags::setPoint(text, u"org", *origin + shift);
    if (auto clip = ass::VisualTags::clip(text); clip && m_components.value(QStringLiteral("shift-clip"), true)) {
        if (clip->rectangle) {
            clip->bounds.translate(shift);
            text = ass::VisualTags::setClip(text, *clip);
        } else if (clip->pathEditable) {
            if (auto path = ass::VectorPath::parse(clip->path, clip->drawingScale);
                path && path->translate(shift / drawingFactor(clip->drawingScale))) {
                clip->path = path->serialize();
                text = ass::VisualTags::setClip(text, *clip);
            }
        }
    }
    if (const auto drawing = ass::VisualTags::drawing(text); drawing && drawing->pathEditable
        && m_components.value(QStringLiteral("shift-drawing"), true)) {
        if (auto path = ass::VectorPath::parse(drawing->path, drawing->drawingScale);
            path && path->translate(shift / drawingFactor(drawing->drawingScale)))
            text = ass::VisualTags::setDrawingPath(text, path->serialize(), drawing->drawingScale);
    }
    return text;
}

void GeometryShiftTool::clearGesture()
{
    m_events.clear();
    m_eventIds.clear();
    m_dragging = false;
    m_startScreen = {};
    m_startScript = {};
    m_anchor = {};
}

void registerGeometryShiftTool(VisualToolManager &manager)
{
    manager.registerToolFactory(shiftDescriptor(),
        [](app::DocumentContext *document, VideoViewport *viewport) {
            return std::make_shared<GeometryShiftTool>(document, viewport);
        });
}

} // namespace yoake::ui
