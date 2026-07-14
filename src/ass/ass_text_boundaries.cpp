#include "ass/ass_text_boundaries.h"

#include <QtCore/QTextBoundaryFinder>

namespace yoake::ass {
namespace {

bool isOverrideValueCharacter(QChar character)
{
    return !character.isSpace() && character != u'\\' && character != u'{' && character != u'}'
        && character != u'(' && character != u')' && character != u',';
}

QPair<int, int> fontNameRange(QStringView text, int position)
{
    const int blockStart = text.lastIndexOf(u'{', position);
    const int blockEnd = text.indexOf(u'}', position);
    if (blockStart < 0 || blockEnd < 0 || blockStart >= position || position > blockEnd)
        return {-1, -1};

    const int tagStart = text.lastIndexOf(QStringLiteral("\\fn"), position, Qt::CaseInsensitive);
    if (tagStart < blockStart)
        return {-1, -1};
    const int valueStart = tagStart + 3;
    if (position < valueStart)
        return {-1, -1};

    int valueEnd = valueStart;
    while (valueEnd < blockEnd && text[valueEnd] != u'\\')
        ++valueEnd;
    if (position > valueEnd)
        return {-1, -1};

    int start = position;
    while (start > valueStart && isOverrideValueCharacter(text[start - 1]))
        --start;
    int end = position;
    while (end < valueEnd && isOverrideValueCharacter(text[end]))
        ++end;
    return start < end ? QPair<int, int>{start, end} : QPair<int, int>{valueStart, valueEnd};
}

} // namespace

QVariantMap TextBoundaries::selectionRange(const QString &text, int position) const
{
    const auto [start, end] = rangeAt(text, position);
    return {{QStringLiteral("start"), start}, {QStringLiteral("end"), end}};
}

QPair<int, int> TextBoundaries::rangeAt(QStringView text, int position)
{
    position = qBound(0, position, static_cast<int>(text.size()));
    if (const auto range = fontNameRange(text, position); range.first >= 0)
        return range;

    const QString copy = text.toString();
    QTextBoundaryFinder finder(QTextBoundaryFinder::Word, copy);
    finder.setPosition(position);
    int start = finder.toPreviousBoundary();
    if (start < 0)
        start = 0;
    finder.setPosition(position);
    int end = finder.toNextBoundary();
    if (end < 0)
        end = copy.size();
    return {start, end};
}

} // namespace yoake::ass
