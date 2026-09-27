#pragma once

#include <QtCore/QPointF>
#include <QtCore/QSet>
#include <QtCore/QString>
#include <QtCore/QVector>

#include <optional>

namespace yoake::ass {

// Structured subset of ASS drawing syntax used by interactive path editing.
// Unsupported spline commands retain their original source and are read-only.
class VectorPath final {
public:
    enum class Kind { Move, Line, Cubic };

    struct Command {
        Kind kind = Kind::Move;
        QChar moveCommand = u'm';
        QVector<QPointF> points; // Move/Line: endpoint; Cubic: c1, c2, endpoint.
    };

    [[nodiscard]] static std::optional<VectorPath> parse(const QString &source, int drawingScale = 1);
    [[nodiscard]] static VectorPath startAt(QPointF point, int drawingScale = 1);

    [[nodiscard]] bool editable() const { return m_editable; }
    [[nodiscard]] int drawingScale() const { return m_drawingScale; }
    void setDrawingScale(int scale);
    [[nodiscard]] const QString &source() const { return m_source; }
    [[nodiscard]] const QVector<Command> &commands() const { return m_commands; }
    [[nodiscard]] int nodeCount() const;
    [[nodiscard]] QPointF node(int index) const;
    [[nodiscard]] bool setNode(int index, QPointF point);
    [[nodiscard]] bool translateNodes(const QSet<int> &indices, QPointF delta);
    [[nodiscard]] bool translate(QPointF delta);
    [[nodiscard]] bool appendLine(QPointF endpoint);
    [[nodiscard]] bool appendCubic(QPointF control1, QPointF control2, QPointF endpoint);
    [[nodiscard]] bool deleteNodes(const QSet<int> &indices);
    [[nodiscard]] bool insertOnSegment(int segmentIndex, qreal t);
    [[nodiscard]] bool convertSegmentToCubic(int segmentIndex);
    [[nodiscard]] bool convertSegmentToLine(int segmentIndex);
    [[nodiscard]] QString serialize() const;

private:
    QString m_source;
    QVector<Command> m_commands;
    int m_drawingScale = 1;
    bool m_editable = false;
};

} // namespace yoake::ass
