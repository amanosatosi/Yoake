#pragma once

#include <QtCore/QVector>
#include <QtCore/QtTypes>

namespace yoake::media {

class FrameTimeMap final {
public:
    void reset(QVector<qint64> frameStartsMs = {}, qint64 durationMs = 0);

    [[nodiscard]] bool isEmpty() const { return m_frameStartsMs.isEmpty(); }
    [[nodiscard]] int frameCount() const { return m_frameStartsMs.size(); }
    [[nodiscard]] qint64 durationMs() const { return m_durationMs; }
    [[nodiscard]] int frameAtTime(qint64 timeMs) const;
    [[nodiscard]] qint64 frameStartMs(int frame) const;
    [[nodiscard]] qint64 frameEndMs(int frame) const;
    [[nodiscard]] bool variableFrameRate() const;
    [[nodiscard]] const QVector<qint64> &frameStarts() const { return m_frameStartsMs; }

private:
    QVector<qint64> m_frameStartsMs;
    qint64 m_durationMs = 0;
};

} // namespace yoake::media
