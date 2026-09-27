#include "ass/ass_event_edits.h"

#include <algorithm>

namespace yoake::ass::EventEdits {

bool isSafeSplitBoundary(const QString &text, qsizetype utf16Position)
{
    if (utf16Position < 0 || utf16Position > text.size())
        return false;

    if (utf16Position > 0 && utf16Position < text.size()
        && text.at(utf16Position - 1).isHighSurrogate()
        && text.at(utf16Position).isLowSurrogate()) {
        return false;
    }

    for (qsizetype index = 0; index < text.size();) {
        if (text.at(index) != u'{') {
            ++index;
            continue;
        }
        const qsizetype blockStart = index++;
        int nestedBraces = 1;
        while (index < text.size() && nestedBraces > 0) {
            if (text.at(index) == u'{')
                ++nestedBraces;
            else if (text.at(index) == u'}')
                --nestedBraces;
            ++index;
        }
        const bool closed = nestedBraces == 0;
        const qsizetype blockEnd = index - 1;
        if (utf16Position > blockStart && (!closed || utf16Position <= blockEnd)) {
            return false;
        }
    }

    for (qsizetype index = 0; index + 1 < text.size(); ++index) {
        if (text.at(index) == u'\\' && utf16Position == index + 1) {
            const QChar code = text.at(index + 1);
            if (code == u'N' || code == u'n' || code == u'h')
                return false;
        }
    }
    return true;
}

std::optional<Split> splitAtCursor(const Event &event,
    qsizetype utf16Position,
    std::optional<qint64> splitTimeMs)
{
    if (splitTimeMs && (*splitTimeMs <= event.startMs || *splitTimeMs >= event.endMs))
        return std::nullopt;
    const qsizetype splitPosition = std::clamp<qsizetype>(utf16Position, 0, event.text.size());
    if (!isSafeSplitBoundary(event.text, splitPosition))
        return std::nullopt;
    Split result{event, event};
    result.first.text = event.text.left(splitPosition);
    result.second.id = QUuid::createUuid();
    result.second.text = event.text.mid(splitPosition);
    if (splitTimeMs) {
        result.first.endMs = *splitTimeMs;
        result.second.startMs = *splitTimeMs;
    }
    return result;
}

} // namespace yoake::ass::EventEdits
