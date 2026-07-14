#include "app/document_context.h"

#include "media/media_session.h"
#include "models/subtitle_model.h"
#include "renderer/mangetsu_session.h"

#include <QtConcurrent/QtConcurrentRun>
#include <QtCore/QFileInfo>
#include <QtCore/QFutureWatcher>
#include <QtCore/QPointer>
#include <QtCore/QSaveFile>

#include <algorithm>

namespace yoake::app {
namespace {

QVariant eventValue(const ass::Event &event, int role)
{
    using models::SubtitleModel;
    switch (role) {
    case SubtitleModel::LayerRole: return event.layer;
    case SubtitleModel::StartMsRole: return event.startMs;
    case SubtitleModel::EndMsRole: return event.endMs;
    case SubtitleModel::StyleRole: return event.style;
    case SubtitleModel::ActorRole: return event.actor;
    case SubtitleModel::MarginLeftRole: return event.marginLeft;
    case SubtitleModel::MarginRightRole: return event.marginRight;
    case SubtitleModel::MarginVerticalRole: return event.marginVertical;
    case SubtitleModel::EffectRole: return event.effect;
    case SubtitleModel::TextRole: return event.text;
    case SubtitleModel::CommentRole: return event.comment;
    default: return {};
    }
}

QVariant normalizedEventValue(const ass::Event &event, int role, const QVariant &value)
{
    using models::SubtitleModel;
    switch (role) {
    case SubtitleModel::StartMsRole:
        return std::clamp<qint64>(value.toLongLong(), 0, event.endMs);
    case SubtitleModel::EndMsRole:
        return std::max(event.startMs, value.toLongLong());
    case SubtitleModel::MarginLeftRole:
    case SubtitleModel::MarginRightRole:
    case SubtitleModel::MarginVerticalRole:
        return std::max(0, value.toInt());
    case SubtitleModel::LayerRole:
        return value.toInt();
    case SubtitleModel::CommentRole:
        return value.toBool();
    case SubtitleModel::StyleRole:
    case SubtitleModel::ActorRole:
    case SubtitleModel::EffectRole:
    case SubtitleModel::TextRole:
        return value.toString();
    default:
        return value;
    }
}

QString writeFile(const QString &path, const QByteArray &contents)
{
    QSaveFile file(path);
    if (!file.open(QIODevice::WriteOnly))
        return file.errorString();
    if (file.write(contents) != contents.size())
        return file.errorString();
    if (!file.commit())
        return file.errorString();
    return {};
}

} // namespace

class EditEventCommand final : public QUndoCommand {
public:
    EditEventCommand(DocumentContext *context, QUuid id, int role,
        QVariant before, QVariant after, QString description)
        : QUndoCommand(std::move(description)), m_context(context), m_id(std::move(id)),
          m_role(role), m_before(std::move(before)), m_after(std::move(after)),
          m_beforeState(context->m_currentStateId), m_afterState(context->allocateStateId()),
          m_mergeEpoch(context->m_mergeEpoch)
    {
    }

    void undo() override
    {
        m_context->m_lines->applyField(m_id, m_role, m_before);
        m_context->transitionToState(m_beforeState);
    }

    void redo() override
    {
        m_context->m_lines->applyField(m_id, m_role, m_after);
        m_context->transitionToState(m_afterState);
    }

    int id() const override { return 1000 + m_role; }

    bool mergeWith(const QUndoCommand *command) override
    {
        const auto *other = dynamic_cast<const EditEventCommand *>(command);
        if (!other || other->m_context != m_context || other->m_id != m_id
            || other->m_role != m_role || other->m_mergeEpoch != m_mergeEpoch)
            return false;
        m_after = other->m_after;
        m_afterState = other->m_afterState;
        return true;
    }

private:
    DocumentContext *m_context;
    QUuid m_id;
    int m_role;
    QVariant m_before;
    QVariant m_after;
    quint64 m_beforeState;
    quint64 m_afterState;
    quint64 m_mergeEpoch;
};

class ReplaceEventsCommand final : public QUndoCommand {
public:
    ReplaceEventsCommand(DocumentContext *context,
        QVector<ass::Event> before,
        models::SubtitleModel::SelectionSnapshot beforeSelection,
        QVector<ass::Event> after,
        models::SubtitleModel::SelectionSnapshot afterSelection,
        QString description)
        : QUndoCommand(std::move(description)), m_context(context),
          m_before(std::move(before)), m_beforeSelection(std::move(beforeSelection)),
          m_after(std::move(after)), m_afterSelection(std::move(afterSelection)),
          m_beforeState(context->m_currentStateId), m_afterState(context->allocateStateId())
    {
    }

