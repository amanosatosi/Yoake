#include "ass/ass_event_edits.h"

#include <algorithm>

namespace yoake::ass::EventEdits {

std::optional<Split> splitAtCursor(const Event &event,
    qsizetype utf16Position,
    std::optional<qint64> splitTimeMs)
{
    if (splitTimeMs && (*splitTimeMs <= event.startMs || *splitTimeMs >= event.endMs))
        return std::nullopt;
    const qsizetype splitPosition = std::clamp<qsizetype>(utf16Position, 0, event.text.size());
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
