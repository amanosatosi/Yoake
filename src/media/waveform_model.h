#pragma once

#include <QtCore/QAbstractListModel>
#include <QtCore/QPointF>
#include <QtCore/QVariantList>
#include <QtCore/QVector>

namespace yoake::media {

class WaveformModel final : public QAbstractListModel {
    Q_OBJECT
    Q_PROPERTY(int count READ count NOTIFY countChanged)
    Q_PROPERTY(bool busy READ busy NOTIFY busyChanged)
    Q_PROPERTY(bool complete READ complete NOTIFY completeChanged)
    Q_PROPERTY(QString errorString READ errorString NOTIFY errorStringChanged)

public:
    enum Role { MinimumRole = Qt::UserRole + 1, MaximumRole };
    Q_ENUM(Role)

    explicit WaveformModel(QObject *parent = nullptr);

    int rowCount(const QModelIndex &parent = {}) const override;
    QVariant data(const QModelIndex &index, int role) const override;
    QHash<int, QByteArray> roleNames() const override;

    [[nodiscard]] int count() const { return static_cast<int>(m_peaks.size()); }
    [[nodiscard]] bool busy() const { return m_busy; }
    [[nodiscard]] bool complete() const { return m_complete; }
    [[nodiscard]] QString errorString() const { return m_errorString; }
    Q_INVOKABLE QPointF sample(int index) const;
    Q_INVOKABLE QVariantList samplesForRange(qint64 startMs, qint64 endMs, int pixelWidth) const;

    void beginDecode(quint64 generation);
    void appendPeaks(quint64 generation,
        qint64 startBucket,
        QVector<QPointF> peaks,
        int samplesPerPeak,
        int sampleRate,
        qint64 firstTimeMs,
        bool complete);
    void fail(quint64 generation, const QString &error);
    void clear();

signals:
    void countChanged();
    void busyChanged();
    void completeChanged();
    void errorStringChanged();

private:
    void rebuildLevels();

    QVector<QPointF> m_peaks;
    QVector<QVector<QPointF>> m_levels;
    quint64 m_generation = 0;
    qint64 m_firstTimeMs = 0;
    int m_samplesPerPeak = 0;
    int m_sampleRate = 0;
    bool m_busy = false;
    bool m_complete = false;
    QString m_errorString;
};

} // namespace yoake::media