    void undo() override
    {
        m_context->m_lines->applyEvents(m_before, m_beforeSelection);
        m_context->transitionToState(m_beforeState);
    }

    void redo() override
    {
        m_context->m_lines->applyEvents(m_after, m_afterSelection);
        m_context->transitionToState(m_afterState);
    }

private:
    DocumentContext *m_context;
    QVector<ass::Event> m_before;
    models::SubtitleModel::SelectionSnapshot m_beforeSelection;
    QVector<ass::Event> m_after;
    models::SubtitleModel::SelectionSnapshot m_afterSelection;
    quint64 m_beforeState;
    quint64 m_afterState;
};

DocumentContext::DocumentContext(ass::Document document, QUrl fileUrl, QObject *parent)
    : QObject(parent), m_document(std::move(document)), m_fileUrl(std::move(fileUrl)),
      m_lines(new models::SubtitleModel(this, &m_document)),
      m_media(new media::MediaSession(this)),
      m_karaoke(new timing::KaraokeSession(this)),
      m_renderer(std::make_shared<renderer::MangetsuSession>())
{
    connect(&m_undo, &QUndoStack::canUndoChanged, this, &DocumentContext::commandStateChanged);
    connect(&m_undo, &QUndoStack::canRedoChanged, this, &DocumentContext::commandStateChanged);
    connect(&m_undo, &QUndoStack::undoTextChanged, this, &DocumentContext::commandStateChanged);
    connect(&m_undo, &QUndoStack::redoTextChanged, this, &DocumentContext::commandStateChanged);
    connect(m_karaoke, &timing::KaraokeSession::dirtyChanged,
        this, &DocumentContext::modifiedChanged);
}

DocumentContext::~DocumentContext()
{
    delete m_karaoke;
    m_karaoke = nullptr;
    delete m_lines;
    m_lines = nullptr;
}

QString DocumentContext::title() const
{
    if (m_fileUrl.isLocalFile()) {
        const QString name = QFileInfo(m_fileUrl.toLocalFile()).fileName();
        if (!name.isEmpty())
            return name;
    }
    return QStringLiteral("Untitled");
}

const ass::Event *DocumentContext::activeEvent() const
{
    return m_lines->activeEvent();
}

QString DocumentContext::activeText() const { const auto *e = activeEvent(); return e ? e->text : QString{}; }
qint64 DocumentContext::activeStartMs() const { const auto *e = activeEvent(); return e ? e->startMs : 0; }
qint64 DocumentContext::activeEndMs() const { const auto *e = activeEvent(); return e ? e->endMs : 0; }
QString DocumentContext::activeStyle() const { const auto *e = activeEvent(); return e ? e->style : QString{}; }
QString DocumentContext::activeActor() const { const auto *e = activeEvent(); return e ? e->actor : QString{}; }
QString DocumentContext::activeEffect() const { const auto *e = activeEvent(); return e ? e->effect : QString{}; }
int DocumentContext::activeLayer() const { const auto *e = activeEvent(); return e ? e->layer : 0; }
bool DocumentContext::activeComment() const { const auto *e = activeEvent(); return e && e->comment; }

void DocumentContext::setActiveText(const QString &v) { if (const auto *e = activeEvent()) editEvent(e->id, models::SubtitleModel::TextRole, v); }
void DocumentContext::setActiveStartMs(qint64 v) { if (const auto *e = activeEvent()) editEvent(e->id, models::SubtitleModel::StartMsRole, v); }
void DocumentContext::setActiveEndMs(qint64 v) { if (const auto *e = activeEvent()) editEvent(e->id, models::SubtitleModel::EndMsRole, v); }
void DocumentContext::setActiveStyle(const QString &v) { if (const auto *e = activeEvent()) editEvent(e->id, models::SubtitleModel::StyleRole, v); }
void DocumentContext::setActiveActor(const QString &v) { if (const auto *e = activeEvent()) editEvent(e->id, models::SubtitleModel::ActorRole, v); }
void DocumentContext::setActiveEffect(const QString &v) { if (const auto *e = activeEvent()) editEvent(e->id, models::SubtitleModel::EffectRole, v); }
void DocumentContext::setActiveLayer(int v) { if (const auto *e = activeEvent()) editEvent(e->id, models::SubtitleModel::LayerRole, v); }
void DocumentContext::setActiveComment(bool v) { if (const auto *e = activeEvent()) editEvent(e->id, models::SubtitleModel::CommentRole, v); }

void DocumentContext::undo() { if (!m_karaoke->active()) m_undo.undo(); }
void DocumentContext::redo() { if (!m_karaoke->active()) m_undo.redo(); }

void DocumentContext::editEvent(const QUuid &id, int role, const QVariant &value)
{
    if (m_karaoke->active())
        return;
    const int row = m_document.eventIndex(id);
    if (row < 0)
        return;
    const ass::Event &event = m_document.events()[row];
    const QVariant before = eventValue(event, role);
    const QVariant after = normalizedEventValue(event, role, value);
    if (!before.isValid() || before == after)
        return;
    static const QHash<int, QString> descriptions = {
        {models::SubtitleModel::TextRole, tr("Edit subtitle text")},
        {models::SubtitleModel::StartMsRole, tr("Set start time")},
        {models::SubtitleModel::EndMsRole, tr("Set end time")},
        {models::SubtitleModel::StyleRole, tr("Set style")},
        {models::SubtitleModel::ActorRole, tr("Set actor")},
        {models::SubtitleModel::EffectRole, tr("Set effect")},
        {models::SubtitleModel::CommentRole, tr("Toggle comment")}
    };
    m_undo.push(new EditEventCommand(this, id, role, before, after,
        descriptions.value(role, tr("Edit subtitle line"))));
}

void DocumentContext::commitKaraokeText(const QString &value)
{
    const ass::Event *event = activeEvent();
    if (!event || event->text == value)
        return;
    ++m_mergeEpoch;
    m_undo.push(new EditEventCommand(this, event->id, models::SubtitleModel::TextRole,
        event->text, value, tr("Commit karaoke timing")));
    ++m_mergeEpoch;
}

void DocumentContext::replaceEvents(QVector<ass::Event> events,
    const QString &description, const QUuid &preferredActive)
{
    if (events.isEmpty())
        events.push_back(ass::Event{});
    auto beforeSelection = m_lines->selectionSnapshot();
    auto afterSelection = beforeSelection;
    if (!preferredActive.isNull()) {
        afterSelection.activeId = preferredActive;
        afterSelection.selectedIds = {preferredActive};
        const auto it = std::find_if(events.cbegin(), events.cend(),
            [&preferredActive](const ass::Event &event) { return event.id == preferredActive; });
        afterSelection.anchorId = it == events.cend() ? QUuid{} : preferredActive;
    }
    m_undo.push(new ReplaceEventsCommand(this, m_document.events(), beforeSelection,
        std::move(events), std::move(afterSelection), description));
}

void DocumentContext::insertAfterActive()
{
    if (m_karaoke->active())
        return;
    QVector<ass::Event> events = m_document.events();
    const int row = std::max(0, m_lines->activeRow());
    ass::Event event;
    if (const ass::Event *active = activeEvent()) {
        event.startMs = active->endMs;
        event.endMs = active->endMs + 5000;
        event.style = active->style;
        event.actor = active->actor;
    }
    events.insert(std::min(row + 1, static_cast<int>(events.size())), event);
    replaceEvents(std::move(events), tr("Insert subtitle line"), event.id);
}

void DocumentContext::duplicateSelected()
{
    if (m_karaoke->active())
        return;
    QVector<ass::Event> events = m_document.events();
    const QVector<int> selected = m_lines->selectedRows();
    if (selected.isEmpty())
        return;
    int offset = 0;
    QUuid lastId;
    for (int row : selected) {
        ass::Event copy = events[row + offset];
        copy.id = QUuid::createUuid();
        events.insert(row + offset + 1, copy);
        lastId = copy.id;
        ++offset;
    }
    replaceEvents(std::move(events), tr("Duplicate subtitle lines"), lastId);
}

void DocumentContext::deleteSelected()
{
    if (m_karaoke->active())
        return;
    QVector<ass::Event> events = m_document.events();
    QVector<int> selected = m_lines->selectedRows();
    if (selected.isEmpty())
        return;
    const int fallbackRow = std::max(0, selected.front() - 1);
    std::sort(selected.begin(), selected.end(), std::greater<>());
    for (int row : selected)
        events.removeAt(row);
    if (events.isEmpty())
        events.push_back(ass::Event{});
    const QUuid active = events[std::min(fallbackRow, static_cast<int>(events.size()) - 1)].id;
    replaceEvents(std::move(events), tr("Delete subtitle lines"), active);
}

void DocumentContext::toggleSelectedComments()
{
    if (m_karaoke->active())
        return;
    QVector<ass::Event> events = m_document.events();
    const QVector<int> selected = m_lines->selectedRows();
    if (selected.isEmpty())
        return;
    const bool makeComment = std::any_of(selected.cbegin(), selected.cend(),
        [&events](int row) { return !events[row].comment; });
    for (int row : selected)
        events[row].comment = makeComment;
    replaceEvents(std::move(events), tr("Toggle subtitle comments"));
}

void DocumentContext::setSelectedTiming(qint64 startMs, qint64 endMs)
{
    if (m_karaoke->active())
        return;
    const int row = m_lines->activeRow();
    if (row < 0)
        return;
    QVector<ass::Event> events = m_document.events();
    const qint64 normalizedStart = std::max<qint64>(0, startMs);
    const qint64 normalizedEnd = std::max(normalizedStart, endMs);
    if (events[row].startMs == normalizedStart && events[row].endMs == normalizedEnd)
        return;
    events[row].startMs = normalizedStart;
    events[row].endMs = normalizedEnd;
    replaceEvents(std::move(events), tr("Set subtitle timing"));
}

void DocumentContext::transitionToState(quint64 stateId)
{
    const bool wasModified = modified();
    m_currentStateId = stateId;
    if (wasModified != modified())
        emit modifiedChanged();
    emit rendererRevisionChanged(++m_rendererRevision);
    emit activeLineChanged();
}

void DocumentContext::save(const QUrl &target)
{
    if (m_saving)
        return;
    const QUrl destination = target.isEmpty() ? m_fileUrl : target;
    if (!destination.isLocalFile()) {
        emit savePathRequired();
        return;
    }
    if (m_karaoke->dirty())
        m_karaoke->commit();

    const QByteArray contents = m_document.serialize();
    const QString path = destination.toLocalFile();
    const quint64 savedState = m_currentStateId;
    ++m_mergeEpoch;
    m_saving = true;
    emit savingChanged();

    auto *watcher = new QFutureWatcher<QString>(this);
    const QPointer<DocumentContext> guard(this);
    connect(watcher, &QFutureWatcher<QString>::finished, this,
        [guard, watcher, destination, savedState] {
            const QString error = watcher->result();
            watcher->deleteLater();
            if (!guard)
                return;
            guard->m_saving = false;
            emit guard->savingChanged();
            if (error.isEmpty()) {
                const bool titleChanged = guard->m_fileUrl != destination;
                const bool wasModified = guard->modified();
                guard->m_fileUrl = destination;
                guard->m_savedStateId = savedState;
                emit guard->fileUrlChanged();
                if (titleChanged)
                    emit guard->titleChanged();
                if (wasModified != guard->modified())
                    emit guard->modifiedChanged();
            }
            emit guard->saveFinished(error.isEmpty(), error);
        });
    watcher->setFuture(QtConcurrent::run(writeFile, path, contents));
}

} // namespace yoake::app
