#pragma once

#include <QtCore/QAbstractListModel>
#include <QtCore/QString>
#include <QtCore/QVector>

namespace yoake::app { class DocumentContext; }

namespace yoake::timing {

class KaraokeSession final : public QAbstractListModel {
    Q_OBJECT
    Q_PROPERTY(bool active READ active NOTIFY activeChanged)
    Q_PROPERTY(bool dirty READ dirty NOTIFY dirtyChanged)
    Q_PROPERTY(QString tagType READ tagType WRITE setTagType NOTIFY tagTypeChanged)
    Q_PROPERTY(int selectedIndex READ selectedIndex WRITE setSelectedIndex NOTIFY selectedIndexChanged)
    Q_PROPERTY(int count READ count NOTIFY countChanged)

public:
    enum Role {
        LabelRole = Qt::UserRole + 1,
        StartMsRole,
        EndMsRole,
        TagTypeRole,
        SelectedRole
    };
    Q_ENUM(Role)

    struct Slot {
        QString source;
        QString label;
        QString tagType = QStringLiteral("\\k");
        qint64 startMs = 0;
        qint64 endMs = 0;
        qint64 sourceDurationMs = 0;
    };

    explicit KaraokeSession(app::DocumentContext *context);

    int rowCount(const QModelIndex &parent = {}) const override;
    QVariant data(const QModelIndex &index, int role) const override;
    QHash<int, QByteArray> roleNames() const override;

    [[nodiscard]] bool active() const { return m_active; }
    [[nodiscard]] bool dirty() const { return m_dirty; }
    [[nodiscard]] QString tagType() const { return m_tagType; }
    [[nodiscard]] int selectedIndex() const { return m_selectedIndex; }
    [[nodiscard]] int count() const { return static_cast<int>(m_slots.size()); }

    void setTagType(const QString &tagType);
    void setSelectedIndex(int index);

    Q_INVOKABLE void beginOriginal();
    Q_INVOKABLE void cancel();
    Q_INVOKABLE void commit();
    Q_INVOKABLE qint64 boundaryMs(int boundaryIndex) const;
    Q_INVOKABLE qint64 slotStartMs(int index) const;
    Q_INVOKABLE qint64 slotEndMs(int index) const;
    Q_INVOKABLE int nearestBoundary(qint64 timeMs, qint64 toleranceMs) const;
    Q_INVOKABLE void moveBoundary(int boundaryIndex, qint64 timeMs);
    Q_INVOKABLE void selectAtTime(qint64 timeMs);
    Q_INVOKABLE void playSelected();

signals:
    void activeChanged();
    void dirtyChanged();
    void tagTypeChanged();
    void selectedIndexChanged();
    void countChanged();
    void boundariesChanged();

private:
    void reload();
    void setDirty(bool dirty);
    [[nodiscard]] QString serializedText() const;

    app::DocumentContext *m_context = nullptr;
    QVector<Slot> m_slots;
    QString m_originalText;
    QString m_tagType = QStringLiteral("\\k");
    int m_selectedIndex = -1;
    bool m_active = false;
    bool m_dirty = false;
    bool m_committing = false;
};

} // namespace yoake::timing
