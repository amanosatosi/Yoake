#pragma once

#include "ass/ass_event.h"

#include <QtCore/QtGlobal>

#include <optional>

namespace yoake::ass::EventEdits {

struct Split {
    Event first;
    Event second;
};

[[nodiscard]] bool isSafeSplitBoundary(const QString &text, qsizetype utf16Position);
[[nodiscard]] std::optional<Split> splitAtCursor(const Event &event,
    qsizetype utf16Position,
    std::optional<qint64> splitTimeMs = {});

} // namespace yoake::ass::EventEdits
