#include "media/frame_time_map.h"

#include <algorithm>
#include <cstdlib>

namespace yoake::media {

void FrameTimeMap::reset(QVector<qint64> frameStartsMs, qint64 durationMs)
{
    for (qsizetype index = 1; index < frameStartsMs.size(); ++index)
        frameStartsMs[index] = std::max(frameStartsMs[index], frameStartsMs[index - 1]);
    m_frameStartsMs = std::move(frameStartsMs);
    const qint64 finalStart = m_frameStartsMs.isEmpty() ? 0 : m_frameStartsMs.back();
    m_durationMs = std::max(durationMs, finalStart);
}

int FrameTimeMap::frameAtTime(qint64 timeMs) const
{
    if (m_frameStartsMs.isEmpty())
        return -1;
    const auto after = std::upper_bound(m_frameStartsMs.cbegin(), m_frameStartsMs.cend(), timeMs);
    if (after == m_frameStartsMs.cbegin())
        return 0;
    return static_cast<int>(std::distance(m_frameStartsMs.cbegin(), after) - 1);
}

qint64 FrameTimeMap::frameStartMs(int frame) const
{
    if (m_frameStartsMs.isEmpty())
        return 0;
    const int last = static_cast<int>(m_frameStartsMs.size()) - 1;
    return m_frameStartsMs[std::clamp(frame, 0, last)];
}

qint64 FrameTimeMap::frameEndMs(int frame) const
{
    if (m_frameStartsMs.isEmpty())
        return 0;
    const int last = static_cast<int>(m_frameStartsMs.size()) - 1;
    const int bounded = std::clamp(frame, 0, last);
    return bounded + 1 < m_frameStartsMs.size() ? m_frameStartsMs[bounded + 1] : m_durationMs;
}

bool FrameTimeMap::variableFrameRate() const
{
    if (m_frameStartsMs.size() < 3)
        return false;
    const qint64 nominal = m_frameStartsMs[1] - m_frameStartsMs[0];
    for (qsizetype index = 2; index < m_frameStartsMs.size(); ++index) {
        if (std::llabs((m_frameStartsMs[index] - m_frameStartsMs[index - 1]) - nominal) > 1)
            return true;
    }
    return false;
}

} // namespace yoake::media
