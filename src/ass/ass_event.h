#pragma once

#include <QtCore/QString>
#include <QtCore/QUuid>

namespace yoake::ass {

struct Event {
    QUuid id = QUuid::createUuid();
    bool comment = false;
    int layer = 0;
    qint64 startMs = 0;
    qint64 endMs = 5000;
    QString style = QStringLiteral("Default");
    QString actor;
    int marginLeft = 0;
    int marginRight = 0;
    int marginVertical = 0;
    QString effect;
    QString text;

    friend bool operator==(const Event &, const Event &) = default;
};

} // namespace yoake::ass
