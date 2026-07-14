#pragma once

#include "ass/ass_document.h"

#include <QtCore/QAbstractListModel>
#include <QtCore/QSet>

namespace yoake::app { class DocumentContext; }

namespace yoake::models {

class SubtitleModel final : public QAbstractListModel {
    Q_OBJECT
    Q_PROPERTY(int activeRow READ activeRow NOTIFY activeRowChanged)
    Q_PROPERTY(int selectedCount READ selectedCount NOTIFY selectionChanged)

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
        ActiveRole
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
    [[nodiscard]] int selectedCount() const { return m_selectedIds.size(); }
    [[nodiscard]] const ass::Event *activeEvent() const;
    [[nodiscard]] const ass::Event *eventAt(int row) const;
    [[nodiscard]] QVector<int> selectedRows() const;

    Q_INVOKABLE void selectRow(int row, bool toggle = false, bool extend = false);
    Q_INVOKABLE void setActiveRow(int row);
    Q_INVOKABLE void setField(int row, int role, const QVariant &value);

    SelectionSnapshot selectionSnapshot() const;
    void restoreSelection(const SelectionSnapshot &snapshot);
    void applyField(const QUuid &id, int role, const QVariant &value);
    void applyEvents(const QVector<ass::Event> &events, const SelectionSnapshot &selection);

signals:
    void activeRowChanged();
    void selectionChanged();

private:
    int rowForId(const QUuid &id) const;
    void announceSelectionChange(const QSet<QUuid> &oldSelection, const QUuid &oldActive);

    app::DocumentContext *m_context = nullptr;
    ass::Document *m_document = nullptr;
    QUuid m_activeId;
    QSet<QUuid> m_selectedIds;
    QUuid m_anchorId;
};

} // namespace yoake::models
