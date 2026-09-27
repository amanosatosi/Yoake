#include "ass/vector_path.h"

#include "ass/visual_tags.h"

#include <QtCore/QLocale>
#include <QtCore/QRegularExpression>
#include <QtCore/QStringList>

#include <algorithm>
#include <cmath>
#include <utility>

namespace yoake::ass {
namespace {

struct Token {
    QChar command;
    qreal number = 0.0;
    bool isCommand = false;
};

std::optional<QVector<Token>> tokenize(const QString &source)
{
    static const QRegularExpression numberPattern(
        QStringLiteral(R"([+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)"));
    QVector<Token> result;
    qsizetype i = 0;
    while (i < source.size()) {
        const QChar c = source.at(i);
        if (c.isSpace() || c == u',') {
            ++i;
            continue;
        }
        if (c.isLetter()) {
            result.push_back({c.toLower(), 0.0, true});
            ++i;
            continue;
        }
        const auto match = numberPattern.match(source.mid(i));
        if (!match.hasMatch() || match.capturedStart() != 0)
            return {};
        bool ok = false;
        const qreal value = QLocale::c().toDouble(match.captured(), &ok);
        if (!ok || !std::isfinite(value))
            return {};
        result.push_back({{}, value, false});
        i += match.capturedEnd();
    }
    return result;
}

QPointF cubicPoint(QPointF p0, QPointF p1, QPointF p2, QPointF p3, qreal t)
{
    const qreal u = 1.0 - t;
    return p0 * (u * u * u) + p1 * (3.0 * u * u * t)
        + p2 * (3.0 * u * t * t) + p3 * (t * t * t);
}

} // namespace

std::optional<VectorPath> VectorPath::parse(const QString &source, int drawingScale)
{
    const auto tokens = tokenize(source);
    if (!tokens || tokens->isEmpty())
        return {};

    VectorPath path;
    path.m_source = source;
    path.setDrawingScale(drawingScale);
    QChar currentCommand;
    QVector<qreal> values;
    bool unsupported = false;
    bool hasMove = false;
    auto flush = [&]() -> bool {
        if (currentCommand.isNull())
            return values.isEmpty();
        if (path.m_commands.isEmpty() && currentCommand != u'm' && currentCommand != u'n')
            return false;
        const auto appendPairs = [&](Kind kind, int groupSize) -> bool {
            if (values.isEmpty() || values.size() % groupSize != 0)
                return false;
            for (qsizetype offset = 0; offset < values.size(); offset += groupSize) {
                Command command;
                command.kind = kind;
                command.moveCommand = currentCommand;
                for (int pair = 0; pair < groupSize; pair += 2)
                    command.points.push_back(QPointF(values.at(offset + pair), values.at(offset + pair + 1)));
                path.m_commands.push_back(std::move(command));
            }
            return true;
        };
        bool valid = false;
        switch (currentCommand.unicode()) {
        case u'm':
        case u'n':
            valid = values.size() >= 2 && values.size() % 2 == 0;
            if (valid) {
                bool firstPair = true;
                for (qsizetype offset = 0; offset < values.size(); offset += 2) {
                    Command command;
                    // Each m/n starts a new subpath. Additional coordinate
                    // pairs in the same command are line endpoints.
                    command.kind = firstPair ? Kind::Move : Kind::Line;
                    command.moveCommand = currentCommand;
                    command.points = {QPointF(values.at(offset), values.at(offset + 1))};
                    path.m_commands.push_back(std::move(command));
                    firstPair = false;
                    hasMove = true;
                }
            }
            break;
        case u'l': valid = appendPairs(Kind::Line, 2); break;
        case u'b': valid = appendPairs(Kind::Cubic, 6); break;
        case u's':
        case u'p':
        case u'c':
            // ASS B-splines are intentionally read-only until their complete
            // command semantics are modeled. Keep their original bytes intact.
            unsupported = true;
            valid = currentCommand == u'c' ? values.isEmpty() : !values.isEmpty() && values.size() % 2 == 0;
            break;
        default:
            return false;
        }
        values.clear();
        return valid;
    };

    for (const Token &token : *tokens) {
        if (!token.isCommand) {
            if (currentCommand.isNull())
                return {};
            values.push_back(token.number);
            continue;
        }
        if (!flush())
            return {};
        currentCommand = token.command;
    }
    if (!flush() || !hasMove)
        return {};
    path.m_editable = !unsupported;
    return path;
}

VectorPath VectorPath::startAt(QPointF point, int drawingScale)
{
    VectorPath path;
    path.setDrawingScale(drawingScale);
    path.m_editable = true;
    path.m_commands.push_back({Kind::Move, u'm', {point}});
    return path;
}

void VectorPath::setDrawingScale(int scale)
{
    m_drawingScale = std::clamp(scale, 1, 100);
}

int VectorPath::nodeCount() const
{
    if (!m_editable)
        return 0;
    int count = 0;
    for (const Command &command : m_commands)
        count += command.points.size();
    return count;
}

QPointF VectorPath::node(int index) const
{
    if (index < 0)
        return {};
    for (const Command &command : m_commands) {
        if (index < command.points.size())
            return command.points.at(index);
        index -= command.points.size();
    }
    return {};
}

bool VectorPath::setNode(int index, QPointF point)
{
    if (!m_editable || !std::isfinite(point.x()) || !std::isfinite(point.y()) || index < 0)
        return false;
    for (Command &command : m_commands) {
        if (index < command.points.size()) {
            command.points[index] = point;
            return true;
        }
        index -= command.points.size();
    }
    return false;
}

bool VectorPath::translateNodes(const QSet<int> &indices, QPointF delta)
{
    if (!m_editable || indices.isEmpty())
        return false;
    bool changed = false;
    int index = 0;
    for (Command &command : m_commands) {
        for (QPointF &point : command.points) {
            if (indices.contains(index)) {
                point += delta;
                changed = true;
            }
            ++index;
        }
    }
    return changed;
}

bool VectorPath::translate(QPointF delta)
{
    if (!m_editable || m_commands.isEmpty())
        return false;
    for (Command &command : m_commands)
        for (QPointF &point : command.points)
            point += delta;
    return true;
}

bool VectorPath::appendLine(QPointF endpoint)
{
    if (!m_editable || m_commands.isEmpty() || !std::isfinite(endpoint.x()) || !std::isfinite(endpoint.y()))
        return false;
    m_commands.push_back({Kind::Line, u'm', {endpoint}});
    return true;
}

bool VectorPath::appendCubic(QPointF control1, QPointF control2, QPointF endpoint)
{
    if (!m_editable || m_commands.isEmpty())
        return false;
    m_commands.push_back({Kind::Cubic, u'm', {control1, control2, endpoint}});
    return true;
}

bool VectorPath::deleteNodes(const QSet<int> &indices)
{
    if (!m_editable || indices.isEmpty())
        return false;
    QVector<Command> updated;
    int nodeIndex = 0;
    bool changed = false;
    for (const Command &command : std::as_const(m_commands)) {
        const int count = command.points.size();
        bool removeCommand = false;
        bool convertCubic = false;
        if (command.kind == Kind::Line) {
            removeCommand = indices.contains(nodeIndex);
        } else if (command.kind == Kind::Cubic) {
            const bool c1 = indices.contains(nodeIndex);
            const bool c2 = indices.contains(nodeIndex + 1);
            const bool endpoint = indices.contains(nodeIndex + 2);
            removeCommand = endpoint;
            convertCubic = !endpoint && (c1 || c2);
        } // Move commands remain protected so deletion cannot join subpaths accidentally.
        if (removeCommand || convertCubic)
            changed = true;
        if (!removeCommand) {
            if (convertCubic) {
                Command line;
                line.kind = Kind::Line;
                line.points = {command.points.back()};
                updated.push_back(std::move(line));
            } else {
                updated.push_back(command);
            }
        }
        nodeIndex += count;
    }
    if (!changed || updated.isEmpty() || updated.front().kind != Kind::Move)
        return false;
    m_commands = std::move(updated);
    return true;
}

bool VectorPath::insertOnSegment(int segmentIndex, qreal t)
{
    if (!m_editable || segmentIndex < 1 || segmentIndex >= m_commands.size())
        return false;
    t = std::clamp(t, 0.0, 1.0);
    if (t <= 0.0 || t >= 1.0)
        return false;
    Command &segment = m_commands[segmentIndex];
    const QPointF start = m_commands[segmentIndex - 1].points.back();
    if (segment.kind == Kind::Line) {
        const QPointF end = segment.points.front();
        segment.points.front() = start + (end - start) * t;
        Command remainder;
        remainder.kind = Kind::Line;
        remainder.points = {end};
        m_commands.insert(segmentIndex + 1, remainder);
        return true;
    }
    if (segment.kind != Kind::Cubic)
        return false;
    const QPointF p1 = segment.points[0];
    const QPointF p2 = segment.points[1];
    const QPointF p3 = segment.points[2];
    const QPointF q0 = start + (p1 - start) * t;
    const QPointF q1 = p1 + (p2 - p1) * t;
    const QPointF q2 = p2 + (p3 - p2) * t;
    const QPointF r0 = q0 + (q1 - q0) * t;
    const QPointF r1 = q1 + (q2 - q1) * t;
    const QPointF split = r0 + (r1 - r0) * t;
    segment.points = {q0, r0, split};
    Command remainder;
    remainder.kind = Kind::Cubic;
    remainder.points = {r1, q2, p3};
    m_commands.insert(segmentIndex + 1, remainder);
    return true;
}

bool VectorPath::convertSegmentToCubic(int segmentIndex)
{
    if (!m_editable || segmentIndex < 1 || segmentIndex >= m_commands.size())
        return false;
    Command &segment = m_commands[segmentIndex];
    if (segment.kind != Kind::Line)
        return false;
    const QPointF start = m_commands[segmentIndex - 1].points.back();
    const QPointF end = segment.points.front();
    segment.kind = Kind::Cubic;
    segment.points = {start + (end - start) / 3.0, start + (end - start) * (2.0 / 3.0), end};
    return true;
}

bool VectorPath::convertSegmentToLine(int segmentIndex)
{
    if (!m_editable || segmentIndex < 1 || segmentIndex >= m_commands.size())
        return false;
    Command &segment = m_commands[segmentIndex];
    if (segment.kind != Kind::Cubic)
        return false;
    segment.kind = Kind::Line;
    segment.points = {segment.points.back()};
    return true;
}

QString VectorPath::serialize() const
{
    if (!m_editable)
        return m_source;
    QStringList parts;
    for (const Command &command : m_commands) {
        QChar name = u'l';
        if (command.kind == Kind::Move)
            name = command.moveCommand;
        else if (command.kind == Kind::Cubic)
            name = u'b';
        QString part(name);
        for (const QPointF &point : command.points) {
            part += u' ';
            part += VisualTags::formatNumber(point.x());
            part += u' ';
            part += VisualTags::formatNumber(point.y());
        }
        parts.push_back(std::move(part));
    }
    return parts.join(u' ');
}

} // namespace yoake::ass
