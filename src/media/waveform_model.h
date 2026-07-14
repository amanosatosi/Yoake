#pragma once

#include <QtCore/QAbstractListModel>
#include <QtCore/QPointF>

namespace yoake::media {

class WaveformModel final : public QAbstractListModel {
    Q_OBJECT
    Q_PROPERTY(int count READ count NOTIFY countChanged)
    Q_PROPERTY(bool busy READ busy NOTIFY busyChanged)
    Q_PROPERTY(QString errorString READ errorString NOTIFY errorStringChanged)

public:
    enum Role { MinimumRole = Qt::UserRole + 1, MaximumRole };
    Q_ENUM(Role)

    explicit WaveformModel(QObject *parent = nullptr);

    int rowCount(const QModelIndex &parent = {}) const override;
    QVariant data(const QModelIndex &index, int role) const override;
    QHash<int, QByteArray> roleNames() const override;

    [[nodiscard]] int count() const { return m_peaks.size(); }
    [[nodiscard]] bool busy() const { return m_busy; }
    [[nodiscard]] QString errorString() const { return m_errorString; }
    Q_INVOKABLE QPointF sample(int index) const;

    void beginDecode(quint64 generation);
    void updatePeaks(quint64 generation, QVector<QPointF> peaks, bool complete);
    void fail(quint64 generation, const QString &error);
    void clear();

signals:
    void countChanged();
    void busyChanged();
    void errorStringChanged();

private:
    QVector<QPointF> m_peaks;
    quint64 m_generation = 0;
    bool m_busy = false;
    QString m_errorString;
};

} // namespace yoake::media
