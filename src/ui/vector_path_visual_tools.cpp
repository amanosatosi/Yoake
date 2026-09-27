#include "ui/vector_path_visual_tools.h"

#include "app/document_context.h"
#include "ass/vector_path.h"
#include "ass/visual_tags.h"
#include "ui/video_viewport.h"
#include "ui/visual_tool_manager.h"

#include <QtCore/QLineF>
#include <QtCore/QtMath>

#include <algorithm>
#include <cmath>
#include <functional>
#include <utility>

namespace yoake::ui {
namespace {

int nodeIndex(const QString &id)
{
    if (!id.startsWith(QStringLiteral("vector-")) || id.startsWith(QStringLiteral("vector-segment-"))
        || id.startsWith(QStringLiteral("vector-curve-")))
        return -1;
    bool ok = false;
    const int value = id.mid(7).toInt(&ok);
    return ok ? value : -1;
}

int segmentIndex(const QString &id)
{
    QString suffix;
    if (id.startsWith(QStringLiteral("vector-segment-"))) suffix = id.mid(15);
    else if (id.startsWith(QStringLiteral("vector-curve-"))) suffix = id.mid(13).section(u'-', 0, 0);
    else return -1;
    bool ok = false;
    const int value = suffix.toInt(&ok);
    return ok ? value : -1;
}

qreal pathScaleFactor(int scale)
{
    return std::pow(2.0, std::clamp(scale, 1, 100) - 1);
}

qreal distanceToSegment(QPointF point, QPointF start, QPointF end, QPointF *projection = nullptr)
{
    const QPointF delta = end - start;
    const qreal lengthSquared = QPointF::dotProduct(delta, delta);
    const qreal t = lengthSquared < 0.0001 ? 0.0
        : qBound<qreal>(0.0, QPointF::dotProduct(point - start, delta) / lengthSquared, 1.0);
    const QPointF nearest = start + delta * t;
    if (projection) *projection = nearest;
    return QLineF(point, nearest).length();
}

QVector<QPointF> simplify(const QVector<QPointF> &points, qreal tolerance)
{
    if (points.size() <= 2) return points;
    QVector<bool> keep(points.size(), false);
    keep[0] = keep[points.size() - 1] = true;
    std::function<void(int, int)> recurse = [&](int first, int last) {
        qreal maximum = tolerance;
        int selected = -1;
        const QLineF line(points[first], points[last]);
        for (int i = first + 1; i < last; ++i) {
            const qreal distance = line.length() < 0.001 ? QLineF(points[i], points[first]).length()
                : std::abs((points[last].x() - points[first].x()) * (points[first].y() - points[i].y())
                    - (points[first].x() - points[i].x()) * (points[last].y() - points[first].y())) / line.length();
            if (distance > maximum) { maximum = distance; selected = i; }
        }
        if (selected >= 0) {
            keep[selected] = true;
            recurse(first, selected);
            recurse(selected, last);
        }
    };
    recurse(0, points.size() - 1);
    QVector<QPointF> result;
    for (qsizetype i = 0; i < points.size(); ++i)
        if (keep.at(i)) result.push_back(points.at(i));
    return result;
}

QVariantMap pathDescriptor(QString id, QString name, QString icon, QString tip, QString command, QString factory)
{
    return {{QStringLiteral("id"), std::move(id)}, {QStringLiteral("name"), std::move(name)},
        {QStringLiteral("icon"), std::move(icon)}, {QStringLiteral("tooltip"), std::move(tip)},
        {QStringLiteral("commandId"), std::move(command)}, {QStringLiteral("factory"), std::move(factory)},
        {QStringLiteral("capabilities"), QStringList{QStringLiteral("ass-override"), QStringLiteral("script-geometry")}}};
}

} // namespace

VectorPathEditor::VectorPathEditor(app::DocumentContext *document, VideoViewport *viewport)
    : VisualToolBase(document, viewport)
{
}

QVariantList VectorPathEditor::contextOptions() const
{
    QVariantList result;
    const auto add = [this, &result](const QString &id, const QString &label) {
        result.push_back(QVariantMap{{QStringLiteral("id"), id},
            {QStringLiteral("label"), id == m_contextOption ? label + QStringLiteral(" ✓") : label},
            {QStringLiteral("value"), true}});
    };
    if (supportsInverseClip()) {
        const auto clip = ass::VisualTags::clip(activeText());
        result.push_back(QVariantMap{{QStringLiteral("id"), QStringLiteral("invert")},
            {QStringLiteral("label"), clip && clip->inverse ? tr("Inverse ✓") : tr("Invert")},
            {QStringLiteral("value"), true}});
    }
    add(QStringLiteral("nodes"), tr("Nodes"));
    add(QStringLiteral("add-line"), tr("Add line"));
    add(QStringLiteral("add-bezier"), tr("Add Bézier"));
    add(QStringLiteral("freehand"), tr("Freehand"));
    add(QStringLiteral("smooth-freehand"), tr("Smooth"));
    add(QStringLiteral("insert"), tr("Insert point"));
    add(QStringLiteral("line-to-bezier"), tr("Line to Bézier"));
    add(QStringLiteral("bezier-to-line"), tr("Bézier to line"));
    return result;
}

void VectorPathEditor::activate() {}

void VectorPathEditor::deactivate()
{
    if (m_freehandDrawing) finishFreehand();
    commitOperation();
    VisualToolBase::deactivate();
    m_contextOption.clear();
    m_selectedFeature.clear();
    m_selectedNodes.clear();
    m_bezierPoints.clear();
}

void VectorPathEditor::activeLineChanged()
{
    VisualToolBase::activeLineChanged();
    m_selectedFeature.clear();
    m_selectedNodes.clear();
    m_bezierPoints.clear();
}

void VectorPathEditor::pointerDown(const QPointF &point, int modifiers)
{
    clearSnapGuides();
    setCurrentPointer(point);
    m_pointer = point;
    if (!document() || !viewport()) return;
    const PathSnapshot snapshot = loadPathSnapshot(activeText());
    if (snapshot.exists && !snapshot.editable) return;

    const QVariantList features = renderOverlay();
    m_selectedFeature = hitFeatureAt(point, features);
    const int selectedNode = nodeIndex(m_selectedFeature);
    if (selectedNode >= 0) {
        if (modifiers & Qt::ControlModifier) {
            if (m_selectedNodes.contains(selectedNode)) m_selectedNodes.remove(selectedNode);
            else m_selectedNodes.insert(selectedNode);
        } else if (modifiers & Qt::ShiftModifier) {
            m_selectedNodes.insert(selectedNode);
        } else if (!m_selectedNodes.contains(selectedNode)) {
            m_selectedNodes = {selectedNode};
        }
    } else if (m_selectedFeature.isEmpty() && !(modifiers & (Qt::ControlModifier | Qt::ShiftModifier))) {
        m_selectedNodes.clear();
    }

    m_dragStartScreen = point;
    m_dragStartScript = toScript(point);
    m_dragOriginalText = activeText();
    m_dragText = m_dragOriginalText;
    m_dragNode = selectedNode;
    m_dragRole.clear();
    m_dragging = false;
    m_freehandDrawing = false;

    if (m_contextOption == QStringLiteral("add-line") || m_contextOption == QStringLiteral("add-bezier")
        || m_contextOption == QStringLiteral("insert")) {
        // These are click operations. Pointer motion only updates the cursor;
        // it must never open the ordinary node/path drag transaction.
        m_dragRole = m_contextOption;
        m_dragging = true;
        return;
    }
    if (m_contextOption == QStringLiteral("freehand") || m_contextOption == QStringLiteral("smooth-freehand")) {
        m_dragRole = m_contextOption;
        m_freehandDrawing = true;
        m_freehand = {point};
        m_freehandLast = point;
        return;
    }
    if (selectedNode >= 0) {
        m_dragRole = QStringLiteral("vector-node");
        m_dragStartHandle = pathPointToScript(snapshot, snapshot.pathText.isEmpty()
            ? QPointF{} : ass::VectorPath::parse(snapshot.pathText, snapshot.scale)->node(selectedNode));
        m_dragging = true;
    } else if (m_selectedFeature.startsWith(QStringLiteral("vector-segment-"))
               || m_selectedFeature.startsWith(QStringLiteral("vector-curve-"))) {
        m_dragRole = QStringLiteral("vector-path");
        m_dragStartHandle = m_dragStartScript;
        m_dragging = true;
    }
}

void VectorPathEditor::pointerMove(const QPointF &point, int modifiers)
{
    setCurrentPointer(point);
    m_pointer = point;
    if (m_freehandDrawing) {
        if (QLineF(point, m_freehandLast).length() >= 3.0) {
            m_freehand.push_back(point);
            m_freehandLast = point;
        }
        return;
    }
    if (!m_dragging) {
        m_selectedFeature = hitFeatureAt(point, renderOverlay());
        return;
    }
    if (!m_dragging || m_dragRole == QStringLiteral("add-line")
        || m_dragRole == QStringLiteral("add-bezier") || m_dragRole == QStringLiteral("insert"))
        return;
    if (QLineF(point, m_dragStartScreen).length() < 0.75) return;
    if (!hasTextEdit() && !beginTextEdit()) return;
    (void)snap(toScript(point), modifiers);
    const QString updated = m_dragRole == QStringLiteral("vector-node")
        ? textForNodeDrag(point, modifiers) : textForPathDrag(point);
    if (updated != m_dragText) {
        m_dragText = updated;
        previewTextEdit(updated);
    }
}

void VectorPathEditor::pointerUp(const QPointF &point, int modifiers)
{
    pointerMove(point, modifiers);
    if (m_freehandDrawing) {
        if (QLineF(point, m_freehandLast).length() > 1.0) m_freehand.push_back(point);
        finishFreehand();
    } else if (m_dragRole == QStringLiteral("add-line")) {
        appendPoint(toScript(point), false, false);
    } else if (m_dragRole == QStringLiteral("add-bezier")) {
        appendPoint(toScript(point), true, false);
    } else if (m_dragRole == QStringLiteral("insert")) {
        appendPoint(toScript(point), false, true);
    } else if (hasTextEdit()) {
        commitOperation();
    }
    clearGesture();
}

void VectorPathEditor::pointerLeave()
{
    // Retain a live drag when the cursor leaves the overlay; Qt still sends
    // the matching release/cancel event to the manager.
}

void VectorPathEditor::keyDown(int key, int modifiers)
{
    if (key == Qt::Key_Escape) {
        cancelOperation();
        m_freehand.clear();
        m_bezierPoints.clear();
        return;
    }
    if (key == Qt::Key_Delete) {
        deleteSelectedNodes();
        return;
    }
    if (key == Qt::Key_Left || key == Qt::Key_Right || key == Qt::Key_Up || key == Qt::Key_Down) {
        if (m_selectedNodes.isEmpty() || !document()) return;
        const qreal step = (modifiers & Qt::ShiftModifier) ? 10.0 : 1.0;
        QPointF delta;
        if (key == Qt::Key_Left) delta.setX(-step);
        if (key == Qt::Key_Right) delta.setX(step);
        if (key == Qt::Key_Up) delta.setY(-step);
        if (key == Qt::Key_Down) delta.setY(step);
        m_dragOriginalText = activeText();
        m_dragText = m_dragOriginalText;
        m_dragNode = *m_selectedNodes.cbegin();
        const PathSnapshot snapshot = loadPathSnapshot(m_dragOriginalText);
        const auto path = ass::VectorPath::parse(snapshot.pathText, snapshot.scale);
        if (!path || m_dragNode < 0 || m_dragNode >= path->nodeCount()) return;
        m_dragStartHandle = pathPointToScript(snapshot, path->node(m_dragNode));
        m_dragStartScreen = toScreen(m_dragStartHandle);
        m_dragStartScript = toScript(m_dragStartScreen);
        if (beginTextEdit()) {
            const QPointF screen = m_dragStartScreen + viewport()->scriptDeltaToScreenDelta(delta);
            const QString updated = textForNodeDrag(screen, modifiers);
            previewTextEdit(updated);
            commitOperation();
        }
    }
}

void VectorPathEditor::setOption(const QString &id, const QVariant &value)
{
    if (id == QStringLiteral("invert") && supportsInverseClip()) {
        editTextOnce(invertedPathText(activeText()));
    } else if (id == QStringLiteral("line-to-bezier")) {
        convertSelectedSegment(true);
    } else if (id == QStringLiteral("bezier-to-line")) {
        convertSelectedSegment(false);
    } else if (id == QStringLiteral("nodes") || id == QStringLiteral("add-line")
        || id == QStringLiteral("add-bezier") || id == QStringLiteral("freehand")
        || id == QStringLiteral("smooth-freehand") || id == QStringLiteral("insert")) {
        m_contextOption = id;
        if (id != QStringLiteral("add-bezier")) m_bezierPoints.clear();
    }
    Q_UNUSED(value);
}

void VectorPathEditor::cancelOperation()
{
    VisualToolBase::cancelOperation();
    clearGesture();
    m_freehand.clear();
    m_freehandDrawing = false;
}

void VectorPathEditor::commitOperation()
{
    VisualToolBase::commitOperation();
    clearGesture();
}

void VectorPathEditor::buildOverlay(QVariantList &features) const
{
    const PathSnapshot snapshot = loadPathSnapshot(activeText());
    if (snapshot.editable) {
        const auto parsed = ass::VectorPath::parse(snapshot.pathText, snapshot.scale);
        if (parsed && parsed->editable()) {
            QPointF current;
            bool hasCurrent = false;
            int node = 0;
            const auto scriptPoint = [this, &snapshot](QPointF point) { return pathPointToScript(snapshot, point); };
            for (int commandIndex = 0; commandIndex < parsed->commands().size(); ++commandIndex) {
                const auto &command = parsed->commands().at(commandIndex);
                if (command.kind == ass::VectorPath::Kind::Move) {
                    current = command.points.front(); hasCurrent = true;
                    addPoint(features, QStringLiteral("vector-%1").arg(node), scriptPoint(current),
                        QString::number(node + 1), QStringLiteral("vector-node"), m_selectedNodes.contains(node), QStringLiteral("endpoint"));
                    ++node;
                } else if (command.kind == ass::VectorPath::Kind::Line && hasCurrent) {
                    const QPointF end = command.points.front();
                    addLine(features, QStringLiteral("vector-segment-%1").arg(commandIndex), scriptPoint(current), scriptPoint(end), QStringLiteral("vector-path"));
                    addPoint(features, QStringLiteral("vector-%1").arg(node), scriptPoint(end), QString::number(node + 1),
                        QStringLiteral("vector-node"), m_selectedNodes.contains(node), QStringLiteral("endpoint"));
                    current = end; ++node;
                } else if (command.kind == ass::VectorPath::Kind::Cubic && hasCurrent) {
                    const QPointF c1 = command.points[0], c2 = command.points[1], end = command.points[2];
                    addLine(features, QStringLiteral("vector-control-a-%1").arg(commandIndex), scriptPoint(current), scriptPoint(c1));
                    addLine(features, QStringLiteral("vector-control-b-%1").arg(commandIndex), scriptPoint(c2), scriptPoint(end));
                    addPoint(features, QStringLiteral("vector-%1").arg(node), scriptPoint(c1), {}, QStringLiteral("vector-node"), m_selectedNodes.contains(node), QStringLiteral("control"));
                    addPoint(features, QStringLiteral("vector-%1").arg(node + 1), scriptPoint(c2), {}, QStringLiteral("vector-node"), m_selectedNodes.contains(node + 1), QStringLiteral("control"));
                    addPoint(features, QStringLiteral("vector-%1").arg(node + 2), scriptPoint(end), QString::number(node + 3),
                        QStringLiteral("vector-node"), m_selectedNodes.contains(node + 2), QStringLiteral("endpoint"));
                    QPointF previous = current;
                    for (int step = 1; step <= 16; ++step) {
                        const qreal t = step / 16.0, u = 1.0 - t;
                        const QPointF curve = current * (u*u*u) + c1 * (3*u*u*t) + c2 * (3*u*t*t) + end * (t*t*t);
                        addLine(features, QStringLiteral("vector-curve-%1-%2").arg(commandIndex).arg(step),
                            scriptPoint(previous), scriptPoint(curve), QStringLiteral("vector-path"));
                        previous = curve;
                    }
                    current = end; node += 3;
                }
            }
        }
    } else if (snapshot.exists && !snapshot.editable) {
        addPoint(features, QStringLiteral("vector-read-only"), snapshot.anchor,
            tr("Unsupported drawing syntax is preserved read-only"));
    }
    if (m_freehandDrawing && m_freehand.size() > 1) {
        for (int i = 1; i < m_freehand.size(); ++i)
            addScreenLine(features, QStringLiteral("freehand-%1").arg(i), m_freehand[i - 1], m_freehand[i]);
    }
}

QPointF VectorPathEditor::pathPointToScript(const PathSnapshot &snapshot, QPointF point) const
{
    return snapshot.anchor + point * pathScaleFactor(snapshot.scale);
}

QPointF VectorPathEditor::scriptToPathPoint(const PathSnapshot &snapshot, QPointF point) const
{
    return (point - snapshot.anchor) / pathScaleFactor(snapshot.scale);
}

QString VectorPathEditor::hitFeatureAt(QPointF point, const QVariantList &features) const
{
    const QString pointId = hitPoint(features, point);
    if (!pointId.isEmpty()) return pointId;
    QString best;
    if (hitLine(features, point, &best) < 9.0) return best;
    return {};
}

QString VectorPathEditor::textForNodeDrag(QPointF screenPoint, int)
{
    PathSnapshot snapshot = loadPathSnapshot(m_dragOriginalText);
    auto path = ass::VectorPath::parse(snapshot.pathText, snapshot.scale);
    if (!path || !path->editable() || m_dragNode < 0 || m_dragNode >= path->nodeCount()) return m_dragOriginalText;
    const QPointF script = m_dragStartHandle + (toScript(screenPoint) - m_dragStartScript);
    const QPointF local = scriptToPathPoint(snapshot, script);
    QSet<int> nodes = m_selectedNodes;
    if (nodes.isEmpty()) nodes.insert(m_dragNode);
    if (!path->translateNodes(nodes, local - path->node(m_dragNode))) return m_dragOriginalText;
    return storePath(snapshot, *path);
}

QString VectorPathEditor::textForPathDrag(QPointF screenPoint) const
{
    PathSnapshot snapshot = loadPathSnapshot(m_dragOriginalText);
    auto path = ass::VectorPath::parse(snapshot.pathText, snapshot.scale);
    const QPointF delta = toScript(screenPoint) - m_dragStartScript;
    if (!path || !path->editable() || !path->translate(delta / pathScaleFactor(snapshot.scale))) return m_dragOriginalText;
    return storePath(snapshot, *path);
}

void VectorPathEditor::appendPoint(QPointF scriptPoint, bool bezier, bool insert)
{
    PathSnapshot snapshot = loadPathSnapshot(activeText());
    if (!snapshot.editable) return;
    const QPointF local = scriptToPathPoint(snapshot, scriptPoint);
    auto path = snapshot.pathText.isEmpty() ? std::optional<ass::VectorPath>{}
        : ass::VectorPath::parse(snapshot.pathText, snapshot.scale);
    if (!snapshot.pathText.isEmpty() && (!path || !path->editable())) return;
    if (bezier && path) {
        m_bezierPoints.push_back(local);
        if (m_bezierPoints.size() < 3) return;
        if (!path->appendCubic(m_bezierPoints[0], m_bezierPoints[1], m_bezierPoints[2])) {
            m_bezierPoints.clear(); return;
        }
        m_bezierPoints.clear();
    } else if (!path) {
        path = ass::VectorPath::startAt(local, snapshot.scale);
    } else {
        bool inserted = false;
        qreal nearest = 12.0;
        int selectedSegment = -1;
        qreal selectedT = 0.5;
        QPointF current;
        for (int commandIndex = 0; commandIndex < path->commands().size(); ++commandIndex) {
            const auto &command = path->commands().at(commandIndex);
            if (command.kind == ass::VectorPath::Kind::Move) { current = command.points.front(); continue; }
            const auto screenFor = [this, &snapshot](QPointF localPoint) { return toScreen(pathPointToScript(snapshot, localPoint)); };
            if (command.kind == ass::VectorPath::Kind::Line) {
                QPointF projection;
                const QPointF a = screenFor(current), b = screenFor(command.points.front());
                const qreal distance = distanceToSegment(m_pointer, a, b, &projection);
                if (distance < nearest) {
                    const QPointF d = b - a;
                    nearest = distance; selectedSegment = commandIndex;
                    selectedT = QPointF::dotProduct(projection - a, d) / std::max<qreal>(0.0001, QPointF::dotProduct(d, d));
                }
                current = command.points.front();
            } else {
                const QPointF p0 = current, p1 = command.points[0], p2 = command.points[1], p3 = command.points[2];
                QPointF previous = screenFor(p0);
                for (int step = 1; step <= 64; ++step) {
                    const qreal t = step / 64.0, u = 1.0 - t;
                    const QPointF curve = p0*(u*u*u) + p1*(3*u*u*t) + p2*(3*u*t*t) + p3*(t*t*t);
                    const QPointF screen = screenFor(curve);
                    QPointF projection;
                    const qreal distance = distanceToSegment(m_pointer, previous, screen, &projection);
                    if (distance < nearest) {
                        const QPointF delta = screen - previous;
                        const qreal localT = QPointF::dotProduct(projection - previous, delta)
                            / std::max<qreal>(0.0001, QPointF::dotProduct(delta, delta));
                        nearest = distance; selectedSegment = commandIndex;
                        selectedT = ((step - 1) + localT) / 64.0;
                    }
                    previous = screen;
                }
                current = p3;
            }
        }
        if (selectedSegment >= 0) inserted = path->insertOnSegment(selectedSegment, selectedT);
        if (insert && !inserted) return;
        if (!inserted && !path->appendLine(local)) return;
    }
    const QString updated = storePath(snapshot, *path);
    editTextOnce(updated);
}

void VectorPathEditor::finishFreehand()
{
    if (m_freehand.isEmpty()) { m_freehandDrawing = false; return; }
    QVector<QPointF> points = m_freehand;
    if (m_contextOption == QStringLiteral("smooth-freehand")) points = simplify(points, 2.5);
    PathSnapshot snapshot = loadPathSnapshot(activeText());
    if (!snapshot.editable) { m_freehand.clear(); m_freehandDrawing = false; return; }
    std::optional<ass::VectorPath> path = snapshot.pathText.isEmpty() ? std::nullopt
        : ass::VectorPath::parse(snapshot.pathText, snapshot.scale);
    if (!snapshot.pathText.isEmpty() && (!path || !path->editable())) { m_freehand.clear(); m_freehandDrawing = false; return; }
    for (const QPointF screen : std::as_const(points)) {
        const QPointF local = scriptToPathPoint(snapshot, toScript(screen));
        if (!path) path = ass::VectorPath::startAt(local, snapshot.scale);
        else if (!path->appendLine(local)) break;
    }
    if (path) editTextOnce(storePath(snapshot, *path));
    m_freehand.clear();
    m_freehandDrawing = false;
}

void VectorPathEditor::deleteSelectedNodes()
{
    if (m_selectedNodes.isEmpty()) return;
    PathSnapshot snapshot = loadPathSnapshot(activeText());
    auto path = ass::VectorPath::parse(snapshot.pathText, snapshot.scale);
    if (!snapshot.editable || !path || !path->deleteNodes(m_selectedNodes)) return;
    editTextOnce(storePath(snapshot, *path));
    m_selectedNodes.clear();
}

void VectorPathEditor::convertSelectedSegment(bool toBezier)
{
    const int index = segmentIndex(m_selectedFeature);
    if (index < 1) return;
    PathSnapshot snapshot = loadPathSnapshot(activeText());
    auto path = ass::VectorPath::parse(snapshot.pathText, snapshot.scale);
    if (!snapshot.editable || !path) return;
    const bool changed = toBezier ? path->convertSegmentToCubic(index) : path->convertSegmentToLine(index);
    if (changed) editTextOnce(storePath(snapshot, *path));
}

void VectorPathEditor::clearGesture()
{
    m_dragging = false;
    m_dragNode = -1;
    m_dragRole.clear();
    m_dragStartScreen = {};
    m_dragStartScript = {};
    m_dragStartHandle = {};
    m_dragOriginalText.clear();
    m_dragText.clear();
}

VectorClipTool::VectorClipTool(app::DocumentContext *document, VideoViewport *viewport)
    : VectorPathEditor(document, viewport) {}
QVariantMap VectorClipTool::descriptor() const { return pathDescriptor(toolName(), tr("Vector Clip"), icon(), tooltip(), commandId(), factoryId()); }
VectorPathEditor::PathSnapshot VectorClipTool::loadPathSnapshot(const QString &text) const
{
    PathSnapshot snapshot;
    snapshot.text = text;
    snapshot.editable = true;
    const auto clip = ass::VisualTags::clip(text);
    if (!clip) return snapshot;
    snapshot.exists = true;
    snapshot.pathText = clip->path;
    snapshot.scale = clip->drawingScale;
    snapshot.editable = clip->rectangle || clip->pathEditable || clip->path.isEmpty();
    return snapshot;
}
QString VectorClipTool::storePath(const PathSnapshot &snapshot, const ass::VectorPath &path) const
{
    auto clip = ass::VisualTags::clip(snapshot.text).value_or(ass::VisualTags::Clip{});
    clip.rectangle = false;
    clip.drawingScale = snapshot.scale;
    clip.path = path.serialize();
    clip.pathEditable = true;
    return ass::VisualTags::setClip(snapshot.text, clip);
}
QString VectorClipTool::invertedPathText(const QString &text) const
{
    auto clip = ass::VisualTags::clip(text);
    if (!clip) return text;
    clip->inverse = !clip->inverse;
    return ass::VisualTags::setClip(text, *clip);
}
QString VectorClipTool::toolName() const { return QStringLiteral("vector-clip"); }
QString VectorClipTool::icon() const { return QStringLiteral("⌗"); }
QString VectorClipTool::tooltip() const { return tr("Edit vector clip nodes, curves, or freehand paths"); }
QString VectorClipTool::commandId() const { return QStringLiteral("visual.vectorClip"); }
QString VectorClipTool::factoryId() const { return QStringLiteral("VectorClipTool"); }

DrawingTool::DrawingTool(app::DocumentContext *document, VideoViewport *viewport)
    : VectorPathEditor(document, viewport) {}
QVariantMap DrawingTool::descriptor() const { return pathDescriptor(toolName(), tr("Vector Drawing"), icon(), tooltip(), commandId(), factoryId()); }
VectorPathEditor::PathSnapshot DrawingTool::loadPathSnapshot(const QString &text) const
{
    PathSnapshot snapshot;
    snapshot.text = text;
    snapshot.editable = true;
    snapshot.anchor = effectivePosition(activeEvent());
    const auto drawing = ass::VisualTags::drawing(text);
    if (!drawing) return snapshot;
    snapshot.exists = true;
    snapshot.pathText = drawing->path;
    snapshot.scale = drawing->drawingScale;
    snapshot.editable = drawing->pathEditable || drawing->path.isEmpty();
    return snapshot;
}
QString DrawingTool::storePath(const PathSnapshot &snapshot, const ass::VectorPath &path) const
{
    return ass::VisualTags::setDrawingPath(snapshot.text, path.serialize(), snapshot.scale);
}
QString DrawingTool::toolName() const { return QStringLiteral("drawing"); }
QString DrawingTool::icon() const { return QStringLiteral("⌁"); }
QString DrawingTool::tooltip() const { return tr("Edit an ASS vector drawing on the active line"); }
QString DrawingTool::commandId() const { return QStringLiteral("visual.drawing"); }
QString DrawingTool::factoryId() const { return QStringLiteral("DrawingTool"); }

void registerVectorVisualTools(VisualToolManager &manager)
{
    manager.registerToolFactory(VectorClipTool(nullptr, nullptr).descriptor(),
        [](app::DocumentContext *document, VideoViewport *viewport) { return std::make_shared<VectorClipTool>(document, viewport); });
    manager.registerToolFactory(DrawingTool(nullptr, nullptr).descriptor(),
        [](app::DocumentContext *document, VideoViewport *viewport) { return std::make_shared<DrawingTool>(document, viewport); });
}

} // namespace yoake::ui
