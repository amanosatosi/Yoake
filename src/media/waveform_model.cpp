#include "media/waveform_model.h"

namespace yoake::media {

WaveformModel::WaveformModel(QObject *parent) : QAbstractListModel(parent) { }

int WaveformModel::rowCount(const QModelIndex &parent) const
{
    return parent.isValid() ? 0 : m_peaks.size();
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

void WaveformModel::beginDecode(quint64 generation)
{
    m_generation = generation;
    beginResetModel();
    m_peaks.clear();
    endResetModel();
    emit countChanged();
    if (!m_errorString.isEmpty()) {
        m_errorString.clear();
        emit errorStringChanged();
    }
    if (!m_busy) {
        m_busy = true;
        emit busyChanged();
    }
}

void WaveformModel::updatePeaks(quint64 generation, QVector<QPointF> peaks, bool complete)
{
    if (generation != m_generation)
        return;
    beginResetModel();
    m_peaks = std::move(peaks);
    endResetModel();
    emit countChanged();
    if (complete && m_busy) {
        m_busy = false;
        emit busyChanged();
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
    endResetModel();
    emit countChanged();
    const bool wasBusy = m_busy;
    m_busy = false;
    m_errorString.clear();
    if (wasBusy)
        emit busyChanged();
    emit errorStringChanged();
}

} // namespace yoake::media
