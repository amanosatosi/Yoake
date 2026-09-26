#pragma once

#include "ass/ass_document.h"

#include <QtCore/QAbstractListModel>
#include <QtCore/QHash>
#include <QtCore/QSet>

namespace yoake::app { class DocumentContext; }

namespace yoake::models {

class SubtitleModel final : public QAbstractListModel {
    Q_OBJECT
    Q_PROPERTY(int activeRow READ activeRow NOTIFY activeRowChanged)
    Q_PROPERTY(QString activeId READ activeId NOTIFY activeRowChanged)
    Q_PROPERTY(int selectedCount READ selectedCount NOTIFY selectionChanged)
    Q_PROPERTY(int count READ count NOTIFY countChanged)

public:
    enum Role {
        IdRole = Qt::UserRole + 1,
        LayerRole,
        StartMsRole,
        EndMsRole,
        StyleRole,
        ActorRole,
        MarginLeftRole,
        MarginRightRole,
        MarginVerticalRole,
        EffectRole,
        TextRole,
        CommentRole,
        SelectedRole,
        ActiveRole,
        CpsRole
    };
    Q_ENUM(Role)

    struct SelectionSnapshot {
        QUuid activeId;
        QSet<QUuid> selectedIds;
        QUuid anchorId;
    };

    SubtitleModel(app::DocumentContext *context, ass::Document *document);

    int rowCount(const QModelIndex &parent = {}) const override;
    QVariant data(const QModelIndex &index, int role) const override;
    QHash<int, QByteArray> roleNames() const override;

    [[nodiscard]] int activeRow() const;
    [[nodiscard]] int count() const { return rowCount(); }
    [[nodiscard]] QString activeId() const { return m_activeId.toString(QUuid::WithoutBraces); }
    [[nodiscard]] int selectedCount() const { return m_selectedIds.size(); }
    [[nodiscard]] const ass::Event *activeEvent() const;
    [[nodiscard]] const ass::Event *eventAt(int row) const;
    [[nodiscard]] QVector<int> selectedRows() const;

    Q_INVOKABLE void selectRow(int row, bool toggle = false, bool extend = false);
    Q_INVOKABLE void activateRow(int row);
    Q_INVOKABLE void selectAll();
    Q_INVOKABLE void clearToActive();
    Q_INVOKABLE void moveActive(int delta, bool extend = false);
    Q_INVOKABLE void selectFirst();
    Q_INVOKABLE void selectLast();
    Q_INVOKABLE void selectPrevious();
    Q_INVOKABLE void selectNext();
    Q_INVOKABLE void setActiveRow(int row);
    Q_INVOKABLE void setField(int row, int role, const QVariant &value);

    SelectionSnapshot selectionSnapshot() const;
    void restoreSelection(const SelectionSnapshot &snapshot);
    void applyField(const QUuid &id, int role, const QVariant &value);
    void applyEvents(const QVector<ass::Event> &events, const SelectionSnapshot &selection);

signals:
    void countChanged();
    void activeRowChanged();
    void selectionChanged();

private:
    int rowForId(const QUuid &id) const;
    double cpsForEvent(const ass::Event &event) const;
    void announceSelectionChange(const QSet<QUuid> &oldSelection, const QUuid &oldActive);

    app::DocumentContext *m_context = nullptr;
    ass::Document *m_document = nullptr;
    QUuid m_activeId;
    QSet<QUuid> m_selectedIds;
    QUuid m_anchorId;
    mutable QHash<QUuid, double> m_cpsCache;
};

} // namespace yoake::models
