#include "ui/visual_tool_base.h"

#include "app/document_context.h"
#include "ass/visual_tags.h"
#include "media/media_session.h"
#include "models/subtitle_model.h"
#include "ui/video_viewport.h"

#include <QtCore/QLineF>
#include <QtCore/QCoreApplication>
#include <QtCore/QRegularExpression>
#include <QtCore/QtMath>
#include <QtGui/QFont>
#include <QtGui/QFontMetricsF>

#include <algorithm>
#include <cmath>

namespace yoake::ui {
namespace {

QString plainTextForMetrics(QString text)
{
    static const QRegularExpression blocks(QStringLiteral("\\{[^}]*\\}"));
    text.remove(blocks);
    text.replace(QStringLiteral("\\N"), QStringLiteral("\n"), Qt::CaseInsensitive);
    text.replace(QStringLiteral("\\n"), QStringLiteral("\n"), Qt::CaseInsensitive);
    return text;
}

qreal pointDistance(QPointF first, QPointF second)
{
    return QLineF(first, second).length();
}

qreal distanceToSegment(QPointF point, QPointF start, QPointF end)
{
    const QPointF delta = end - start;
    const qreal lengthSquared = QPointF::dotProduct(delta, delta);
    const qreal t = lengthSquared <= 0.0001 ? 0.0
        : qBound<qreal>(0.0, QPointF::dotProduct(point - start, delta) / lengthSquared, 1.0);
    return pointDistance(point, start + delta * t);
}

QString numericText(QPointF point)
{
    return QStringLiteral("%1, %2").arg(ass::VisualTags::formatNumber(point.x()),
                                       ass::VisualTags::formatNumber(point.y()));
}

} // namespace

VisualToolBase::VisualToolBase(app::DocumentContext *document, VideoViewport *viewport)
    : m_document(document), m_viewport(viewport), m_snapService(viewport)
{
}

QString VisualToolBase::tr(const char *sourceText)
{
    return QCoreApplication::translate("VisualToolManager", sourceText);
}

QVariantList VisualToolBase::renderOverlay() const
{
    QVariantList features;
    m_coordinateLabel.clear();
    if (!document() || !viewport() || viewport()->videoSize().isEmpty())
        return features;
    buildOverlay(features);
    return addSnapGuides(std::move(features));
}

void VisualToolBase::activeLineChanged()
{
    if (document() && document()->lines()->activeId() != m_editActiveId)
        cancelOperation();
    m_guides.clear();
}

void VisualToolBase::deactivate()
{
    m_guides.clear();
}

void VisualToolBase::commitOperation()
{
    if (!m_editStarted || !document()) {
        m_guides.clear();
        return;
    }
    m_editStarted = false;
    m_editActiveId.clear();
    document()->commitVisualTextEdit();
    m_guides.clear();
}

void VisualToolBase::cancelOperation()
{
    if (!m_editStarted || !document()) {
        m_guides.clear();
        return;
    }
    m_editStarted = false;
    m_editActiveId.clear();
    document()->cancelVisualTextEdit();
    m_guides.clear();
}

QString VisualToolBase::activeText() const
{
    return document() ? document()->activeText() : QString{};
}

ass::Event VisualToolBase::activeEvent() const
{
    return document() ? document()->activeEventSnapshot() : ass::Event{};
}

QPointF VisualToolBase::toScript(QPointF screen) const
{
    return viewport() ? viewport()->screenToScript(screen) : QPointF{};
}

QPointF VisualToolBase::toScreen(QPointF script) const
{
    return viewport() ? viewport()->scriptToScreen(script) : QPointF{};
}

QPointF VisualToolBase::effectivePosition(const ass::Event &event) const
{
    const QString text = event.text;
    if (const auto move = ass::VisualTags::move(text)) {
        const qreal t0 = move->startMs.value_or(0.0);
        const qreal duration = static_cast<qreal>(std::max<qint64>(0, event.endMs - event.startMs));
        const qreal t1 = move->endMs.value_or(duration);
        const qreal now = document() && document()->media()
            ? document()->media()->displayedFrameStartMs() - event.startMs : 0.0;
        const qreal amount = t1 <= t0 ? 0.0 : qBound<qreal>(0.0, (now - t0) / (t1 - t0), 1.0);
        return move->start + (move->end - move->start) * amount;
    }
    if (const auto position = ass::VisualTags::point(text, u"pos"))
        return *position;
    if (!document())
        return {};
    const auto style = document()->styleForName(event.style);
    const int alignment = ass::VisualTags::number(text, u"an").value_or(style.alignment);
    const auto project = document()->projectProperties();
    const int marginLeft = event.marginLeft > 0 ? event.marginLeft : style.marginLeft;
    const int marginRight = event.marginRight > 0 ? event.marginRight : style.marginRight;
    const int marginV = event.marginVertical > 0 ? event.marginVertical : style.marginVertical;
    const int column = (alignment - 1) % 3;
    const int row = (alignment - 1) / 3;
    const qreal x = column == 0 ? marginLeft : column == 1 ? project.playResX / 2.0 : project.playResX - marginRight;
    const qreal y = row == 0 ? project.playResY - marginV : row == 1 ? project.playResY / 2.0 : marginV;
    return QPointF(x, y);
}

QSizeF VisualToolBase::estimateSubtitleBounds(const QString &text, const ass::Document::Style &style,
                                               qreal scaleX, qreal scaleY) const
{
    QFont font(style.fontName);
    font.setPixelSize(std::max(1, qRound(style.fontSize)));
    const QFontMetricsF metrics(font);
    const QStringList lines = plainTextForMetrics(text).split(u'\n', Qt::KeepEmptyParts);
    qreal width = 0.0;
    for (const QString &line : lines)
        width = std::max(width, metrics.horizontalAdvance(line));
    const qreal height = std::max<qsizetype>(1, lines.size()) * metrics.lineSpacing();
    return QSizeF(std::max<qreal>(1.0, width * style.scaleX / 100.0 * scaleX / 100.0),
                  std::max<qreal>(1.0, height * style.scaleY / 100.0 * scaleY / 100.0));
}

QPointF VisualToolBase::snap(QPointF point, int modifiers) const
{
    const auto result = m_snapService.snap(point, (modifiers & Qt::AltModifier) != 0);
    m_guides = result.guides;
    return result.point;
}

void VisualToolBase::addPoint(QVariantList &features, QString id, QPointF scriptPoint,
                              QString label, QString role, bool selected, QString nodeType) const
{
    addScreenPoint(features, std::move(id), toScreen(scriptPoint), std::move(label),
                   std::move(role), selected, std::move(nodeType));
    QVariantMap last = features.last().toMap();
    last.insert(QStringLiteral("scriptX"), scriptPoint.x());
    last.insert(QStringLiteral("scriptY"), scriptPoint.y());
    features.last() = last;
}

void VisualToolBase::addScreenPoint(QVariantList &features, QString id, QPointF screenPoint,
                                    QString label, QString role, bool selected, QString nodeType) const
{
    features.push_back(QVariantMap{{QStringLiteral("id"), std::move(id)}, {QStringLiteral("kind"), QStringLiteral("point")},
        {QStringLiteral("x"), screenPoint.x()}, {QStringLiteral("y"), screenPoint.y()},
        {QStringLiteral("label"), std::move(label)}, {QStringLiteral("role"), std::move(role)},
        {QStringLiteral("selected"), selected},
        {QStringLiteral("radius"), nodeType == QStringLiteral("control") ? 4.5 : 6.0},
        {QStringLiteral("nodeType"), std::move(nodeType)}});
}

void VisualToolBase::addLine(QVariantList &features, QString id, QPointF start, QPointF end,
                             QString role) const
{
    addScreenLine(features, std::move(id), toScreen(start), toScreen(end), std::move(role));
    QVariantMap last = features.last().toMap();
    if (last.value(QStringLiteral("id")).toString().contains(QStringLiteral("control")))
        last.insert(QStringLiteral("guide"), true);
    features.last() = last;
}

void VisualToolBase::addScreenLine(QVariantList &features, QString id, QPointF start, QPointF end,
                                   QString role) const
{
    features.push_back(QVariantMap{{QStringLiteral("id"), std::move(id)}, {QStringLiteral("kind"), QStringLiteral("line")},
        {QStringLiteral("x"), start.x()}, {QStringLiteral("y"), start.y()},
        {QStringLiteral("x2"), end.x()}, {QStringLiteral("y2"), end.y()},
        {QStringLiteral("role"), std::move(role)}});
}

void VisualToolBase::addRect(QVariantList &features, QString id, QRectF rect, QString role) const
{
    const QPointF topLeft = toScreen(rect.topLeft());
    const QPointF bottomRight = toScreen(rect.bottomRight());
    features.push_back(QVariantMap{{QStringLiteral("id"), std::move(id)}, {QStringLiteral("kind"), QStringLiteral("rect")},
        {QStringLiteral("x"), topLeft.x()}, {QStringLiteral("y"), topLeft.y()},
        {QStringLiteral("width"), bottomRight.x() - topLeft.x()}, {QStringLiteral("height"), bottomRight.y() - topLeft.y()},
        {QStringLiteral("scriptX"), rect.x()}, {QStringLiteral("scriptY"), rect.y()},
        {QStringLiteral("scriptWidth"), rect.width()}, {QStringLiteral("scriptHeight"), rect.height()},
        {QStringLiteral("role"), std::move(role)}});
}

QVariantMap VisualToolBase::feature(const QVariantList &features, const QString &id) const
{
    for (const QVariant &entry : features) {
        const QVariantMap map = entry.toMap();
        if (map.value(QStringLiteral("id")).toString() == id)
            return map;
    }
    return {};
}

QString VisualToolBase::hitPoint(const QVariantList &features, QPointF point, qreal tolerance) const
{
    QString best;
    qreal bestDistance = tolerance;
    for (const QVariant &entry : features) {
        const QVariantMap map = entry.toMap();
        if (map.value(QStringLiteral("kind")).toString() != QStringLiteral("point")
            || map.value(QStringLiteral("role")).toString().isEmpty())
            continue;
        const qreal distance = pointDistance(point, QPointF(map.value(QStringLiteral("x")).toReal(),
                                                            map.value(QStringLiteral("y")).toReal()));
        if (distance <= bestDistance) {
            bestDistance = distance;
            best = map.value(QStringLiteral("id")).toString();
        }
    }
    return best;
}

qreal VisualToolBase::hitLine(const QVariantList &features, QPointF point,
                              QString *featureId, qreal tolerance) const
{
    qreal nearest = tolerance;
    QString best;
    for (const QVariant &entry : features) {
        const QVariantMap map = entry.toMap();
        if (map.value(QStringLiteral("kind")).toString() != QStringLiteral("line")
            || map.value(QStringLiteral("role")).toString().isEmpty())
            continue;
        const qreal distance = distanceToSegment(point,
            QPointF(map.value(QStringLiteral("x")).toReal(), map.value(QStringLiteral("y")).toReal()),
            QPointF(map.value(QStringLiteral("x2")).toReal(), map.value(QStringLiteral("y2")).toReal()));
        if (distance <= nearest) {
            nearest = distance;
            best = map.value(QStringLiteral("id")).toString();
        }
    }
    if (featureId)
        *featureId = best;
    return nearest;
}

QVariantList VisualToolBase::addSnapGuides(QVariantList features) const
{
    if (!viewport())
        return features;
    for (const QVariant &guide : m_guides) {
        const QVariantMap map = guide.toMap();
        if (map.value(QStringLiteral("axis")).toString() == QStringLiteral("x")) {
            const qreal x = toScreen(QPointF(map.value(QStringLiteral("value")).toReal(), 0)).x();
            features.push_back(QVariantMap{{QStringLiteral("id"), QStringLiteral("snap-guide-x")},
                {QStringLiteral("kind"), QStringLiteral("screen-line")}, {QStringLiteral("x"), x},
                {QStringLiteral("y"), 0}, {QStringLiteral("x2"), x},
                {QStringLiteral("y2"), viewport()->viewportSize().height()}, {QStringLiteral("snap"), true}});
        } else {
            const qreal y = toScreen(QPointF(0, map.value(QStringLiteral("value")).toReal())).y();
            features.push_back(QVariantMap{{QStringLiteral("id"), QStringLiteral("snap-guide-y")},
                {QStringLiteral("kind"), QStringLiteral("screen-line")}, {QStringLiteral("x"), 0},
                {QStringLiteral("y"), y}, {QStringLiteral("x2"), viewport()->viewportSize().width()},
                {QStringLiteral("y2"), y}, {QStringLiteral("snap"), true}});
        }
    }
    return features;
}

bool VisualToolBase::beginTextEdit()
{
    if (!document() || m_editStarted)
        return false;
    m_editActiveId = document()->lines()->activeId();
    if (!document()->beginVisualTextEdit(m_editActiveId)) {
        m_editActiveId.clear();
        return false;
    }
    m_editStarted = true;
    return true;
}

bool VisualToolBase::beginTextEditGroup(const QStringList &eventIds)
{
    if (!document() || m_editStarted)
        return false;
    m_editActiveId = document()->lines()->activeId();
    if (!document()->beginVisualTextEditGroup(eventIds)) {
        m_editActiveId.clear();
        return false;
    }
    m_editStarted = true;
    return true;
}

void VisualToolBase::previewTextEdit(const QString &text)
{
    if (m_editStarted && document())
        document()->previewVisualTextEdit(text);
}

void VisualToolBase::previewTextEditGroup(const QVariantMap &textsByEventId)
{
    if (m_editStarted && document())
        document()->previewVisualTextEditGroup(textsByEventId);
}

void VisualToolBase::editTextOnce(const QString &text)
{
    if (text == activeText() || !beginTextEdit())
        return;
    previewTextEdit(text);
    commitOperation();
}

} // namespace yoake::ui
