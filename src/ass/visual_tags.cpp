#include "ass/visual_tags.h"

#include <QtCore/QLocale>
#include <QtCore/QStringList>
#include <QtCore/QStringView>
#include <QtCore/QVector>

#include <algorithm>
#include <cmath>

namespace yoake::ass::VisualTags {
namespace {

struct Token {
    QString name;
    qsizetype start = 0;
    qsizetype end = 0;
    QString arguments;
    QString rawValue;
    bool hasArguments = false;
};

bool isEscaped(const QString &text, qsizetype index)
{
    qsizetype backslashes = 0;
    for (qsizetype i = index - 1; i >= 0 && text.at(i) == u'\\'; --i)
        ++backslashes;
    return backslashes % 2 != 0;
}

qsizetype findUnescaped(const QString &text, QChar character, qsizetype from)
{
    qsizetype index = from;
    while ((index = text.indexOf(character, index)) >= 0) {
        if (!isEscaped(text, index))
            return index;
        ++index;
    }
    return -1;
}

QVector<Token> overrideTokens(const QString &text)
{
    QVector<Token> tokens;
    qsizetype blockStart = 0;
    while ((blockStart = findUnescaped(text, u'{', blockStart)) >= 0) {
        const qsizetype blockEnd = findUnescaped(text, u'}', blockStart + 1);
        if (blockEnd < 0)
            break;
        qsizetype slash = text.indexOf(u'\\', blockStart + 1);
        while (slash >= 0 && slash < blockEnd) {
            qsizetype nameEnd = slash + 1;
            if (nameEnd < blockEnd && text.at(nameEnd).isDigit()) {
                while (nameEnd < blockEnd && text.at(nameEnd).isDigit()) ++nameEnd;
                while (nameEnd < blockEnd && text.at(nameEnd).isLetter()) ++nameEnd;
            } else {
                while (nameEnd < blockEnd && text.at(nameEnd).isLetter()) ++nameEnd;
            }
            if (nameEnd == slash + 1) {
                slash = text.indexOf(u'\\', slash + 1);
                continue;
            }
            qsizetype nextSlash = blockEnd;
            int depth = 0;
            for (qsizetype i = nameEnd; i < blockEnd; ++i) {
                if (text.at(i) == u'(') ++depth;
                else if (text.at(i) == u')' && depth > 0) --depth;
                else if (text.at(i) == u'\\' && depth == 0) {
                    nextSlash = i;
                    break;
                }
            }
            qsizetype open = nameEnd;
            while (open < nextSlash && text.at(open).isSpace())
                ++open;
            Token token;
            token.name = text.mid(slash + 1, nameEnd - slash - 1).toLower();
            token.start = slash;
            token.end = nextSlash;
            token.rawValue = text.mid(nameEnd, nextSlash - nameEnd);
            if (open < nextSlash && text.at(open) == u'(') {
                int depth = 1;
                qsizetype close = open + 1;
                for (; close < nextSlash && depth > 0; ++close) {
                    if (text.at(close) == u'(') ++depth;
                    else if (text.at(close) == u')') --depth;
                }
                if (depth == 0) {
                    token.hasArguments = true;
                    token.arguments = text.mid(open + 1, close - open - 2);
                }
            }
            // Retain whitespace after a tag as part of its surrounding source.
            while (token.end > token.start && text.at(token.end - 1).isSpace())
                --token.end;
            tokens.push_back(std::move(token));
            slash = nextSlash < blockEnd ? nextSlash : -1;
        }
        blockStart = blockEnd + 1;
    }
    return tokens;
}

QString canonicalTag(QStringView tag)
{
    QString result = tag.toString().toLower();
    if (!result.startsWith(u'\\'))
        result.prepend(u'\\');
    return result.mid(1);
}

std::optional<QVector<qreal>> numericArguments(const Token &token)
{
    if (!token.hasArguments)
        return {};
    QVector<qreal> values;
    const QStringList parts = token.arguments.split(u',', Qt::KeepEmptyParts);
    for (const QString &part : parts) {
        bool ok = false;
        const qreal number = QLocale::c().toDouble(part.trimmed(), &ok);
        if (!ok || !std::isfinite(number))
            return {};
        values.push_back(number);
    }
    return values;
}

std::optional<Token> lastToken(const QString &text, const QStringList &names)
{
    std::optional<Token> result;
    for (const Token &token : overrideTokens(text)) {
        if (names.contains(token.name, Qt::CaseInsensitive))
            result = token;
    }
    return result;
}

QString replaceToken(const QString &text, const QStringList &names, const QString &replacement)
{
    const QVector<Token> tokens = overrideTokens(text);
    std::optional<Token> last;
    for (const Token &token : tokens) {
        if (names.contains(token.name, Qt::CaseInsensitive))
            last = token;
    }
    if (last) {
        QString result = text;
        result.replace(last->start, last->end - last->start, replacement);
        return result;
    }
    qsizetype blockStart = 0;
    while (blockStart < text.size() && text.at(blockStart).isSpace())
        ++blockStart;
    if (blockStart < text.size() && text.at(blockStart) == u'{' && !isEscaped(text, blockStart)) {
        const qsizetype blockEnd = findUnescaped(text, u'}', blockStart + 1);
        if (blockEnd >= 0) {
            QString result = text;
            result.insert(blockEnd, replacement);
            return result;
        }
    }
    return QStringLiteral("{") + replacement + QStringLiteral("}") + text;
}

QString pointArguments(const QPointF &point)
{
    return formatNumber(point.x()) + u',' + formatNumber(point.y());
}

QString pathArgument(const Clip &clip)
{
    const QString path = clip.path.trimmed();
    if (path.isEmpty())
        return {};
    if (clip.drawingScale <= 1)
        return path;
    return QString::number(clip.drawingScale) + u',' + path;
}

} // namespace

std::optional<QPointF> point(const QString &text, QStringView tag)
{
    const auto token = lastToken(text, {canonicalTag(tag)});
    const auto args = token ? numericArguments(*token) : std::nullopt;
    if (!args || args->size() != 2)
        return {};
    return QPointF(args->at(0), args->at(1));
}

std::optional<Move> move(const QString &text)
{
    const auto token = lastToken(text, {QStringLiteral("move")});
    const auto args = token ? numericArguments(*token) : std::nullopt;
    if (!args || (args->size() != 4 && args->size() != 6))
        return {};
    Move result{QPointF(args->at(0), args->at(1)), QPointF(args->at(2), args->at(3)), {}, {}};
    if (args->size() == 6) {
        result.startMs = args->at(4);
        result.endMs = args->at(5);
    }
    return result;
}

std::optional<qreal> number(const QString &text, QStringView tag)
{
    const auto token = lastToken(text, {canonicalTag(tag)});
    if (!token)
        return {};
    if (token->hasArguments) {
        const auto args = numericArguments(*token);
        if (!args || args->size() != 1)
            return {};
        return args->front();
    }
    bool ok = false;
    const qreal result = QLocale::c().toDouble(token->rawValue.trimmed(), &ok);
    return ok && std::isfinite(result) ? std::optional<qreal>(result) : std::nullopt;
}

std::optional<Clip> clip(const QString &text)
{
    const auto token = lastToken(text, {QStringLiteral("clip"), QStringLiteral("iclip")});
    if (!token || !token->hasArguments)
        return {};
    Clip result;
    result.inverse = token->name == QStringLiteral("iclip");
    QString args = token->arguments.trimmed();
    const qsizetype firstComma = args.indexOf(u',');
    if (firstComma >= 0) {
        bool scaleOk = false;
        const int scale = args.left(firstComma).trimmed().toInt(&scaleOk);
        if (scaleOk && scale >= 1 && scale <= 100) {
            result.drawingScale = scale;
            args = args.mid(firstComma + 1).trimmed();
        }
    }
    const QStringList fields = args.split(u',', Qt::KeepEmptyParts);
    if (fields.size() == 4) {
        QVector<qreal> values;
        for (const QString &field : fields) {
            bool ok = false;
            const qreal value = QLocale::c().toDouble(field.trimmed(), &ok);
            if (!ok || !std::isfinite(value))
                return {};
            values.push_back(value);
        }
        result.rectangle = true;
        result.bounds = QRectF(QPointF(values[0], values[1]), QPointF(values[2], values[3])).normalized();
        return result;
    }
    if (args.startsWith(u'm', Qt::CaseInsensitive) || args.startsWith(u'n', Qt::CaseInsensitive)) {
        result.rectangle = false;
        result.path = args;
        return result;
    }
    return {};
}

std::optional<Drawing> drawing(const QString &text)
{
    bool active = false;
    int scale = 1;
    qsizetype cursor = 0;
    while (cursor < text.size()) {
        const qsizetype blockStart = findUnescaped(text, u'{', cursor);
        const qsizetype segmentEnd = blockStart < 0 ? text.size() : blockStart;
        if (active) {
            qsizetype start = cursor;
            qsizetype end = segmentEnd;
            while (start < end && text.at(start).isSpace()) ++start;
            while (end > start && text.at(end - 1).isSpace()) --end;
            if (end > start)
                return Drawing{scale, text.mid(start, end - start), start, end - start};
        }
        if (blockStart < 0)
            break;
        const qsizetype blockEnd = findUnescaped(text, u'}', blockStart + 1);
        if (blockEnd < 0)
            break;
        for (const Token &token : overrideTokens(text)) {
            if (token.start <= blockStart || token.start >= blockEnd || token.name != QStringLiteral("p"))
                continue;
            bool ok = false;
            const int value = token.rawValue.trimmed().toInt(&ok);
            if (ok && value > 0) {
                active = true;
                scale = std::clamp(value, 1, 100);
            } else if (ok) {
                active = false;
            }
        }
        cursor = blockEnd + 1;
    }
    return {};
}

QString setPoint(const QString &text, QStringView tag, const QPointF &point)
{
    const QString name = canonicalTag(tag);
    return replaceToken(text, {name}, u'\\' + name + u'(' + pointArguments(point) + u')');
}

QString setMove(const QString &text, const Move &move)
{
    QString args = pointArguments(move.start) + u',' + pointArguments(move.end);
    if (move.startMs && move.endMs)
        args += u',' + formatNumber(*move.startMs) + u',' + formatNumber(*move.endMs);
    return replaceToken(text, {QStringLiteral("move")}, QStringLiteral("\\move(") + args + u')');
}

QString setNumber(const QString &text, QStringView tag, qreal value)
{
    const QString name = canonicalTag(tag);
    static const QStringList directValueTags = {QStringLiteral("frz"), QStringLiteral("frx"),
        QStringLiteral("fry"), QStringLiteral("fscx"), QStringLiteral("fscy"),
        QStringLiteral("an")};
    const QString rendered = directValueTags.contains(name)
        ? u'\\' + name + formatNumber(value)
        : u'\\' + name + u'(' + formatNumber(value) + u')';
    return replaceToken(text, {name}, rendered);
}

QString setClip(const QString &text, const Clip &clip)
{
    const QString name = clip.inverse ? QStringLiteral("iclip") : QStringLiteral("clip");
    const QRectF normalizedBounds = clip.bounds.normalized();
    const QString args = clip.rectangle
        ? pointArguments(normalizedBounds.topLeft()) + u',' + pointArguments(normalizedBounds.bottomRight())
        : pathArgument(clip);
    return replaceToken(text, {QStringLiteral("clip"), QStringLiteral("iclip")},
        u'\\' + name + u'(' + args + u')');
}

QString setDrawingPath(const QString &text, QStringView path, int drawingScale)
{
    const auto existing = drawing(text);
    const QString value = path.toString();
    if (existing) {
        QString result = text;
        result.replace(existing->pathStart, existing->pathLength, value);
        return result;
    }
    const int normalizedScale = std::clamp(drawingScale, 1, 100);
    return text + QStringLiteral("{\\p%1}%2{\\p0}").arg(normalizedScale).arg(value);
}

QString removeTag(const QString &text, QStringView tag)
{
    const QString name = canonicalTag(tag);
    QVector<Token> tokens = overrideTokens(text);
    QString result = text;
    for (auto it = tokens.crbegin(); it != tokens.crend(); ++it) {
        if (it->name == name)
            result.remove(it->start, it->end - it->start);
    }
    return result;
}

QString formatNumber(qreal value)
{
    if (!std::isfinite(value))
        value = 0.0;
    if (std::abs(value) < 0.0005)
        value = 0.0;
    QString result = QLocale::c().toString(value, 'f', 3);
    while (result.contains(u'.') && result.endsWith(u'0'))
        result.chop(1);
    if (result.endsWith(u'.'))
        result.chop(1);
    return result;
}

} // namespace yoake::ass::VisualTags
