#include "models/subtitle_model.h"

#include "app/document_context.h"

#include <algorithm>

namespace yoake::models {

SubtitleModel::SubtitleModel(app::DocumentContext *context, ass::Document *document)
    : QAbstractListModel(context), m_context(context), m_document(document)
{
    if (!m_document->events().isEmpty()) {
        m_activeId = m_document->events().front().id;
        m_selectedIds.insert(m_activeId);
        m_anchorId = m_activeId;
    }
}

int SubtitleModel::rowCount(const QModelIndex &parent) const
{
    return parent.isValid() ? 0 : m_document->events().size();
}

QVariant SubtitleModel::data(const QModelIndex &index, int role) const
{
    const ass::Event *event = eventAt(index.row());
    if (!event || index.column() != 0)
        return {};

    switch (role) {
    case IdRole: return event->id.toString(QUuid::WithoutBraces);
    case LayerRole: return event->layer;
    case StartMsRole: return event->startMs;
    case EndMsRole: return event->endMs;
    case StyleRole: return event->style;
    case ActorRole: return event->actor;
    case MarginLeftRole: return event->marginLeft;
    case MarginRightRole: return event->marginRight;
    case MarginVerticalRole: return event->marginVertical;
    case EffectRole: return event->effect;
    case TextRole: return event->text;
    case CommentRole: return event->comment;
    case SelectedRole: return m_selectedIds.contains(event->id);
    case ActiveRole: return m_activeId == event->id;
    default: return {};
    }
}

QHash<int, QByteArray> SubtitleModel::roleNames() const
{
    return {
        {IdRole, "lineId"}, {LayerRole, "subtitleLayer"}, {StartMsRole, "startMs"},
        {EndMsRole, "endMs"}, {StyleRole, "style"}, {ActorRole, "actor"},
        {MarginLeftRole, "marginLeft"}, {MarginRightRole, "marginRight"},
        {MarginVerticalRole, "marginVertical"}, {EffectRole, "effect"},
        {TextRole, "subtitleText"}, {CommentRole, "comment"},
        {SelectedRole, "selected"}, {ActiveRole, "active"}
    };
}

int SubtitleModel::activeRow() const
{
    return rowForId(m_activeId);
}

const ass::Event *SubtitleModel::activeEvent() const
{
    return eventAt(activeRow());
}

const ass::Event *SubtitleModel::eventAt(int row) const
{
    if (row < 0 || row >= m_document->events().size())
        return nullptr;
    return &m_document->events()[row];
}

QVector<int> SubtitleModel::selectedRows() const
{
    QVector<int> rows;
    for (int row = 0; row < m_document->events().size(); ++row) {
        if (m_selectedIds.contains(m_document->events()[row].id))
            rows.push_back(row);
    }
    return rows;
}

void SubtitleModel::selectRow(int row, bool toggle, bool extend)
{
    const ass::Event *event = eventAt(row);
    if (!event)
        return;
    if (event->id != m_activeId && m_context->karaoke()->active()
        && m_context->karaoke()->dirty()) {
        return;
    }
    const QSet<QUuid> oldSelection = m_selectedIds;
    const QUuid oldActive = m_activeId;

    const int anchorRow = rowForId(m_anchorId);
    if (extend && anchorRow >= 0) {
        m_selectedIds.clear();
        const int first = std::min(anchorRow, row);
        const int last = std::max(anchorRow, row);
        for (int current = first; current <= last; ++current)
            m_selectedIds.insert(m_document->events()[current].id);
    } else if (toggle) {
        if (m_selectedIds.contains(event->id) && m_selectedIds.size() > 1)
            m_selectedIds.remove(event->id);
        else
            m_selectedIds.insert(event->id);
        m_anchorId = event->id;
    } else {
        m_selectedIds = {event->id};
        m_anchorId = event->id;
    }
    m_activeId = event->id;
    announceSelectionChange(oldSelection, oldActive);
}

void SubtitleModel::setActiveRow(int row)
{
    selectRow(row, false, false);
}

void SubtitleModel::setField(int row, int role, const QVariant &value)
{
    const ass::Event *event = eventAt(row);
    if (event)
        m_context->editEvent(event->id, role, value);
}

SubtitleModel::SelectionSnapshot SubtitleModel::selectionSnapshot() const
{
    return {m_activeId, m_selectedIds, m_anchorId};
}

void SubtitleModel::restoreSelection(const SelectionSnapshot &snapshot)
{
    const QSet<QUuid> oldSelection = m_selectedIds;
    const QUuid oldActive = m_activeId;
    m_selectedIds.clear();
    for (const QUuid &id : snapshot.selectedIds) {
        if (rowForId(id) >= 0)
            m_selectedIds.insert(id);
    }
    m_activeId = rowForId(snapshot.activeId) >= 0 ? snapshot.activeId : QUuid{};
    if (m_activeId.isNull() && !m_document->events().isEmpty())
        m_activeId = m_document->events().front().id;
    if (m_selectedIds.isEmpty() && !m_activeId.isNull())
        m_selectedIds.insert(m_activeId);
    m_anchorId = rowForId(snapshot.anchorId) >= 0 ? snapshot.anchorId : m_activeId;
    announceSelectionChange(oldSelection, oldActive);
}

void SubtitleModel::applyField(const QUuid &id, int role, const QVariant &value)
{
    const int row = rowForId(id);
    if (row < 0)
        return;
    ass::Event &event = m_document->events()[row];
    switch (role) {
    case LayerRole: event.layer = value.toInt(); break;
    case StartMsRole: event.startMs = std::clamp<qint64>(value.toLongLong(), 0, event.endMs); break;
    case EndMsRole: event.endMs = std::max(event.startMs, value.toLongLong()); break;
    case StyleRole: event.style = value.toString(); break;
    case ActorRole: event.actor = value.toString(); break;
    case MarginLeftRole: event.marginLeft = std::max(0, value.toInt()); break;
    case MarginRightRole: event.marginRight = std::max(0, value.toInt()); break;
    case MarginVerticalRole: event.marginVertical = std::max(0, value.toInt()); break;
    case EffectRole: event.effect = value.toString(); break;
    case TextRole: event.text = value.toString(); break;
    case CommentRole: event.comment = value.toBool(); break;
    default: return;
    }
    const QModelIndex changed = index(row);
    emit dataChanged(changed, changed, {role});
    if (id == m_activeId)
        emit m_context->activeLineChanged();
}

void SubtitleModel::applyEvents(const QVector<ass::Event> &events, const SelectionSnapshot &selection)
{
    beginResetModel();
    m_document->events() = events;
    endResetModel();
    restoreSelection(selection);
}

int SubtitleModel::rowForId(const QUuid &id) const
{
    return m_document->eventIndex(id);
}

void SubtitleModel::announceSelectionChange(const QSet<QUuid> &oldSelection, const QUuid &oldActive)
{
    QSet<int> rows;
    for (const QUuid &id : oldSelection)
        rows.insert(rowForId(id));
    for (const QUuid &id : m_selectedIds)
        rows.insert(rowForId(id));
    rows.insert(rowForId(oldActive));
    rows.insert(rowForId(m_activeId));
    for (int row : rows) {
        if (row >= 0) {
            const QModelIndex changed = index(row);
            emit dataChanged(changed, changed, {SelectedRole, ActiveRole});
        }
    }
    if (oldActive != m_activeId) {
        emit activeRowChanged();
        emit m_context->activeLineChanged();
    }
    if (oldSelection != m_selectedIds)
        emit selectionChanged();
}

} // namespace yoake::models
