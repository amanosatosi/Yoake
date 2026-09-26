#pragma once

#include "ass/ass_event.h"

#include <QtCore/QByteArray>
#include <QtCore/QString>
#include <QtCore/QVector>

namespace yoake::ass::EventClipboard {

[[nodiscard]] QString mimeType();
[[nodiscard]] QByteArray encode(const QVector<Event> &events);
[[nodiscard]] bool decode(const QByteArray &payload, QVector<Event> *events);
[[nodiscard]] QString plainText(const QVector<Event> &events);

} // namespace yoake::ass::EventClipboard
