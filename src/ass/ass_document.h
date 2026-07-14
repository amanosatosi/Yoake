#pragma once

#include "ass/ass_event.h"

#include <QtCore/QByteArray>
#include <QtCore/QString>
#include <QtCore/QVector>

namespace yoake::ass {

class Document final {
public:
    struct Record {
        enum class Kind { Raw, Event };
        Kind kind = Kind::Raw;
        QString raw;
        QUuid eventId;
    };

    static Document createDefault();
    static Document parse(const QByteArray &contents, QString *error = nullptr);

    [[nodiscard]] QByteArray serialize() const;
    [[nodiscard]] const QVector<Event> &events() const noexcept { return m_events; }
    [[nodiscard]] QVector<Event> &events() noexcept { return m_events; }
    [[nodiscard]] int eventIndex(const QUuid &id) const noexcept;

private:
    QVector<Record> m_records;
    QVector<Event> m_events;
    int m_eventOutputRecord = -1;
};

[[nodiscard]] qint64 parseTime(QStringView value, bool *ok = nullptr);
[[nodiscard]] QString formatTime(qint64 milliseconds);

} // namespace yoake::ass
