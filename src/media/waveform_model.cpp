#include "media/waveform_model.h"

#include <algorithm>
#include <cmath>

namespace yoake::media {

WaveformModel::WaveformModel(QObject *parent) : QAbstractListModel(parent) { }

int WaveformModel::rowCount(const QModelIndex &parent) const
{
    return parent.isValid() ? 0 : static_cast<int>(m_peaks.size());
}

QVariant WaveformModel::data(const QModelIndex &index, int role) const
{
    if (!index.isValid() || index.row() < 0 || index.row() >= m_peaks.size())
        return {};
    if (role == MinimumRole)
        return m_peaks[index.row()].x();
    if (role == MaximumRole)
        return m_peaks[index.row()].y();
    return {};
}

QHash<int, QByteArray> WaveformModel::roleNames() const
{
    return {{MinimumRole, "minimum"}, {MaximumRole, "maximum"}};
}

QPointF WaveformModel::sample(int index) const
{
    return index >= 0 && index < m_peaks.size() ? m_peaks[index] : QPointF{};
}

QVariantList WaveformModel::samplesForRange(qint64 startMs, qint64 endMs, int pixelWidth) const
{
    QVariantList result;
    if (m_peaks.isEmpty() || m_samplesPerPeak <= 0 || m_sampleRate <= 0 || endMs <= startMs)
        return result;

    const double baseBucketMs = 1000.0 * m_samplesPerPeak / m_sampleRate;
    const double desiredBucketMs = static_cast<double>(endMs - startMs) / std::max(1, pixelWidth);
    const QVector<QPointF> *source = &m_peaks;
    qint64 levelFactor = 1;
    for (qsizetype level = 0; level < m_levels.size(); ++level) {
        const qint64 candidateFactor = qint64{1} << (level + 1);
        if (baseBucketMs * candidateFactor > desiredBucketMs * 2.0)
            break;
        source = &m_levels[level];
        levelFactor = candidateFactor;
    }

    const double bucketMs = baseBucketMs * levelFactor;
    const qint64 first = std::clamp<qint64>(
        static_cast<qint64>(std::floor((startMs - m_firstTimeMs) / bucketMs)), 0, source->size());
    const qint64 afterLast = std::clamp<qint64>(
        static_cast<qint64>(std::ceil((endMs - m_firstTimeMs) / bucketMs)), first, source->size());
    if (afterLast <= first)
        return result;

    const qint64 visibleBuckets = afterLast - first;
    const qint64 stride = std::max<qint64>(1,
        static_cast<qint64>(std::ceil(static_cast<double>(visibleBuckets) / std::max(1, pixelWidth))));
    result.reserve(static_cast<qsizetype>((visibleBuckets + stride - 1) / stride));
    for (qint64 bucket = first; bucket < afterLast; bucket += stride) {
        const qint64 end = std::min(afterLast, bucket + stride);
        double minimum = 1.0;
        double maximum = -1.0;
        for (qint64 sample = bucket; sample < end; ++sample) {
            minimum = std::min(minimum, source->at(sample).x());
            maximum = std::max(maximum, source->at(sample).y());
        }
        result.push_back(QPointF(minimum, maximum));
    }
    return result;
}

void WaveformModel::beginDecode(quint64 generation)
{
    m_generation = generation;
    beginResetModel();
    m_peaks.clear();
    m_levels.clear();
    endResetModel();
    m_samplesPerPeak = 0;
    m_sampleRate = 0;
    m_firstTimeMs = 0;
    emit countChanged();
    if (m_complete) {
        m_complete = false;
        emit completeChanged();
    }
    if (!m_errorString.isEmpty()) {
        m_errorString.clear();
        emit errorStringChanged();
    }
    if (!m_busy) {
        m_busy = true;
        emit busyChanged();
    }
}

void WaveformModel::appendPeaks(quint64 generation,
    qint64 startBucket,
    QVector<QPointF> peaks,
    int samplesPerPeak,
    int sampleRate,
    qint64 firstTimeMs,
    bool complete)
{
    if (generation != m_generation || startBucket != m_peaks.size())
        return;
    m_samplesPerPeak = samplesPerPeak;
    m_sampleRate = sampleRate;
    m_firstTimeMs = firstTimeMs;
    if (!peaks.isEmpty()) {
        const int first = static_cast<int>(m_peaks.size());
        const int last = first + static_cast<int>(peaks.size()) - 1;
        beginInsertRows({}, first, last);
        m_peaks += peaks;
        endInsertRows();
        appendCompleteLevels();
        emit countChanged();
    }
    if (complete) {
        rebuildLevels();
        if (!m_complete) {
            m_complete = true;
            emit completeChanged();
        }
        if (m_busy) {
            m_busy = false;
            emit busyChanged();
        }
    }
}

void WaveformModel::appendCompleteLevels()
{
    qsizetype level = 0;
    qsizetype sourceSize = m_peaks.size();
    while (sourceSize >= 2) {
        if (level >= m_levels.size())
            m_levels.push_back({});
        const QVector<QPointF> &source = level == 0 ? m_peaks : m_levels[level - 1];
        QVector<QPointF> &target = m_levels[level];
        const qsizetype completePairs = source.size() / 2;
        target.reserve(completePairs);
        while (target.size() < completePairs) {
            const qsizetype first = target.size() * 2;
            target.push_back(QPointF(
                std::min(source.at(first).x(), source.at(first + 1).x()),
                std::max(source.at(first).y(), source.at(first + 1).y())));
        }
        sourceSize = target.size();
        ++level;
    }
}

void WaveformModel::rebuildLevels()
{
    m_levels.clear();
    QVector<QPointF> source = m_peaks;
    while (source.size() > 1) {
        QVector<QPointF> next;
        next.reserve((source.size() + 1) / 2);
        for (qsizetype index = 0; index < source.size(); index += 2) {
            if (index + 1 >= source.size()) {
                next.push_back(source[index]);
            } else {
                next.push_back(QPointF(
                    std::min(source[index].x(), source[index + 1].x()),
                    std::max(source[index].y(), source[index + 1].y())));
            }
        }
        m_levels.push_back(next);
        source = std::move(next);
    }
}

void WaveformModel::fail(quint64 generation, const QString &error)
{
    if (generation != m_generation)
        return;
    m_errorString = error;
    emit errorStringChanged();
    if (m_busy) {
        m_busy = false;
        emit busyChanged();
    }
}

void WaveformModel::clear()
{
    ++m_generation;
    beginResetModel();
    m_peaks.clear();
    m_levels.clear();
    endResetModel();
    emit countChanged();
    const bool wasBusy = m_busy;
    const bool wasComplete = m_complete;
    m_busy = false;
    m_complete = false;
    m_errorString.clear();
    if (wasBusy)
        emit busyChanged();
    if (wasComplete)
        emit completeChanged();
    emit errorStringChanged();
}

} // namespace yoake::media
