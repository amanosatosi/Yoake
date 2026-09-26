#pragma once

#include "ass/ass_event.h"

#include <QtCore/QByteArray>
#include <QtCore/QString>
#include <QtCore/QStringList>
#include <QtCore/QVector>

namespace yoake::ass {

class Document final {
public:
    struct ProjectProperties {
        QString audioFile;
        QString videoFile;
        int videoPosition = 0;
        int activeRow = 0;
        int scrollPosition = 0;
    };

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
    [[nodiscard]] const QStringList &styleNames() const noexcept { return m_styleNames; }
    [[nodiscard]] const ProjectProperties &projectProperties() const noexcept { return m_projectProperties; }
    [[nodiscard]] int eventIndex(const QUuid &id) const noexcept;

private:
    QVector<Record> m_records;
    QVector<Event> m_events;
    QStringList m_styleNames;
    ProjectProperties m_projectProperties;
    int m_eventOutputRecord = -1;
};

[[nodiscard]] qint64 parseTime(QStringView value, bool *ok = nullptr);
[[nodiscard]] QString formatTime(qint64 milliseconds);

} // namespace yoake::ass
