#include "ui/visual_tool_manager.h"

#include "app/document_context.h"
#include "ass/visual_tags.h"
#include "media/media_session.h"
#include "models/subtitle_model.h"
#include "ui/video_viewport.h"

#include <QtCore/QLineF>
#include <QtCore/QLocale>
#include <QtCore/QRegularExpression>
#include <QtCore/QtMath>
#include <QtGui/QFont>
#include <QtGui/QFontMetricsF>

#include <algorithm>
#include <cmath>
#include <functional>
#include <utility>

namespace yoake::ui {
namespace {

constexpr int leftButton = Qt::LeftButton;
constexpr int deleteKey = Qt::Key_Delete;
constexpr int escapeKey = Qt::Key_Escape;

struct VectorPair {
    QPointF point;
    qsizetype xStart = 0;
    qsizetype xLength = 0;
    qsizetype yStart = 0;
    qsizetype yLength = 0;
    QChar command;
};

QVector<VectorPair> vectorPairs(const QString &path)
{
    QVector<VectorPair> pairs;
    QChar command;
    QVector<QPair<qreal, QPair<qsizetype, qsizetype>>> pending;
    qsizetype index = 0;
    while (index < path.size()) {
        if (path.at(index).isLetter()) {
            command = path.at(index).toLower();
            ++index;
            pending.clear();
            continue;
        }
        while (index < path.size() && (path.at(index).isSpace() || path.at(index) == u',')) ++index;
        if (index >= path.size()) break;
        const qsizetype start = index;
        if (path.at(index) == u'+' || path.at(index) == u'-') ++index;
        bool dot = false;
        bool digit = false;
        while (index < path.size()) {
            const QChar c = path.at(index);
            if (c.isDigit()) { digit = true; ++index; }
            else if (c == u'.' && !dot) { dot = true; ++index; }
            else break;
        }
        if (!digit) { ++index; continue; }
        bool ok = false;
        const qreal value = QLocale::c().toDouble(path.mid(start, index - start), &ok);
        if (!ok) continue;
        pending.push_back({value, {start, index - start}});
        if (pending.size() == 2) {
            const auto &x = pending.at(0);
            const auto &y = pending.at(1);
            pairs.push_back({QPointF(x.first, y.first), x.second.first, x.second.second,
                             y.second.first, y.second.second, command});
            pending.clear();
        }
    }
    return pairs;
}

QString setVectorPair(const QString &path, int pairIndex, const QPointF &point)
{
    const QVector<VectorPair> pairs = vectorPairs(path);
    if (pairIndex < 0 || pairIndex >= pairs.size()) return path;
    const VectorPair &pair = pairs[pairIndex];
    QString result = path;
    result.replace(pair.yStart, pair.yLength, ass::VisualTags::formatNumber(point.y()));
    result.replace(pair.xStart, pair.xLength, ass::VisualTags::formatNumber(point.x()));
    return result;
}

QString plainTextForMetrics(QString text)
{
    static const QRegularExpression blocks(QStringLiteral("\\{[^}]*\\}"));
    text.remove(blocks);
    text.replace(QStringLiteral("\\N"), QStringLiteral("\n"), Qt::CaseInsensitive);
    text.replace(QStringLiteral("\\n"), QStringLiteral("\n"), Qt::CaseInsensitive);
    return text;
}

QString numericText(const QPointF &point)
{
    return QStringLiteral("%1, %2").arg(ass::VisualTags::formatNumber(point.x()),
                                      ass::VisualTags::formatNumber(point.y()));
}

qreal distance(const QPointF &a, const QPointF &b)
{
    return QLineF(a, b).length();
}

qreal distanceToSegment(const QPointF &point, const QPointF &start, const QPointF &end, QPointF *projection)
{
    const QPointF delta = end - start;
    const qreal lengthSquared = QPointF::dotProduct(delta, delta);
    const qreal t = lengthSquared <= 0.0001 ? 0.0
        : qBound<qreal>(0.0, QPointF::dotProduct(point - start, delta) / lengthSquared, 1.0);
    const QPointF nearest = start + delta * t;
    if (projection) *projection = nearest;
    return distance(point, nearest);
}

int vectorNodeIndex(QStringView featureId)
{
    if (!featureId.startsWith(u"vector-")) return -1;
    bool ok = false;
    const int index = featureId.mid(7).toInt(&ok);
    return ok ? index : -1;
}

qreal drawingScaleFactor(int scale)
{
    return std::pow(2.0, std::clamp(scale, 1, 100) - 1);
}

bool isVectorEditor(QStringView toolId)
{
    return toolId == u"vector-clip" || toolId == u"drawing";
}

bool isDrawingEditor(QStringView toolId)
{
    return toolId == u"drawing";
}

QSizeF estimateSubtitleBounds(const QString &text, const ass::Document::Style &style,
                              qreal scaleX, qreal scaleY)
{
    QFont font(style.fontName);
    font.setPixelSize(std::max(1, qRound(style.fontSize)));
    const QFontMetricsF metrics(font);
    const QStringList lines = plainTextForMetrics(text).split(u'\n', Qt::KeepEmptyParts);
    qreal width = 0.0;
    for (const QString &line : lines)
        width = std::max(width, metrics.horizontalAdvance(line));
    const qreal naturalHeight = std::max<qsizetype>(1, lines.size()) * metrics.lineSpacing();
    return QSizeF(std::max<qreal>(1.0, width * style.scaleX / 100.0 * scaleX / 100.0),
                  std::max<qreal>(1.0, naturalHeight * style.scaleY / 100.0 * scaleY / 100.0));
}

QVariantMap descriptor(QString id, QString title, QString icon, QString tip, QString command, QString factory)
{
    return {{QStringLiteral("id"), std::move(id)}, {QStringLiteral("name"), std::move(title)},
        {QStringLiteral("icon"), std::move(icon)}, {QStringLiteral("tooltip"), std::move(tip)},
        {QStringLiteral("commandId"), std::move(command)}, {QStringLiteral("factory"), std::move(factory)},
        {QStringLiteral("capabilities"), QStringList{QStringLiteral("ass-override"), QStringLiteral("script-geometry")}}};
}

QVector<QPointF> simplifyPath(const QVector<QPointF> &points, qreal tolerance)
{
    if (points.size() <= 2) return points;
    QVector<bool> keep(points.size(), false);
    keep[0] = keep[points.size() - 1] = true;
    std::function<void(int, int)> simplify = [&](int first, int last) {
        qreal maximum = tolerance;
        int index = -1;
        const QLineF baseline(points[first], points[last]);
        const qreal length = baseline.length();
        for (int i = first + 1; i < last; ++i) {
            const qreal d = length <= 0.001 ? distance(points[i], points[first])
                : std::abs((points[last].x() - points[first].x()) * (points[first].y() - points[i].y())
                    - (points[first].x() - points[i].x()) * (points[last].y() - points[first].y())) / length;
            if (d > maximum) { maximum = d; index = i; }
        }
        if (index >= 0) {
            keep[index] = true;
            simplify(first, index);
            simplify(index, last);
        }
    };
    simplify(0, points.size() - 1);
    QVector<QPointF> result;
    for (qsizetype i = 0; i < points.size(); ++i)
        if (keep.at(i)) result.push_back(points.at(i));
    return result;
}

} // namespace

VisualToolManager::VisualToolManager(app::DocumentContext *document, VideoViewport *viewport, QObject *parent)
    : QObject(parent), m_document(document), m_viewport(viewport), m_snapService(viewport)
{
    m_tools = {
        descriptor(QStringLiteral("crosshair"), tr("Crosshair"), QStringLiteral("＋"), tr("Inspect script and video pixel coordinates"), QStringLiteral("visual.crosshair"), QStringLiteral("CrosshairTool")),
        descriptor(QStringLiteral("position"), tr("Position"), QStringLiteral("⌖"), tr("Place or translate the active subtitle"), QStringLiteral("visual.position"), QStringLiteral("PositionTool")),
        descriptor(QStringLiteral("move"), tr("Move"), QStringLiteral("↗"), tr("Edit both endpoints of the subtitle move"), QStringLiteral("visual.move"), QStringLiteral("MoveTool")),
        descriptor(QStringLiteral("rotate-z"), tr("Rotate Z"), QStringLiteral("⟳"), tr("Rotate around the subtitle origin"), QStringLiteral("visual.rotateZ"), QStringLiteral("RotateZTool")),
        descriptor(QStringLiteral("rotate-xy"), tr("Rotate X/Y"), QStringLiteral("⤢"), tr("Edit X and Y rotations"), QStringLiteral("visual.rotateXY"), QStringLiteral("RotateXYTool")),
        descriptor(QStringLiteral("scale"), tr("Scale"), QStringLiteral("□"), tr("Scale width and height"), QStringLiteral("visual.scale"), QStringLiteral("ScaleTool")),
        descriptor(QStringLiteral("clip"), tr("Rectangular Clip"), QStringLiteral("▭"), tr("Create or edit a rectangular clip"), QStringLiteral("visual.clip"), QStringLiteral("RectClipTool")),
        descriptor(QStringLiteral("vector-clip"), tr("Vector Clip"), QStringLiteral("⌗"), tr("Edit vector clip nodes, curves, or freehand paths"), QStringLiteral("visual.vectorClip"), QStringLiteral("VectorClipTool")),
        descriptor(QStringLiteral("drawing"), tr("Vector Drawing"), QStringLiteral("⌁"), tr("Edit an ASS vector drawing on the active line"), QStringLiteral("visual.drawing"), QStringLiteral("DrawingTool")),
        descriptor(QStringLiteral("shift"), tr("Geometry Shift"), QStringLiteral("✥"), tr("Translate the line's spatial tags together"), QStringLiteral("visual.shift"), QStringLiteral("GeometryShiftTool"))
    };
    if (m_document) {
        connect(m_document, &app::DocumentContext::activeLineChanged, this, [this] {
            emit contextOptionsChanged();
            rebuildOverlay();
        });
        connect(m_document, &app::DocumentContext::rendererRevisionChanged, this, [this] {
            emit contextOptionsChanged();
            rebuildOverlay();
        });
        connect(m_document->media(), &media::MediaSession::positionChanged, this, &VisualToolManager::rebuildOverlay);
    }
    if (m_viewport)
        connect(m_viewport, &VideoViewport::transformChanged, this, &VisualToolManager::rebuildOverlay);
    rebuildOverlay();
}

QVariantList VisualToolManager::contextOptions() const
{
    if (const auto active = customTool(m_activeToolId))
        return active->contextOptions();
    QVariantList options;
    auto add = [&options](QString id, QString label, QVariant value = true) {
        options.push_back(QVariantMap{{QStringLiteral("id"), std::move(id)},
            {QStringLiteral("label"), std::move(label)}, {QStringLiteral("value"), std::move(value)}});
    };
    if (m_activeToolId == QStringLiteral("clip")) {
        const auto clip = m_document ? ass::VisualTags::clip(m_document->activeText()) : std::nullopt;
        add(QStringLiteral("invert"), clip && clip->inverse ? tr("Inverse ✓") : tr("Invert"));
    } else if (isVectorEditor(m_activeToolId)) {
        if (!isDrawingEditor(m_activeToolId)) {
            const auto clip = m_document ? ass::VisualTags::clip(m_document->activeText()) : std::nullopt;
            add(QStringLiteral("invert"), clip && clip->inverse ? tr("Inverse ✓") : tr("Invert"));
        }
        for (const auto &[id, label] : QVector<QPair<QString, QString>>{
                 {QStringLiteral("nodes"), tr("Nodes")}, {QStringLiteral("add-line"), tr("Add line")},
                 {QStringLiteral("add-bezier"), tr("Add Bézier")}, {QStringLiteral("freehand"), tr("Freehand")},
                 {QStringLiteral("smooth-freehand"), tr("Smooth")}})
            add(id, id == m_contextOption ? label + QStringLiteral(" ✓") : label);
    } else if (m_activeToolId == QStringLiteral("move")) {
        add(QStringLiteral("capture-a"), m_hasMovePointA ? tr("Point A ✓") : tr("Set point A"));
        add(QStringLiteral("capture-b"), tr("Set point B"));
    } else if (m_activeToolId == QStringLiteral("rotate-z")) {
        add(QStringLiteral("angle-a"), m_hasRotationPointA ? tr("Angle point A ✓") : tr("Pick angle point A"));
        add(QStringLiteral("angle-b"), tr("Pick angle point B"));
    } else if (m_activeToolId == QStringLiteral("scale")) {
        for (const auto &[id, label] : QVector<QPair<QString, QString>>{
                 {QStringLiteral("both"), tr("Both")}, {QStringLiteral("x"), tr("Width")},
                 {QStringLiteral("y"), tr("Height")}})
            add(id, id == m_scaleAxis ? label + QStringLiteral(" ✓") : label);
        add(QStringLiteral("fit-rect"), m_scaleRectMode ? tr("Draw target ✓") : tr("Fit to rectangle"));
    } else if (m_activeToolId == QStringLiteral("rotate-xy")) {
        for (const auto &[id, label] : QVector<QPair<QString, QString>>{
                 {QStringLiteral("x"), tr("X axis")}, {QStringLiteral("y"), tr("Y axis")}})
            add(id, id == m_contextOption ? label + QStringLiteral(" ✓") : label);
    } else if (m_activeToolId == QStringLiteral("position")) {
        for (int alignment = 1; alignment <= 9; ++alignment)
            add(QStringLiteral("align-%1").arg(alignment), QString::number(alignment));
    } else if (m_activeToolId == QStringLiteral("shift") && m_document) {
        const QString text = m_document->activeText();
        auto addShift = [&add, this](const QString &id, const QString &label) {
            const bool enabled = m_shiftComponents.value(id, true);
            add(id, label + (enabled ? QStringLiteral(" ✓") : QString{}), enabled);
        };
        const auto move = ass::VisualTags::move(text);
        if (move) {
            addShift(QStringLiteral("shift-move-start"), tr("Move start"));
            addShift(QStringLiteral("shift-move-end"), tr("Move end"));
        }
        if (!move || ass::VisualTags::point(text, u"pos"))
            addShift(QStringLiteral("shift-pos"), tr("Position"));
        if (ass::VisualTags::point(text, u"org")) addShift(QStringLiteral("shift-org"), tr("Origin"));
        if (ass::VisualTags::clip(text)) addShift(QStringLiteral("shift-clip"), tr("Clip"));
        if (ass::VisualTags::drawing(text)) addShift(QStringLiteral("shift-drawing"), tr("Drawing"));
    }
    return options;
}

void VisualToolManager::setActiveToolId(const QString &toolId)
{
    const auto found = std::find_if(m_tools.cbegin(), m_tools.cend(), [&toolId](const QVariant &entry) {
        return entry.toMap().value(QStringLiteral("id")).toString() == toolId;
    });
    if (found == m_tools.cend() || m_activeToolId == toolId)
        return;
    if (m_freehandDrawing)
        finishFreehand();
    commitOperation();
    if (const auto previous = customTool(m_activeToolId))
        previous->deactivate();
    m_activeToolId = toolId;
    if (const auto active = customTool(m_activeToolId))
        active->activate();
    m_contextOption.clear();
    m_selectedFeature.clear();
    m_bezierPoints.clear();
    m_freehandDrawing = false;
    emit activeToolChanged();
    emit contextOptionsChanged();
    rebuildOverlay();
}

bool VisualToolManager::registerToolFactory(const QVariantMap &toolDescriptor, VisualToolFactory factory)
{
    const QString id = toolDescriptor.value(QStringLiteral("id")).toString().trimmed();
    const QString factoryId = toolDescriptor.value(QStringLiteral("factory")).toString().trimmed();
    if (id.isEmpty() || factoryId.isEmpty() || !factory
        || toolDescriptor.value(QStringLiteral("name")).toString().isEmpty()
        || toolDescriptor.value(QStringLiteral("icon")).toString().isEmpty()
        || toolDescriptor.value(QStringLiteral("tooltip")).toString().isEmpty()
        || toolDescriptor.value(QStringLiteral("commandId")).toString().isEmpty())
        return false;
    const bool exists = std::any_of(m_tools.cbegin(), m_tools.cend(), [&id](const QVariant &entry) {
        return entry.toMap().value(QStringLiteral("id")).toString() == id;
    });
    if (exists) return false;
    m_tools.push_back(toolDescriptor);
    m_customFactories.insert(id, std::move(factory));
    emit toolsChanged();
    return true;
}

std::shared_ptr<VisualTool> VisualToolManager::customTool(const QString &toolId) const
{
    const auto found = m_customTools.constFind(toolId);
    if (found != m_customTools.cend())
        return found.value();
    const auto factory = m_customFactories.constFind(toolId);
    if (factory == m_customFactories.cend())
        return {};
    auto created = factory.value()(m_document.data(), m_viewport.data());
    if (created)
        const_cast<VisualToolManager *>(this)->m_customTools.insert(toolId, created);
    return created;
}

void VisualToolManager::setOption(const QString &optionId, const QVariant &value)
{
    if (const auto active = customTool(m_activeToolId)) {
        active->setOption(optionId, value);
        rebuildOverlay();
        emit contextOptionsChanged();
        return;
    }
    if (optionId.startsWith(QStringLiteral("shift-"))) {
        m_shiftComponents.insert(optionId, value.toBool());
        emit contextOptionsChanged();
        rebuildOverlay();
        return;
    }
    if (optionId.startsWith(QStringLiteral("align-"))) {
        if (!m_document) return;
        const int alignment = optionId.mid(6).toInt();
        if (alignment < 1 || alignment > 9) return;
        const QString original = m_document->activeText();
        const ass::Event event = m_document->activeEventSnapshot();
        const ass::Document::Style style = m_document->styleForName(event.style);
        const int oldAlignment = ass::VisualTags::number(original, u"an").value_or(style.alignment);
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
        const QPointF topLeft(oldAnchor.x() - horizontalOffset(oldAlignment),
                              oldAnchor.y() - verticalOffset(oldAlignment));
        const QPointF newAnchor(topLeft.x() + horizontalOffset(alignment),
                                topLeft.y() + verticalOffset(alignment));
        const QPointF delta = newAnchor - oldAnchor;
        QString updated = original;
        if (const auto move = ass::VisualTags::move(updated)) {
            auto shifted = *move;
            shifted.start += delta;
            shifted.end += delta;
            updated = ass::VisualTags::setMove(updated, shifted);
        } else {
            updated = ass::VisualTags::setPoint(updated, u"pos", newAnchor);
        }
        if (const auto origin = ass::VisualTags::point(updated, u"org"))
            updated = ass::VisualTags::setPoint(updated, u"org", *origin + delta);
        updated = ass::VisualTags::setNumber(updated, u"an", alignment);
        if (updated != original && m_document->beginVisualTextEdit(m_document->lines()->activeId())) {
            m_document->previewVisualTextEdit(updated);
            m_document->commitVisualTextEdit();
        }
    } else if (optionId == QStringLiteral("invert") && m_document) {
        auto clip = ass::VisualTags::clip(m_document->activeText());
        if (clip) {
            clip->inverse = !clip->inverse;
            const QString updated = ass::VisualTags::setClip(m_document->activeText(), *clip);
            if (m_document->beginVisualTextEdit(m_document->lines()->activeId())) {
                m_document->previewVisualTextEdit(updated);
                m_document->commitVisualTextEdit();
            }
        }
    } else if (m_activeToolId == QStringLiteral("move") && optionId == QStringLiteral("capture-a")) {
        if (m_viewport && m_document && m_viewport->displayedVideoRect().contains(m_pointer)) {
            m_movePointA = m_viewport->screenToScript(m_pointer);
            m_moveTimeA = m_document->media()->displayedFrameStartMs();
            m_hasMovePointA = true;
        }
    } else if (m_activeToolId == QStringLiteral("move") && optionId == QStringLiteral("capture-b")) {
        if (!m_hasMovePointA || !m_viewport || !m_document) return;
        const ass::Event event = m_document->activeEventSnapshot();
        const QPointF pointB = m_viewport->screenToScript(m_pointer);
        const qint64 timeB = m_document->media()->displayedFrameStartMs();
        const qreal a = qBound<qint64>(0, m_moveTimeA - event.startMs, event.endMs - event.startMs);
        const qreal b = qBound<qint64>(0, timeB - event.startMs, event.endMs - event.startMs);
        ass::VisualTags::Move motion{m_movePointA, pointB, a, b};
        if (a > b) {
            std::swap(motion.start, motion.end);
            std::swap(motion.startMs, motion.endMs);
        }
        const QString updated = ass::VisualTags::setMove(ass::VisualTags::removeTag(m_document->activeText(), u"pos"), motion);
        if (m_document->beginVisualTextEdit(m_document->lines()->activeId())) {
            m_document->previewVisualTextEdit(updated);
            m_document->commitVisualTextEdit();
        }
        m_hasMovePointA = false;
    } else if (m_activeToolId == QStringLiteral("rotate-z") && optionId == QStringLiteral("angle-a")) {
        if (m_viewport && m_viewport->displayedVideoRect().contains(m_pointer)) {
            m_rotationPointA = m_viewport->screenToScript(m_pointer);
            m_hasRotationPointA = true;
        }
    } else if (m_activeToolId == QStringLiteral("rotate-z") && optionId == QStringLiteral("angle-b")) {
        if (!m_hasRotationPointA || !m_viewport || !m_document) return;
        const QPointF pointB = m_viewport->screenToScript(m_pointer);
        qreal angle = qRadiansToDegrees(std::atan2(pointB.y() - m_rotationPointA.y(), pointB.x() - m_rotationPointA.x()));
        while (angle > 180.0) angle -= 360.0;
        while (angle < -180.0) angle += 360.0;
        const QString updated = ass::VisualTags::setNumber(m_document->activeText(), u"frz", angle);
        if (m_document->beginVisualTextEdit(m_document->lines()->activeId())) {
            m_document->previewVisualTextEdit(updated);
            m_document->commitVisualTextEdit();
        }
        m_hasRotationPointA = false;
    } else if (m_activeToolId == QStringLiteral("scale") && optionId == QStringLiteral("fit-rect")) {
        m_scaleRectMode = !m_scaleRectMode;
    } else if (m_activeToolId == QStringLiteral("scale")
        && (optionId == QStringLiteral("x") || optionId == QStringLiteral("y") || optionId == QStringLiteral("both"))) {
        m_scaleAxis = optionId;
    } else {
        m_contextOption = optionId;
        m_contextOptionValue = value;
    }
    emit contextOptionsChanged();
    rebuildOverlay();
}

void VisualToolManager::pointerDown(qreal x, qreal y, int button, int modifiers)
{
    if (button != leftButton || !m_document || !m_viewport)
        return;
    m_pointer = QPointF(x, y);
    if (const auto active = customTool(m_activeToolId)) {
        active->pointerDown(m_pointer, modifiers);
        rebuildOverlay();
        return;
    }
    m_dragStartScreen = m_pointer;
    m_dragStartScript = m_viewport->screenToScript(m_pointer);
    m_selectedFeature = hitFeature(m_pointer);
    m_selectedVectorPoint = vectorNodeIndex(m_selectedFeature);
    if (m_activeToolId == QStringLiteral("crosshair")) {
        rebuildOverlay();
        return;
    }
    m_dragging = true;
    m_dragModifiers = modifiers;
    if (m_activeToolId == QStringLiteral("scale") && m_scaleRectMode) {
        m_dragRole = QStringLiteral("scale-target-rect");
        m_dragStartScript = m_viewport->screenToScript(m_pointer);
        return;
    }
    if (isVectorEditor(m_activeToolId) && m_contextOption.contains(QStringLiteral("freehand"))) {
        m_freehandDrawing = true;
        m_freehand = {m_pointer};
        m_freehandLast = m_pointer;
        return;
    }
    QString role = m_selectedFeature.isEmpty() ? QStringLiteral("create") : feature(m_selectedFeature).value(QStringLiteral("role")).toString();
    if (m_activeToolId == QStringLiteral("position") && role.isEmpty()) role = QStringLiteral("position");
    if (m_activeToolId == QStringLiteral("shift")) role = QStringLiteral("shift");
    if (m_activeToolId == QStringLiteral("clip") && role == QStringLiteral("create")) role = QStringLiteral("clip-create");
    if (isVectorEditor(m_activeToolId) && (m_contextOption == QStringLiteral("add-line")
        || m_contextOption == QStringLiteral("add-bezier"))) {
        m_dragRole = m_contextOption;
        return;
    }
    beginDrag(role, m_pointer, modifiers);
}

void VisualToolManager::pointerMove(qreal x, qreal y, int buttons, int modifiers)
{
    Q_UNUSED(buttons);
    m_pointer = QPointF(x, y);
    if (const auto active = customTool(m_activeToolId)) {
        active->pointerMove(m_pointer, modifiers);
        rebuildOverlay();
        return;
    }
    if (m_freehandDrawing) {
        if (distance(m_pointer, m_freehandLast) >= 3.0) {
            m_freehand.push_back(m_pointer);
            m_freehandLast = m_pointer;
        }
        rebuildOverlay();
        return;
    }
    if (m_dragging && !m_dragRole.isEmpty() && m_dragRole != QStringLiteral("scale-target-rect")
        && m_dragRole != QStringLiteral("add-line") && m_dragRole != QStringLiteral("add-bezier"))
        updateDrag(m_pointer, modifiers);
    else if (!m_dragging && (m_activeToolId == QStringLiteral("crosshair") || isVectorEditor(m_activeToolId))) {
        m_selectedFeature = hitFeature(m_pointer);
        m_selectedVectorPoint = vectorNodeIndex(m_selectedFeature);
    }
    rebuildOverlay();
}

void VisualToolManager::pointerUp(qreal x, qreal y, int button, int modifiers)
{
    if (button != leftButton)
        return;
    m_pointer = QPointF(x, y);
    if (const auto active = customTool(m_activeToolId)) {
        active->pointerUp(m_pointer, modifiers);
        rebuildOverlay();
        return;
    }
    if (m_freehandDrawing) {
        if (distance(m_pointer, m_freehandLast) > 1.0) m_freehand.push_back(m_pointer);
        finishFreehand();
    } else if (m_dragRole == QStringLiteral("scale-target-rect")) {
        commitScaleRectangle(m_viewport->screenToScript(m_pointer), modifiers);
    } else if (m_dragRole == QStringLiteral("add-line")) {
        appendVectorPoint(m_viewport->screenToScript(m_pointer), false);
    } else if (m_dragRole == QStringLiteral("add-bezier")) {
        appendVectorPoint(m_viewport->screenToScript(m_pointer), true);
    } else if (m_transactionStarted) {
        commitOperation();
    }
    m_dragging = false;
    m_dragRole.clear();
    m_transactionStarted = false;
    rebuildOverlay();
}

void VisualToolManager::pointerLeave()
{
    if (const auto active = customTool(m_activeToolId)) {
        active->pointerLeave();
        rebuildOverlay();
        return;
    }
    if (m_activeToolId != QStringLiteral("crosshair")) return;
    m_pointer = QPointF(-1000.0, -1000.0);
    rebuildOverlay();
}

bool VisualToolManager::wheel(qreal x, qreal y, qreal deltaX, qreal deltaY, int modifiers)
{
    if (const auto active = customTool(m_activeToolId)) {
        const bool consumed = active->wheel(QPointF(x, y), QPointF(deltaX, deltaY), modifiers, 0);
        if (consumed) rebuildOverlay();
        return consumed;
    }
    return false;
}

void VisualToolManager::keyDown(int key, int modifiers)
{
    if (const auto active = customTool(m_activeToolId)) {
        active->keyDown(key, modifiers);
        rebuildOverlay();
        return;
    }
    if ((modifiers & Qt::ControlModifier) && (modifiers & Qt::AltModifier)
        && ((key >= Qt::Key_1 && key <= Qt::Key_9) || key == Qt::Key_0)) {
        const int index = key == Qt::Key_0 ? 9 : key - Qt::Key_1;
        if (index >= 0 && index < m_tools.size())
            setActiveToolId(m_tools.at(index).toMap().value(QStringLiteral("id")).toString());
        return;
    }
    if (key == escapeKey) {
        cancelOperation();
        m_freehandDrawing = false;
        m_bezierPoints.clear();
    } else if (key == deleteKey && isVectorEditor(m_activeToolId)) {
        deleteSelectedVectorPoint();
    } else if (key == Qt::Key_Left || key == Qt::Key_Right || key == Qt::Key_Up || key == Qt::Key_Down) {
        if (m_selectedFeature.isEmpty() || !m_document || !m_viewport) return;
        const qreal step = modifiers & Qt::ShiftModifier ? 10.0 : 1.0;
        QPointF delta;
        if (key == Qt::Key_Left) delta.setX(-step);
        if (key == Qt::Key_Right) delta.setX(step);
        if (key == Qt::Key_Up) delta.setY(-step);
        if (key == Qt::Key_Down) delta.setY(step);
        const QVariantMap selected = feature(m_selectedFeature);
        const QPointF start(selected.value(QStringLiteral("x")).toReal(), selected.value(QStringLiteral("y")).toReal());
        const QString role = selected.value(QStringLiteral("role")).toString();
        beginDrag(role, start, modifiers);
        updateDrag(start + m_viewport->scriptDeltaToScreenDelta(delta), modifiers);
        if (m_transactionStarted) commitOperation();
    }
}

void VisualToolManager::cancelOperation()
{
    if (const auto active = customTool(m_activeToolId))
        active->cancelOperation();
    if (m_transactionStarted && m_document)
        m_document->cancelVisualTextEdit();
    m_transactionStarted = false;
    m_dragging = false;
    m_dragRole.clear();
    m_freehandDrawing = false;
    m_freehand.clear();
    rebuildOverlay();
}

void VisualToolManager::commitOperation()
{
    if (const auto active = customTool(m_activeToolId))
        active->commitOperation();
    if (m_transactionStarted && m_document)
        m_document->commitVisualTextEdit();
    m_transactionStarted = false;
    m_dragging = false;
    m_dragRole.clear();
    rebuildOverlay();
}

void VisualToolManager::rebuildOverlay()
{
    m_features.clear();
    m_guides.clear();
    m_coordinateLabel.clear();
    if (!m_document || !m_viewport || m_viewport->videoSize().isEmpty()) {
        emit overlayChanged();
        return;
    }
    if (const auto active = customTool(m_activeToolId)) {
        m_features = active->renderOverlay();
        emit overlayChanged();
        return;
    }
    if (m_activeToolId == QStringLiteral("crosshair")) {
        const QPointF script = m_viewport->screenToScript(m_pointer);
        const QPointF video = m_viewport->screenToVideo(m_pointer);
        m_coordinateLabel = QStringLiteral("Script %1 · Video %2 px").arg(numericText(script), numericText(video));
        if (m_viewport->displayedVideoRect().contains(m_pointer)) {
            QVariantMap cross{{QStringLiteral("id"), QStringLiteral("crosshair")}, {QStringLiteral("kind"), QStringLiteral("crosshair")},
                {QStringLiteral("x"), m_pointer.x()}, {QStringLiteral("y"), m_pointer.y()}, {QStringLiteral("color"), QStringLiteral("#ffffffbb")}};
            m_features.push_back(cross);
            QVariantMap label{{QStringLiteral("id"), QStringLiteral("coordinate-label")}, {QStringLiteral("kind"), QStringLiteral("label")},
                {QStringLiteral("x"), m_pointer.x() + 12}, {QStringLiteral("y"), m_pointer.y() + 12},
                {QStringLiteral("label"), m_coordinateLabel}};
            m_features.push_back(label);
        }
        emit overlayChanged();
        return;
    }
    const ass::Event event = m_document->activeEventSnapshot();
    const QString text = event.text;
    const auto move = ass::VisualTags::move(text);
    const QPointF anchor = effectivePosition(event);

    if (m_activeToolId == QStringLiteral("position")) {
        if (move) {
            addLine(QStringLiteral("move-path"), move->start, move->end, {});
            addPoint(QStringLiteral("move-start"), move->start, tr("Start"), QStringLiteral("translate"));
            addPoint(QStringLiteral("move-end"), move->end, tr("End"), QStringLiteral("translate"));
        } else {
            addPoint(QStringLiteral("position"), anchor, tr("Position"), QStringLiteral("position"), true);
            const QPointF screen = m_viewport->scriptToScreen(anchor);
            const int alignment = ass::VisualTags::number(text, u"an").value_or(m_document->styleForName(event.style).alignment);
            const int column = (alignment - 1) % 3;
            if (column == 0) addLine(QStringLiteral("alignment-guide"), anchor, QPointF(0, anchor.y()));
            else if (column == 2) addLine(QStringLiteral("alignment-guide"), anchor, QPointF(m_viewport->scriptSize().width(), anchor.y()));
            Q_UNUSED(screen);
        }
    } else if (m_activeToolId == QStringLiteral("move")) {
        if (move) {
            addLine(QStringLiteral("move-path"), move->start, move->end, {});
            addPoint(QStringLiteral("move-start"), move->start, tr("Start"), QStringLiteral("move-start"));
            addPoint(QStringLiteral("move-end"), move->end, tr("End"), QStringLiteral("move-end"));
            const qint64 lineDuration = std::max<qint64>(0, event.endMs - event.startMs);
            const qreal startMs = move->startMs.value_or(0.0);
            const qreal endMs = move->endMs.value_or(static_cast<qreal>(lineDuration));
            const qreal now = m_document->media()->displayedFrameStartMs() - event.startMs;
            const qreal amount = endMs <= startMs ? 0.0 : qBound<qreal>(0.0, (now - startMs) / (endMs - startMs), 1.0);
            addPoint(QStringLiteral("move-current"), move->start + (move->end - move->start) * amount,
                     tr("Current"), {}, true);
            addLine(QStringLiteral("move-direction"), move->start, move->start + (move->end - move->start) * 0.25);
        } else {
            addPoint(QStringLiteral("move-origin"), anchor, tr("Click and drag to create a move"), QStringLiteral("create"));
        }
    } else if (m_activeToolId == QStringLiteral("rotate-z")) {
        const QPointF origin = ass::VisualTags::point(text, u"org").value_or(anchor);
        const qreal angle = ass::VisualTags::number(text, u"frz").value_or(0.0);
        const QPointF originScreen = m_viewport->scriptToScreen(origin);
        const qreal radius = 46.0;
        QVariantMap ring{{QStringLiteral("id"), QStringLiteral("rotation-ring")}, {QStringLiteral("kind"), QStringLiteral("ring")},
            {QStringLiteral("x"), originScreen.x()}, {QStringLiteral("y"), originScreen.y()}, {QStringLiteral("radius"), radius}};
        m_features.push_back(ring);
        const qreal radians = qDegreesToRadians(angle);
        const QPointF ray = m_viewport->screenToScript(originScreen
            + QPointF(std::cos(radians) * radius, std::sin(radians) * radius));
        addLine(QStringLiteral("rotation-ray"), origin, ray);
        addPoint(QStringLiteral("rotation-origin"), origin, tr("Origin"), ass::VisualTags::point(text, u"org") ? QStringLiteral("origin") : QString{});
        addPoint(QStringLiteral("rotation-handle"), ray, QStringLiteral("%1°").arg(ass::VisualTags::formatNumber(angle)), QStringLiteral("rotation"));
    } else if (m_activeToolId == QStringLiteral("rotate-xy")) {
        const QPointF center = m_viewport->scriptToScreen(anchor);
        const qreal frx = ass::VisualTags::number(text, u"frx").value_or(0.0);
        const qreal fry = ass::VisualTags::number(text, u"fry").value_or(0.0);
        QVariantMap xHandle{{QStringLiteral("id"), QStringLiteral("rotate-x")}, {QStringLiteral("kind"), QStringLiteral("point")},
            {QStringLiteral("x"), center.x()}, {QStringLiteral("y"), center.y() - 54}, {QStringLiteral("label"), QStringLiteral("X %1°").arg(ass::VisualTags::formatNumber(frx))},
            {QStringLiteral("role"), QStringLiteral("rotate-x")}};
        QVariantMap yHandle{{QStringLiteral("id"), QStringLiteral("rotate-y")}, {QStringLiteral("kind"), QStringLiteral("point")},
            {QStringLiteral("x"), center.x() + 54}, {QStringLiteral("y"), center.y()}, {QStringLiteral("label"), QStringLiteral("Y %1°").arg(ass::VisualTags::formatNumber(fry))},
            {QStringLiteral("role"), QStringLiteral("rotate-y")}};
        m_features << QVariantMap{{QStringLiteral("id"), QStringLiteral("axis-x")}, {QStringLiteral("kind"), QStringLiteral("screen-line")},
                                   {QStringLiteral("x"), center.x()}, {QStringLiteral("y"), center.y()}, {QStringLiteral("x2"), center.x()}, {QStringLiteral("y2"), center.y() - 46}}
                   << QVariantMap{{QStringLiteral("id"), QStringLiteral("axis-y")}, {QStringLiteral("kind"), QStringLiteral("screen-line")},
                                   {QStringLiteral("x"), center.x()}, {QStringLiteral("y"), center.y()}, {QStringLiteral("x2"), center.x() + 46}, {QStringLiteral("y2"), center.y()}};
        m_features << xHandle << yHandle;
    } else if (m_activeToolId == QStringLiteral("scale")) {
        const qreal sx = ass::VisualTags::number(text, u"fscx").value_or(100.0);
        const qreal sy = ass::VisualTags::number(text, u"fscy").value_or(100.0);
        const ass::Document::Style style = m_document->styleForName(event.style);
        const QSizeF bounds = estimateSubtitleBounds(text, style, sx, sy);
        const qreal width = std::max<qreal>(48.0, bounds.width());
        const qreal height = std::max<qreal>(24.0, bounds.height());
        const int align = ass::VisualTags::number(text, u"an").value_or(style.alignment);
        const qreal left = align % 3 == 1 ? anchor.x() : align % 3 == 2 ? anchor.x() - width / 2 : anchor.x() - width;
        const qreal top = align >= 7 ? anchor.y() : align >= 4 ? anchor.y() - height / 2 : anchor.y() - height;
        addRect(QStringLiteral("scale-bounds"), QRectF(left, top, width, height));
        addPoint(QStringLiteral("scale-x"), QPointF(left + width, top + height / 2), QStringLiteral("%1% W").arg(ass::VisualTags::formatNumber(sx)), QStringLiteral("scale-x"));
        addPoint(QStringLiteral("scale-y"), QPointF(left + width / 2, top), QStringLiteral("%1% H").arg(ass::VisualTags::formatNumber(sy)), QStringLiteral("scale-y"));
        addPoint(QStringLiteral("scale-both"), QPointF(left + width, top), QStringLiteral("%1 × %2").arg(ass::VisualTags::formatNumber(sx), ass::VisualTags::formatNumber(sy)), QStringLiteral("scale-both"));
        if (m_dragging && m_dragRole == QStringLiteral("scale-target-rect"))
            addRect(QStringLiteral("scale-target"), QRectF(m_dragStartScript, m_viewport->screenToScript(m_pointer)).normalized());
    } else if (m_activeToolId == QStringLiteral("clip")) {
        const auto clip = ass::VisualTags::clip(text);
        if (clip && clip->rectangle) {
            addRect(QStringLiteral("clip-bounds"), clip->bounds);
            const QRectF r = clip->bounds.normalized();
            addPoint(QStringLiteral("clip-nw"), r.topLeft(), {}, QStringLiteral("clip-nw"));
            addPoint(QStringLiteral("clip-ne"), r.topRight(), {}, QStringLiteral("clip-ne"));
            addPoint(QStringLiteral("clip-sw"), r.bottomLeft(), {}, QStringLiteral("clip-sw"));
            addPoint(QStringLiteral("clip-se"), r.bottomRight(), {}, QStringLiteral("clip-se"));
            addPoint(QStringLiteral("clip-n"), QPointF(r.center().x(), r.top()), {}, QStringLiteral("clip-n"));
            addPoint(QStringLiteral("clip-s"), QPointF(r.center().x(), r.bottom()), {}, QStringLiteral("clip-s"));
            addPoint(QStringLiteral("clip-w"), QPointF(r.left(), r.center().y()), {}, QStringLiteral("clip-w"));
            addPoint(QStringLiteral("clip-e"), QPointF(r.right(), r.center().y()), {}, QStringLiteral("clip-e"));
            addPoint(QStringLiteral("clip-body"), r.center(), clip->inverse ? tr("Inverse clip") : tr("Clip"), QStringLiteral("clip-body"));
        } else {
            addPoint(QStringLiteral("clip-create"), m_viewport->screenToScript(m_pointer), tr("Drag to create clip"), QStringLiteral("clip-create"));
        }
    } else if (isVectorEditor(m_activeToolId)) {
        const bool isDrawing = isDrawingEditor(m_activeToolId);
        const auto clip = isDrawing ? std::optional<ass::VisualTags::Clip>{} : ass::VisualTags::clip(text);
        const auto drawing = isDrawing ? ass::VisualTags::drawing(text) : std::optional<ass::VisualTags::Drawing>{};
        const bool hasPath = isDrawing ? drawing.has_value() : clip && !clip->rectangle;
        if (hasPath) {
            const QString path = isDrawing ? drawing->path : clip->path;
            const int pathScale = isDrawing ? drawing->drawingScale : clip->drawingScale;
            const qreal factor = drawingScaleFactor(pathScale);
            const auto toScript = [isDrawing, &anchor, factor](const QPointF &point) {
                return (isDrawing ? anchor : QPointF{}) + point * factor;
            };
            const QVector<VectorPair> pairs = vectorPairs(path);
            for (int index = 0; index < pairs.size(); ++index)
                addPoint(QStringLiteral("vector-%1").arg(index), toScript(pairs[index].point),
                    QString::number(index + 1), QStringLiteral("vector-node"), index == m_selectedVectorPoint);
            QPointF current;
            bool hasCurrent = false;
            for (int index = 0; index < pairs.size();) {
                const QChar command = pairs[index].command;
                if (command == u'm' || command == u'n') {
                    current = pairs[index].point;
                    hasCurrent = true;
                    ++index;
                } else if (command == u'l' && hasCurrent) {
                    addLine(QStringLiteral("vector-segment-%1").arg(index), toScript(current), toScript(pairs[index].point), QStringLiteral("vector-path"));
                    current = pairs[index].point;
                    ++index;
                } else if (command == u'b' && hasCurrent && index + 2 < pairs.size()) {
                    const QPointF control1 = pairs[index].point;
                    const QPointF control2 = pairs[index + 1].point;
                    const QPointF endpoint = pairs[index + 2].point;
                    addLine(QStringLiteral("vector-control-a-%1").arg(index), toScript(current), toScript(control1));
                    addLine(QStringLiteral("vector-control-b-%1").arg(index), toScript(control2), toScript(endpoint));
                    QPointF previous = current;
                    for (int step = 1; step <= 16; ++step) {
                        const qreal t = step / 16.0;
                        const qreal u = 1.0 - t;
                        const QPointF point = current * (u * u * u) + control1 * (3 * u * u * t)
                            + control2 * (3 * u * t * t) + endpoint * (t * t * t);
                        addLine(QStringLiteral("vector-curve-%1-%2").arg(index).arg(step), toScript(previous), toScript(point), QStringLiteral("vector-path"));
                        previous = point;
                    }
                    current = endpoint;
                    index += 3;
                } else {
                    if (hasCurrent)
                        addLine(QStringLiteral("vector-control-%1").arg(index), toScript(current), toScript(pairs[index].point));
                    current = pairs[index].point;
                    ++index;
                }
            }
        }
        if (m_freehandDrawing && m_freehand.size() > 1) {
            for (int i = 1; i < m_freehand.size(); ++i)
                m_features.push_back(QVariantMap{{QStringLiteral("id"), QStringLiteral("freehand-%1").arg(i)},
                    {QStringLiteral("kind"), QStringLiteral("screen-line")},
                    {QStringLiteral("x"), m_freehand[i - 1].x()}, {QStringLiteral("y"), m_freehand[i - 1].y()},
                    {QStringLiteral("x2"), m_freehand[i].x()}, {QStringLiteral("y2"), m_freehand[i].y()}});
        }
    } else if (m_activeToolId == QStringLiteral("shift")) {
        addPoint(QStringLiteral("shift-origin"), anchor, tr("Drag to shift position, move, origin, and clips"), QStringLiteral("shift"));
        if (move) addLine(QStringLiteral("move-path"), move->start, move->end);
        if (const auto clip = ass::VisualTags::clip(text); clip && clip->rectangle) addRect(QStringLiteral("clip-bounds"), clip->bounds);
    }
    for (const QVariant &guide : std::as_const(m_guides)) {
        const QVariantMap guideMap = guide.toMap();
        if (guideMap.value(QStringLiteral("axis")).toString() == QStringLiteral("x")) {
            const qreal x = m_viewport->scriptToScreen(QPointF(guideMap.value(QStringLiteral("value")).toReal(), 0)).x();
            m_features.push_back(QVariantMap{{QStringLiteral("id"), QStringLiteral("snap-guide-x")},
                {QStringLiteral("kind"), QStringLiteral("screen-line")}, {QStringLiteral("x"), x},
                {QStringLiteral("y"), 0}, {QStringLiteral("x2"), x}, {QStringLiteral("y2"), m_viewport->viewportSize().height()},
                {QStringLiteral("snap"), true}});
        } else {
            const qreal y = m_viewport->scriptToScreen(QPointF(0, guideMap.value(QStringLiteral("value")).toReal())).y();
            m_features.push_back(QVariantMap{{QStringLiteral("id"), QStringLiteral("snap-guide-y")},
                {QStringLiteral("kind"), QStringLiteral("screen-line")}, {QStringLiteral("x"), 0},
                {QStringLiteral("y"), y}, {QStringLiteral("x2"), m_viewport->viewportSize().width()}, {QStringLiteral("y2"), y},
                {QStringLiteral("snap"), true}});
        }
    }
    emit overlayChanged();
}

void VisualToolManager::addPoint(QString id, QPointF scriptPoint, QString label, QString role, bool selected)
{
    if (!m_viewport) return;
    const QPointF screen = m_viewport->scriptToScreen(scriptPoint);
    m_features.push_back(QVariantMap{{QStringLiteral("id"), std::move(id)}, {QStringLiteral("kind"), QStringLiteral("point")},
        {QStringLiteral("x"), screen.x()}, {QStringLiteral("y"), screen.y()}, {QStringLiteral("scriptX"), scriptPoint.x()},
        {QStringLiteral("scriptY"), scriptPoint.y()}, {QStringLiteral("label"), std::move(label)},
        {QStringLiteral("role"), std::move(role)}, {QStringLiteral("selected"), selected}, {QStringLiteral("radius"), 6.0}});
}

void VisualToolManager::addLine(QString id, QPointF start, QPointF end, QString role)
{
    const bool guide = id.contains(QStringLiteral("control"));
    const QPointF a = m_viewport->scriptToScreen(start);
    const QPointF b = m_viewport->scriptToScreen(end);
    m_features.push_back(QVariantMap{{QStringLiteral("id"), std::move(id)}, {QStringLiteral("kind"), QStringLiteral("line")},
        {QStringLiteral("x"), a.x()}, {QStringLiteral("y"), a.y()}, {QStringLiteral("x2"), b.x()}, {QStringLiteral("y2"), b.y()},
        {QStringLiteral("guide"), guide},
        {QStringLiteral("role"), std::move(role)}});
}

void VisualToolManager::addRect(QString id, QRectF rect, QString role)
{
    const QPointF topLeft = m_viewport->scriptToScreen(rect.topLeft());
    const QPointF bottomRight = m_viewport->scriptToScreen(rect.bottomRight());
    m_features.push_back(QVariantMap{{QStringLiteral("id"), std::move(id)}, {QStringLiteral("kind"), QStringLiteral("rect")},
        {QStringLiteral("x"), topLeft.x()}, {QStringLiteral("y"), topLeft.y()},
        {QStringLiteral("width"), bottomRight.x() - topLeft.x()}, {QStringLiteral("height"), bottomRight.y() - topLeft.y()},
        {QStringLiteral("scriptX"), rect.x()}, {QStringLiteral("scriptY"), rect.y()},
        {QStringLiteral("scriptWidth"), rect.width()}, {QStringLiteral("scriptHeight"), rect.height()},
        {QStringLiteral("role"), std::move(role)}});
}

QVariantMap VisualToolManager::feature(const QString &id) const
{
    for (const QVariant &entry : m_features) {
        const QVariantMap map = entry.toMap();
        if (map.value(QStringLiteral("id")).toString() == id)
            return map;
    }
    return {};
}

QString VisualToolManager::hitFeature(const QPointF &point) const
{
    QString best;
    qreal bestDistance = 13.0;
    for (const QVariant &entry : m_features) {
        const QVariantMap map = entry.toMap();
        if (map.value(QStringLiteral("kind")).toString() != QStringLiteral("point")
            || map.value(QStringLiteral("role")).toString().isEmpty()) continue;
        const qreal d = distance(point, QPointF(map.value(QStringLiteral("x")).toReal(), map.value(QStringLiteral("y")).toReal()));
        if (d <= bestDistance) {
            bestDistance = d;
            best = map.value(QStringLiteral("id")).toString();
        }
    }
    if (best.isEmpty() && isVectorEditor(m_activeToolId)) {
        qreal nearest = 9.0;
        for (const QVariant &entry : m_features) {
            const QVariantMap map = entry.toMap();
            if (map.value(QStringLiteral("kind")).toString() != QStringLiteral("line")
                || map.value(QStringLiteral("role")).toString().isEmpty()) continue;
            const QPointF start(map.value(QStringLiteral("x")).toReal(), map.value(QStringLiteral("y")).toReal());
            const QPointF end(map.value(QStringLiteral("x2")).toReal(), map.value(QStringLiteral("y2")).toReal());
            const qreal d = distanceToSegment(point, start, end, nullptr);
            if (d <= nearest) {
                nearest = d;
                best = map.value(QStringLiteral("id")).toString();
            }
        }
    }
    if (best.isEmpty() && m_activeToolId == QStringLiteral("clip")) {
        for (const QVariant &entry : m_features) {
            const QVariantMap map = entry.toMap();
            if (map.value(QStringLiteral("id")).toString() == QStringLiteral("clip-bounds")
                && QRectF(map.value(QStringLiteral("x")).toReal(), map.value(QStringLiteral("y")).toReal(),
                          map.value(QStringLiteral("width")).toReal(), map.value(QStringLiteral("height")).toReal()).contains(point))
                return QStringLiteral("clip-body");
        }
    }
    if (best.isEmpty() && m_activeToolId == QStringLiteral("position")) return QStringLiteral("position");
    return best;
}

void VisualToolManager::beginDrag(const QString &role, const QPointF &screenPoint, int modifiers)
{
    if (!m_document || !m_viewport || role.isEmpty()) return;
    const ass::Event event = m_document->activeEventSnapshot();
    m_dragRole = role;
    m_originalText = event.text;
    m_dragText = event.text;
    m_dragStartScreen = screenPoint;
    m_dragStartScript = m_viewport->screenToScript(screenPoint);
    m_dragModifiers = modifiers;
    const QVariantMap handle = feature(m_selectedFeature);
    m_dragStartHandle = QPointF(handle.value(QStringLiteral("scriptX"), m_dragStartScript.x()).toReal(),
                                handle.value(QStringLiteral("scriptY"), m_dragStartScript.y()).toReal());
    if (role == QStringLiteral("position") || role == QStringLiteral("translate") || role == QStringLiteral("shift"))
        m_dragStartHandle = effectivePosition(event);
    else if (role == QStringLiteral("move-start")) m_dragStartHandle = ass::VisualTags::move(event.text)->start;
    else if (role == QStringLiteral("move-end")) m_dragStartHandle = ass::VisualTags::move(event.text)->end;
    else if (role.startsWith(QStringLiteral("vector-node")) && m_selectedVectorPoint >= 0) {
        if (isDrawingEditor(m_activeToolId)) {
            const auto drawing = ass::VisualTags::drawing(event.text);
            if (drawing) m_dragStartHandle = effectivePosition(event)
                + vectorPairs(drawing->path).value(m_selectedVectorPoint).point * drawingScaleFactor(drawing->drawingScale);
        } else {
            const auto clip = ass::VisualTags::clip(event.text);
            if (clip) m_dragStartHandle = vectorPairs(clip->path).value(m_selectedVectorPoint).point * drawingScaleFactor(clip->drawingScale);
        }
    }
    if (role == QStringLiteral("translate") && m_selectedFeature == QStringLiteral("move-start"))
        m_dragStartHandle = ass::VisualTags::move(event.text)->start;
    else if (role == QStringLiteral("translate") && m_selectedFeature == QStringLiteral("move-end"))
        m_dragStartHandle = ass::VisualTags::move(event.text)->end;
    m_transactionStarted = false;
}

void VisualToolManager::updateDrag(const QPointF &screenPoint, int modifiers)
{
    if (!m_document || distance(screenPoint, m_dragStartScreen) < 0.75) return;
    if (!m_transactionStarted) {
        if (!m_document->beginVisualTextEdit(m_document->lines()->activeId())) return;
        m_transactionStarted = true;
    }
    m_guides.clear();
    snapPoint(m_viewport->screenToScript(screenPoint), modifiers, &m_guides);
    const QString changed = editedText(screenPoint, modifiers);
    if (changed != m_dragText) {
        m_dragText = changed;
        m_document->previewVisualTextEdit(changed);
    }
}

QString VisualToolManager::editedText(const QPointF &screenPoint, int modifiers) const
{
    if (!m_document || !m_viewport) return m_originalText;
    const QPointF script = m_viewport->screenToScript(screenPoint);
    const QPointF delta = script - m_dragStartScript;
    QString text = m_originalText;
    if (m_dragRole == QStringLiteral("position")) {
        if (const auto move = ass::VisualTags::move(text)) {
            auto translated = *move;
            translated.start += delta;
            translated.end += delta;
            return ass::VisualTags::setMove(text, translated);
        }
        const QPointF value = snapPoint(m_dragStartHandle + delta, modifiers);
        return ass::VisualTags::setPoint(text, u"pos", value);
    }
    if (m_dragRole == QStringLiteral("create") && m_activeToolId == QStringLiteral("move")) {
        const ass::Event event = m_document->activeEventSnapshot();
        const qreal duration = std::max<qint64>(0, event.endMs - event.startMs);
        ass::VisualTags::Move created{m_dragStartScript, script, 0.0, duration};
        text = ass::VisualTags::removeTag(text, u"pos");
        return ass::VisualTags::setMove(text, created);
    }
    if (m_dragRole == QStringLiteral("translate")) {
        if (const auto move = ass::VisualTags::move(text)) {
            auto edited = *move;
            const QPointF translate = snapPoint(m_dragStartHandle + delta, modifiers) - m_dragStartHandle;
            if (m_selectedFeature == QStringLiteral("move-start")) edited.start += translate;
            else if (m_selectedFeature == QStringLiteral("move-end")) edited.end += translate;
            else { edited.start += translate; edited.end += translate; }
            return ass::VisualTags::setMove(text, edited);
        }
    }
    if (m_dragRole == QStringLiteral("move-start") || m_dragRole == QStringLiteral("move-end")) {
        auto move = ass::VisualTags::move(text);
        if (!move) return text;
        const QPointF value = snapPoint(m_dragStartHandle + delta, modifiers);
        if (m_dragRole == QStringLiteral("move-start")) move->start = value;
        else move->end = value;
        return ass::VisualTags::setMove(text, *move);
    }
    if (m_dragRole == QStringLiteral("rotation")) {
        const QPointF origin = ass::VisualTags::point(text, u"org").value_or(effectivePosition(m_document->activeEventSnapshot()));
        const qreal initialAngle = std::atan2(m_dragStartScript.y() - origin.y(), m_dragStartScript.x() - origin.x());
        const qreal currentAngle = std::atan2(script.y() - origin.y(), script.x() - origin.x());
        const qreal base = ass::VisualTags::number(text, u"frz").value_or(0.0);
        qreal value = base + qRadiansToDegrees(currentAngle - initialAngle);
        while (value > 180.0) value -= 360.0;
        while (value < -180.0) value += 360.0;
        return ass::VisualTags::setNumber(text, u"frz", value);
    }
    if (m_dragRole == QStringLiteral("origin"))
        return ass::VisualTags::setPoint(text, u"org", snapPoint(m_dragStartHandle + delta, modifiers));
    if (m_dragRole == QStringLiteral("rotate-x") || m_dragRole == QStringLiteral("rotate-y")) {
        if ((m_contextOption == QStringLiteral("x") && m_dragRole == QStringLiteral("rotate-y"))
            || (m_contextOption == QStringLiteral("y") && m_dragRole == QStringLiteral("rotate-x")))
            return text;
        const qreal change = m_dragRole == QStringLiteral("rotate-x")
            ? (m_dragStartScreen.y() - screenPoint.y()) * 0.4
            : (screenPoint.x() - m_dragStartScreen.x()) * 0.4;
        const QStringView tag = m_dragRole == QStringLiteral("rotate-x") ? u"frx" : u"fry";
        const qreal value = ass::VisualTags::number(text, tag).value_or(0.0) + change;
        return ass::VisualTags::setNumber(text, tag, value);
    }
    if (m_dragRole.startsWith(QStringLiteral("scale-"))) {
        const qreal oldX = ass::VisualTags::number(text, u"fscx").value_or(100.0);
        const qreal oldY = ass::VisualTags::number(text, u"fscy").value_or(100.0);
        const qreal factorX = std::exp((screenPoint.x() - m_dragStartScreen.x()) / 130.0);
        const qreal factorY = std::exp((m_dragStartScreen.y() - screenPoint.y()) / 130.0);
        const bool lock = (modifiers & Qt::ShiftModifier) != 0;
        if (m_dragRole == QStringLiteral("scale-x")) {
            if (m_scaleAxis == QStringLiteral("y")) return text;
            text = ass::VisualTags::setNumber(text, u"fscx", qBound<qreal>(1.0, oldX * factorX, 1000.0));
            if (lock) text = ass::VisualTags::setNumber(text, u"fscy", qBound<qreal>(1.0, oldY * factorX, 1000.0));
        } else if (m_dragRole == QStringLiteral("scale-y")) {
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
    if (m_dragRole.startsWith(QStringLiteral("clip-")) || m_dragRole == QStringLiteral("clip-create")) {
        auto clip = ass::VisualTags::clip(text);
        if (!clip || !clip->rectangle) {
            clip = ass::VisualTags::Clip{};
            clip->rectangle = true;
            clip->bounds = QRectF(m_dragStartScript, script).normalized();
            return ass::VisualTags::setClip(text, *clip);
        }
        QRectF r = clip->bounds.normalized();
        if (m_dragRole == QStringLiteral("clip-body")) r.translate(delta);
        else if (m_dragRole == QStringLiteral("clip-nw")) r.setTopLeft(r.topLeft() + delta);
        else if (m_dragRole == QStringLiteral("clip-ne")) r.setTopRight(r.topRight() + delta);
        else if (m_dragRole == QStringLiteral("clip-sw")) r.setBottomLeft(r.bottomLeft() + delta);
        else if (m_dragRole == QStringLiteral("clip-se")) r.setBottomRight(r.bottomRight() + delta);
        else if (m_dragRole == QStringLiteral("clip-n")) r.setTop(r.top() + delta.y());
        else if (m_dragRole == QStringLiteral("clip-s")) r.setBottom(r.bottom() + delta.y());
        else if (m_dragRole == QStringLiteral("clip-w")) r.setLeft(r.left() + delta.x());
        else if (m_dragRole == QStringLiteral("clip-e")) r.setRight(r.right() + delta.x());
        else r = QRectF(m_dragStartScript, script).normalized();
        clip->bounds = r.normalized();
        return ass::VisualTags::setClip(text, *clip);
    }
    if (m_dragRole == QStringLiteral("vector-node")) {
        if (isDrawingEditor(m_activeToolId)) {
            const auto drawing = ass::VisualTags::drawing(text);
            if (!drawing) return text;
            const QPointF anchor = effectivePosition(m_document->activeEventSnapshot());
            const QPointF local = (m_dragStartHandle + delta - anchor) / drawingScaleFactor(drawing->drawingScale);
            return ass::VisualTags::setDrawingPath(text,
                setVectorPair(drawing->path, m_selectedVectorPoint, local), drawing->drawingScale);
        }
        auto clip = ass::VisualTags::clip(text);
        if (!clip || clip->rectangle) return text;
        clip->path = setVectorPair(clip->path, m_selectedVectorPoint,
            (m_dragStartHandle + delta) / drawingScaleFactor(clip->drawingScale));
        return ass::VisualTags::setClip(text, *clip);
    }
    if (m_dragRole == QStringLiteral("vector-path")) {
        if (isDrawingEditor(m_activeToolId)) {
            const auto drawing = ass::VisualTags::drawing(text);
            if (!drawing) return text;
            const qreal factor = drawingScaleFactor(drawing->drawingScale);
            QString path = drawing->path;
            const auto pairs = vectorPairs(path);
            for (int i = pairs.size() - 1; i >= 0; --i)
                path = setVectorPair(path, i, pairs[i].point + delta / factor);
            return ass::VisualTags::setDrawingPath(text, path, drawing->drawingScale);
        }
        auto clip = ass::VisualTags::clip(text);
        if (!clip || clip->rectangle) return text;
        const qreal factor = drawingScaleFactor(clip->drawingScale);
        const auto pairs = vectorPairs(clip->path);
        for (int i = pairs.size() - 1; i >= 0; --i)
            clip->path = setVectorPair(clip->path, i, pairs[i].point + delta / factor);
        return ass::VisualTags::setClip(text, *clip);
    }
    if (m_dragRole == QStringLiteral("shift")) {
        const QPointF shift = snapPoint(m_dragStartHandle + delta, modifiers) - m_dragStartHandle;
        const auto move = ass::VisualTags::move(text);
        if (move) {
            auto edited = *move;
            if (m_shiftComponents.value(QStringLiteral("shift-move-start"), true)) edited.start += shift;
            if (m_shiftComponents.value(QStringLiteral("shift-move-end"), true)) edited.end += shift;
            text = ass::VisualTags::setMove(text, edited);
        }
        const auto positionTag = ass::VisualTags::point(text, u"pos");
        if ((!move || positionTag) && m_shiftComponents.value(QStringLiteral("shift-pos"), true)) {
            const QPointF position = positionTag.value_or(effectivePosition(m_document->activeEventSnapshot()));
            text = ass::VisualTags::setPoint(text, u"pos", position + shift);
        }
        if (const auto org = ass::VisualTags::point(text, u"org"); org
            && m_shiftComponents.value(QStringLiteral("shift-org"), true))
            text = ass::VisualTags::setPoint(text, u"org", *org + shift);
        if (auto clip = ass::VisualTags::clip(text); clip
            && m_shiftComponents.value(QStringLiteral("shift-clip"), true)) {
            if (clip->rectangle) clip->bounds.translate(shift);
            else {
                auto pairs = vectorPairs(clip->path);
                const qreal factor = drawingScaleFactor(clip->drawingScale);
                for (int i = pairs.size() - 1; i >= 0; --i)
                    clip->path = setVectorPair(clip->path, i, pairs[i].point + shift / factor);
            }
            text = ass::VisualTags::setClip(text, *clip);
        }
        if (const auto drawing = ass::VisualTags::drawing(text); drawing
            && m_shiftComponents.value(QStringLiteral("shift-drawing"), true)) {
            QString path = drawing->path;
            const auto pairs = vectorPairs(path);
            const qreal factor = drawingScaleFactor(drawing->drawingScale);
            for (int i = pairs.size() - 1; i >= 0; --i)
                path = setVectorPair(path, i, pairs[i].point + shift / factor);
            text = ass::VisualTags::setDrawingPath(text, path, drawing->drawingScale);
        }
        return text;
    }
    return text;
}

QPointF VisualToolManager::effectivePosition(const ass::Event &event) const
{
    const QString text = event.text;
    if (const auto move = ass::VisualTags::move(text)) {
        const qreal t0 = move->startMs.value_or(0.0);
        const qreal t1 = move->endMs.value_or(static_cast<qreal>(std::max<qint64>(0, event.endMs - event.startMs)));
        const qreal now = m_document && m_document->media() ? m_document->media()->displayedFrameStartMs() - event.startMs : 0.0;
        const qreal amount = t1 <= t0 ? 0.0 : qBound<qreal>(0.0, (now - t0) / (t1 - t0), 1.0);
        return move->start + (move->end - move->start) * amount;
    }
    if (const auto position = ass::VisualTags::point(text, u"pos")) return *position;
    const auto style = m_document->styleForName(event.style);
    const int alignment = ass::VisualTags::number(text, u"an").value_or(style.alignment);
    const auto project = m_document->projectProperties();
    const qreal width = project.playResX;
    const qreal height = project.playResY;
    const int marginLeft = event.marginLeft > 0 ? event.marginLeft : style.marginLeft;
    const int marginRight = event.marginRight > 0 ? event.marginRight : style.marginRight;
    const int marginV = event.marginVertical > 0 ? event.marginVertical : style.marginVertical;
    const int column = (alignment - 1) % 3;
    const int row = (alignment - 1) / 3;
    const qreal x = column == 0 ? marginLeft : column == 1 ? width / 2.0 : width - marginRight;
    const qreal y = row == 0 ? height - marginV : row == 1 ? height / 2.0 : marginV;
    return QPointF(x, y);
}

QPointF VisualToolManager::snapPoint(QPointF point, int modifiers, QVariantList *guides) const
{
    const auto result = m_snapService.snap(point, (modifiers & Qt::AltModifier) != 0);
    if (guides) *guides = result.guides;
    return result.point;
}

void VisualToolManager::finishFreehand()
{
    m_freehandDrawing = false;
    if (!m_document || !m_viewport || m_freehand.size() < 2) return;
    if (!m_document->beginVisualTextEdit(m_document->lines()->activeId())) return;
    if (isDrawingEditor(m_activeToolId)) {
        const ass::Event event = m_document->activeEventSnapshot();
        const auto existing = ass::VisualTags::drawing(m_document->activeText());
        const int scale = existing ? existing->drawingScale : 1;
        const qreal factor = drawingScaleFactor(scale);
        const QPointF anchor = effectivePosition(event);
        const bool smooth = m_contextOption == QStringLiteral("smooth-freehand");
        QVector<QPointF> points = m_freehand;
        if (smooth) points = simplifyPath(points, 2.5);
        QString path = QStringLiteral("m ");
        for (qsizetype i = 0; i < points.size(); ++i) {
            const QPointF script = (m_viewport->screenToScript(points[i]) - anchor) / factor;
            if (i > 0) path += QStringLiteral(" l ");
            path += ass::VisualTags::formatNumber(script.x()) + u' ' + ass::VisualTags::formatNumber(script.y());
        }
        const QString updated = ass::VisualTags::setDrawingPath(m_document->activeText(), path, scale);
        m_document->previewVisualTextEdit(updated);
        m_document->commitVisualTextEdit();
        m_freehand.clear();
        return;
    }
    auto clip = ass::VisualTags::clip(m_document->activeText()).value_or(ass::VisualTags::Clip{});
    clip.rectangle = false;
    clip.drawingScale = 1;
    const bool smooth = m_contextOption == QStringLiteral("smooth-freehand");
    QVector<QPointF> points = m_freehand;
    if (smooth) points = simplifyPath(points, 2.5);
    QString path = QStringLiteral("m ");
    for (qsizetype i = 0; i < points.size(); ++i) {
        const QPointF script = m_viewport->screenToScript(points[i]);
        if (i > 0) path += QStringLiteral(" l ");
        path += ass::VisualTags::formatNumber(script.x()) + u' ' + ass::VisualTags::formatNumber(script.y());
    }
    clip.path = path;
    m_document->previewVisualTextEdit(ass::VisualTags::setClip(m_document->activeText(), clip));
    m_document->commitVisualTextEdit();
    m_freehand.clear();
}

void VisualToolManager::commitScaleRectangle(const QPointF &scriptEnd, int modifiers)
{
    if (!m_document) return;
    const ass::Event event = m_document->activeEventSnapshot();
    const QString original = event.text;
    const auto style = m_document->styleForName(event.style);
    const qreal oldX = ass::VisualTags::number(original, u"fscx").value_or(100.0);
    const qreal oldY = ass::VisualTags::number(original, u"fscy").value_or(100.0);
    const QSizeF currentBounds = estimateSubtitleBounds(original, style, oldX, oldY);
    const qreal currentWidth = currentBounds.width();
    const qreal currentHeight = currentBounds.height();
    const QRectF target(m_dragStartScript, scriptEnd);
    if (target.width() < 1.0 || target.height() < 1.0) return;
    const qreal factorX = target.width() / currentWidth;
    const qreal factorY = target.height() / currentHeight;
    const bool aspectLock = (modifiers & Qt::ShiftModifier) != 0;
    const qreal uniform = std::min(factorX, factorY);
    QString updated = original;
    if (m_scaleAxis != QStringLiteral("y"))
        updated = ass::VisualTags::setNumber(updated, u"fscx", qBound<qreal>(1.0, oldX * (aspectLock ? uniform : factorX), 1000.0));
    if (m_scaleAxis != QStringLiteral("x"))
        updated = ass::VisualTags::setNumber(updated, u"fscy", qBound<qreal>(1.0, oldY * (aspectLock ? uniform : factorY), 1000.0));
    if (updated != original && m_document->beginVisualTextEdit(m_document->lines()->activeId())) {
        m_document->previewVisualTextEdit(updated);
        m_document->commitVisualTextEdit();
    }
    m_scaleRectMode = false;
    emit contextOptionsChanged();
}

void VisualToolManager::appendVectorPoint(const QPointF &scriptPoint, bool bezier)
{
    if (!m_document) return;
    if (isDrawingEditor(m_activeToolId)) {
        const ass::Event event = m_document->activeEventSnapshot();
        const QString original = m_document->activeText();
        const auto existing = ass::VisualTags::drawing(original);
        const int scale = existing ? existing->drawingScale : 1;
        const qreal factor = drawingScaleFactor(scale);
        const QPointF pathPoint = (scriptPoint - effectivePosition(event)) / factor;
        QString path = existing ? existing->path.trimmed() : QString{};
        if (bezier && path.isEmpty()) {
            path = QStringLiteral("m %1 %2").arg(ass::VisualTags::formatNumber(pathPoint.x()), ass::VisualTags::formatNumber(pathPoint.y()));
        } else if (bezier) {
            m_bezierPoints.push_back(pathPoint);
            if (m_bezierPoints.size() < 3) return;
            const QPointF a = m_bezierPoints[0], b = m_bezierPoints[1], c = m_bezierPoints[2];
            path += QStringLiteral(" b %1 %2 %3 %4 %5 %6")
                .arg(ass::VisualTags::formatNumber(a.x()), ass::VisualTags::formatNumber(a.y()),
                     ass::VisualTags::formatNumber(b.x()), ass::VisualTags::formatNumber(b.y()),
                     ass::VisualTags::formatNumber(c.x()), ass::VisualTags::formatNumber(c.y()));
            m_bezierPoints.clear();
        } else if (path.isEmpty()) {
            path = QStringLiteral("m %1 %2").arg(ass::VisualTags::formatNumber(pathPoint.x()), ass::VisualTags::formatNumber(pathPoint.y()));
        } else {
            const QVector<VectorPair> pairs = vectorPairs(path);
            int insertionPair = -1;
            qreal nearestDistance = 12.0;
            for (int i = 1; i < pairs.size(); ++i) {
                if (pairs[i].command != u'l') continue;
                const QPointF a = m_viewport->scriptToScreen(effectivePosition(event) + pairs[i - 1].point * factor);
                const QPointF b = m_viewport->scriptToScreen(effectivePosition(event) + pairs[i].point * factor);
                const qreal d = distanceToSegment(m_pointer, a, b, nullptr);
                if (d < nearestDistance) { nearestDistance = d; insertionPair = i; }
            }
            if (insertionPair >= 0) {
                path.insert(pairs[insertionPair].xStart, QStringLiteral("%1 %2 ").arg(
                    ass::VisualTags::formatNumber(pathPoint.x()), ass::VisualTags::formatNumber(pathPoint.y())));
            } else {
                path += QStringLiteral(" l %1 %2").arg(ass::VisualTags::formatNumber(pathPoint.x()), ass::VisualTags::formatNumber(pathPoint.y()));
            }
        }
        const QString updated = ass::VisualTags::setDrawingPath(original, path, scale);
        if (updated != original && m_document->beginVisualTextEdit(m_document->lines()->activeId())) {
            m_document->previewVisualTextEdit(updated);
            m_document->commitVisualTextEdit();
        }
        return;
    }
    auto clip = ass::VisualTags::clip(m_document->activeText()).value_or(ass::VisualTags::Clip{});
    clip.rectangle = false;
    const qreal factor = drawingScaleFactor(clip.drawingScale);
    const QPointF pathPoint = scriptPoint / factor;
    QString path = clip.path.trimmed();
    if (bezier) {
        if (path.isEmpty() && m_bezierPoints.isEmpty()) {
            path = QStringLiteral("m %1 %2").arg(ass::VisualTags::formatNumber(pathPoint.x()), ass::VisualTags::formatNumber(pathPoint.y()));
            clip.path = path;
            if (m_document->beginVisualTextEdit(m_document->lines()->activeId())) {
                m_document->previewVisualTextEdit(ass::VisualTags::setClip(m_document->activeText(), clip));
                m_document->commitVisualTextEdit();
            }
            return;
        }
        m_bezierPoints.push_back(pathPoint);
        if (m_bezierPoints.size() < 3) {
            emit contextOptionsChanged();
            return;
        }
        if (path.isEmpty()) {
            const QPointF start = m_bezierPoints.front();
            path = QStringLiteral("m %1 %2").arg(ass::VisualTags::formatNumber(start.x()), ass::VisualTags::formatNumber(start.y()));
        }
        const QPointF a = m_bezierPoints[0], b = m_bezierPoints[1], c = m_bezierPoints[2];
        path += QStringLiteral(" b %1 %2 %3 %4 %5 %6")
            .arg(ass::VisualTags::formatNumber(a.x()), ass::VisualTags::formatNumber(a.y()),
                 ass::VisualTags::formatNumber(b.x()), ass::VisualTags::formatNumber(b.y()),
                 ass::VisualTags::formatNumber(c.x()), ass::VisualTags::formatNumber(c.y()));
        m_bezierPoints.clear();
    } else {
        if (path.isEmpty()) path = QStringLiteral("m %1 %2").arg(ass::VisualTags::formatNumber(pathPoint.x()), ass::VisualTags::formatNumber(pathPoint.y()));
        else {
            const QVector<VectorPair> pairs = vectorPairs(path);
            int insertionPair = -1;
            qreal nearestDistance = 12.0;
            for (int i = 1; i < pairs.size(); ++i) {
                if (pairs[i].command != u'l') continue;
                const QPointF a = m_viewport->scriptToScreen(pairs[i - 1].point * factor);
                const QPointF b = m_viewport->scriptToScreen(pairs[i].point * factor);
                const qreal d = distanceToSegment(m_pointer, a, b, nullptr);
                if (d < nearestDistance) { nearestDistance = d; insertionPair = i; }
            }
            if (insertionPair >= 0) {
                const VectorPair &at = pairs[insertionPair];
                path.insert(at.xStart, QStringLiteral("%1 %2 ").arg(
                    ass::VisualTags::formatNumber(pathPoint.x()), ass::VisualTags::formatNumber(pathPoint.y())));
            } else {
                path += QStringLiteral(" l %1 %2").arg(ass::VisualTags::formatNumber(pathPoint.x()), ass::VisualTags::formatNumber(pathPoint.y()));
            }
        }
    }
    clip.path = path;
    if (m_document->beginVisualTextEdit(m_document->lines()->activeId())) {
        m_document->previewVisualTextEdit(ass::VisualTags::setClip(m_document->activeText(), clip));
        m_document->commitVisualTextEdit();
    }
}

bool VisualToolManager::deleteSelectedVectorPoint()
{
    if (!m_document || m_selectedVectorPoint <= 0) return false;
    if (isDrawingEditor(m_activeToolId)) {
        const auto drawing = ass::VisualTags::drawing(m_document->activeText());
        if (!drawing) return false;
        const QVector<VectorPair> pairs = vectorPairs(drawing->path);
        if (m_selectedVectorPoint >= pairs.size() || pairs[m_selectedVectorPoint].command != u'l' || pairs.size() <= 2)
            return false;
        const VectorPair pair = pairs[m_selectedVectorPoint];
        QString path = drawing->path;
        qsizetype start = pair.xStart;
        if (start > 0 && path.at(start - 1).isSpace()) --start;
        path.remove(start, pair.yStart + pair.yLength - start);
        if (!m_document->beginVisualTextEdit(m_document->lines()->activeId())) return false;
        m_document->previewVisualTextEdit(ass::VisualTags::setDrawingPath(m_document->activeText(), path, drawing->drawingScale));
        m_document->commitVisualTextEdit();
        return true;
    }
    auto clip = ass::VisualTags::clip(m_document->activeText());
    if (!clip || clip->rectangle) return false;
    QVector<VectorPair> pairs = vectorPairs(clip->path);
    if (m_selectedVectorPoint >= pairs.size() || pairs[m_selectedVectorPoint].command != u'l' || pairs.size() <= 2)
        return false;
    const VectorPair pair = pairs[m_selectedVectorPoint];
    QString path = clip->path;
    qsizetype start = pair.xStart;
    if (start > 0 && path.at(start - 1).isSpace()) --start;
    path.remove(start, pair.yStart + pair.yLength - start);
    clip->path = path;
    if (!m_document->beginVisualTextEdit(m_document->lines()->activeId())) return false;
    m_document->previewVisualTextEdit(ass::VisualTags::setClip(m_document->activeText(), *clip));
    m_document->commitVisualTextEdit();
    return true;
}

} // namespace yoake::ui
