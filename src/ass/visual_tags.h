#pragma once

#include <QtCore/QPointF>
#include <QtCore/QRectF>
#include <QtCore/QString>
#include <QtCore/QStringView>

#include <optional>

namespace yoake::ass::VisualTags {

struct Move {
    QPointF start;
    QPointF end;
    std::optional<qreal> startMs;
    std::optional<qreal> endMs;
};

struct Clip {
    bool inverse = false;
    bool rectangle = false;
    int drawingScale = 1;
    QRectF bounds;
    QString path;
};

struct Drawing {
    int drawingScale = 1;
    QString path;
    qsizetype pathStart = 0;
    qsizetype pathLength = 0;
};

[[nodiscard]] std::optional<QPointF> point(const QString &text, QStringView tag);
[[nodiscard]] std::optional<Move> move(const QString &text);
[[nodiscard]] std::optional<qreal> number(const QString &text, QStringView tag);
[[nodiscard]] std::optional<Clip> clip(const QString &text);
[[nodiscard]] std::optional<Drawing> drawing(const QString &text);
[[nodiscard]] QString setPoint(const QString &text, QStringView tag, const QPointF &point);
[[nodiscard]] QString setMove(const QString &text, const Move &move);
[[nodiscard]] QString setNumber(const QString &text, QStringView tag, qreal value);
[[nodiscard]] QString setClip(const QString &text, const Clip &clip);
[[nodiscard]] QString setDrawingPath(const QString &text, QStringView path, int drawingScale = 1);
[[nodiscard]] QString removeTag(const QString &text, QStringView tag);

[[nodiscard]] QString formatNumber(qreal value);

} // namespace yoake::ass::VisualTags
