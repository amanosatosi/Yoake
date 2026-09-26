#include "ass/ass_event_clipboard.h"

#include "ass/ass_document.h"

#include <QtCore/QJsonArray>
#include <QtCore/QJsonDocument>
#include <QtCore/QJsonObject>
#include <QtCore/QStringList>

#include <utility>

namespace yoake::ass::EventClipboard {
namespace {

QJsonObject toJson(const Event &event)
{
    return {{QStringLiteral("comment"), event.comment},
        {QStringLiteral("layer"), event.layer},
        {QStringLiteral("startMs"), QString::number(event.startMs)},
        {QStringLiteral("endMs"), QString::number(event.endMs)},
        {QStringLiteral("style"), event.style},
        {QStringLiteral("actor"), event.actor},
        {QStringLiteral("marginLeft"), event.marginLeft},
        {QStringLiteral("marginRight"), event.marginRight},
        {QStringLiteral("marginVertical"), event.marginVertical},
        {QStringLiteral("effect"), event.effect},
        {QStringLiteral("text"), event.text}};
}

bool fromJson(const QJsonValue &value, Event *event)
{
    if (!value.isObject())
        return false;
    const QJsonObject object = value.toObject();
    bool startOk = false;
    bool endOk = false;
    Event parsed;
    parsed.comment = object.value(QStringLiteral("comment")).toBool();
    parsed.layer = object.value(QStringLiteral("layer")).toInt();
    parsed.startMs = object.value(QStringLiteral("startMs")).toString().toLongLong(&startOk);
    parsed.endMs = object.value(QStringLiteral("endMs")).toString().toLongLong(&endOk);
    parsed.style = object.value(QStringLiteral("style")).toString();
    parsed.actor = object.value(QStringLiteral("actor")).toString();
    parsed.marginLeft = object.value(QStringLiteral("marginLeft")).toInt();
    parsed.marginRight = object.value(QStringLiteral("marginRight")).toInt();
    parsed.marginVertical = object.value(QStringLiteral("marginVertical")).toInt();
    parsed.effect = object.value(QStringLiteral("effect")).toString();
    parsed.text = object.value(QStringLiteral("text")).toString();
    if (!startOk || !endOk || parsed.startMs < 0 || parsed.endMs < parsed.startMs
        || parsed.marginLeft < 0 || parsed.marginRight < 0 || parsed.marginVertical < 0) {
        return false;
    }
    parsed.id = QUuid::createUuid();
    *event = std::move(parsed);
    return true;
}

} // namespace

QString mimeType()
{
    return QStringLiteral("application/x-yoake-subtitle-rows+json");
}

QByteArray encode(const QVector<Event> &events)
{
    QJsonArray serialized;
    for (const Event &event : events)
        serialized.push_back(toJson(event));
    return QJsonDocument(serialized).toJson(QJsonDocument::Compact);
}

bool decode(const QByteArray &payload, QVector<Event> *events)
{
    if (!events)
        return false;
    QJsonParseError parseError;
    const QJsonDocument document = QJsonDocument::fromJson(payload, &parseError);
    if (parseError.error != QJsonParseError::NoError || !document.isArray())
        return false;
    QVector<Event> decoded;
    decoded.reserve(document.array().size());
    for (const QJsonValue &value : document.array()) {
        Event event;
        if (!fromJson(value, &event))
            return false;
        decoded.push_back(std::move(event));
    }
    *events = std::move(decoded);
    return true;
}

QString plainText(const QVector<Event> &events)
{
    QStringList readable;
    readable.reserve(events.size());
    for (const Event &event : events)
        readable.push_back(QStringList{formatTime(event.startMs), formatTime(event.endMs),
            event.style, event.actor, event.text}.join(u'\t'));
    return readable.join(u'\n');
}

} // namespace yoake::ass::EventClipboard
