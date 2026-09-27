#include "app/document_context.h"

#include "ass/ass_event_clipboard.h"
#include "ass/ass_event_edits.h"
#include "media/media_session.h"
#include "models/subtitle_model.h"
#include "renderer/mangetsu_session.h"
#include "ui/video_viewport.h"
#include "ui/visual_tool_manager.h"

#include <QtConcurrent/QtConcurrentRun>
#include <QtCore/QFileInfo>
#include <QtCore/QDir>
#include <QtCore/QFutureWatcher>
#include <QtCore/QHash>
#include <QtCore/QMetaObject>
#include <QtCore/QPointer>
#include <QtCore/QSet>
#include <QtCore/QSaveFile>
#include <QtGui/QClipboard>
#include <QtGui/QGuiApplication>
#include <QtCore/QMimeData>

#include <algorithm>
#include <functional>
#include <limits>
#include <utility>

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
        QVariant before, QVariant after,
        models::SubtitleModel::SelectionSnapshot beforeSelection,
        models::SubtitleModel::SelectionSnapshot afterSelection,
        QString description)
        : QUndoCommand(std::move(description)), m_context(context), m_id(std::move(id)),
          m_role(role), m_before(std::move(before)), m_after(std::move(after)),
          m_beforeSelection(std::move(beforeSelection)),
          m_afterSelection(std::move(afterSelection)),
          m_beforeState(context->m_currentStateId), m_afterState(context->allocateStateId()),
          m_mergeEpoch(context->m_mergeEpoch)
    {
    }

    void undo() override
    {
        m_context->m_lines->applyField(m_id, m_role, m_before);
        m_context->m_lines->restoreSelection(m_beforeSelection);
        m_context->transitionToState(m_beforeState);
        if (m_role == models::SubtitleModel::ActorRole
            || m_role == models::SubtitleModel::EffectRole) {
            emit m_context->suggestionsChanged();
        }
    }

    void redo() override
    {
        m_context->m_lines->applyField(m_id, m_role, m_after);
        m_context->m_lines->restoreSelection(m_afterSelection);
        m_context->transitionToState(m_afterState);
        if (m_role == models::SubtitleModel::ActorRole
            || m_role == models::SubtitleModel::EffectRole) {
            emit m_context->suggestionsChanged();
        }
    }

    int id() const override { return 1000 + m_role; }

    bool mergeWith(const QUndoCommand *command) override
    {
        const auto *other = dynamic_cast<const EditEventCommand *>(command);
        if (!other || other->m_context != m_context || other->m_id != m_id
            || other->m_role != m_role || other->m_mergeEpoch != m_mergeEpoch)
            return false;
        m_after = other->m_after;
        m_afterSelection = other->m_afterSelection;
        m_afterState = other->m_afterState;
        return true;
    }

private:
    DocumentContext *m_context;
    QUuid m_id;
    int m_role;
    QVariant m_before;
    QVariant m_after;
    models::SubtitleModel::SelectionSnapshot m_beforeSelection;
    models::SubtitleModel::SelectionSnapshot m_afterSelection;
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
        emit m_context->suggestionsChanged();
    }

    void redo() override
    {
        m_context->m_lines->applyEvents(m_after, m_afterSelection);
        m_context->transitionToState(m_afterState);
        emit m_context->suggestionsChanged();
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
    m_videoViewport = new ui::VideoViewport(this);
    const auto &scriptInfo = m_document.projectProperties();
    m_videoViewport->setScriptSize(QSizeF(scriptInfo.playResX, scriptInfo.playResY));
    m_visualTools = new ui::VisualToolManager(this, m_videoViewport, this);
    connect(m_media, &media::MediaSession::metadataChanged, this, [this] {
        m_videoViewport->setVideoSize(QSizeF(m_media->sourceWidth(), m_media->sourceHeight()));
    });
    connect(&m_undo, &QUndoStack::canUndoChanged, this, &DocumentContext::commandStateChanged);
    connect(&m_undo, &QUndoStack::canRedoChanged, this, &DocumentContext::commandStateChanged);
    connect(&m_undo, &QUndoStack::undoTextChanged, this, &DocumentContext::commandStateChanged);
    connect(&m_undo, &QUndoStack::redoTextChanged, this, &DocumentContext::commandStateChanged);
    connect(m_karaoke, &timing::KaraokeSession::dirtyChanged,
        this, &DocumentContext::modifiedChanged);
    if (QGuiApplication::clipboard()) {
        connect(QGuiApplication::clipboard(), &QClipboard::dataChanged,
            this, &DocumentContext::clipboardChanged);
    }
    connect(m_lines, &models::SubtitleModel::activeRowChanged, this, [this] {
        // Keep later typing on a reselected line separate from the last edit epoch.
        ++m_mergeEpoch;
    });
    const auto &project = m_document.projectProperties();
    if (!m_document.events().isEmpty())
        m_lines->setActiveRow(std::clamp(
            project.activeRow, 0, static_cast<int>(m_document.events().size()) - 1));
    connect(m_media, &media::MediaSession::metadataChanged, this, [this] {
        if (m_pendingLinkedVideoFrame < 0 || !m_media->hasVideo())
            return;
        const int frame = std::exchange(m_pendingLinkedVideoFrame, -1);
        QMetaObject::invokeMethod(this, [this, frame] {
            if (m_media->hasVideo())
                m_media->requestFrame(frame);
        }, Qt::QueuedConnection);
    });
    if (m_fileUrl.isLocalFile()
        && (!project.videoFile.isEmpty() || !project.audioFile.isEmpty())) {
        QMetaObject::invokeMethod(this, &DocumentContext::loadLinkedMedia, Qt::QueuedConnection);
    }
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

QStringList DocumentContext::actorSuggestions() const
{
    QStringList values;
    QSet<QString> seen;
    for (const ass::Event &event : m_document.events()) {
        if (event.actor.isEmpty() || seen.contains(event.actor.toCaseFolded()))
            continue;
        seen.insert(event.actor.toCaseFolded());
        values.push_back(event.actor);
    }
    return values;
}

QStringList DocumentContext::effectSuggestions() const
{
    QStringList values;
    QSet<QString> seen;
    for (const ass::Event &event : m_document.events()) {
        if (event.effect.isEmpty() || seen.contains(event.effect.toCaseFolded()))
            continue;
        seen.insert(event.effect.toCaseFolded());
        values.push_back(event.effect);
    }
    return values;
}

const ass::Event *DocumentContext::activeEvent() const
{
    return m_lines->activeEvent();
}

ass::Event DocumentContext::activeEventSnapshot() const
{
    const ass::Event *event = activeEvent();
    return event ? *event : ass::Event{};
}

QString DocumentContext::activeText() const { const auto *e = activeEvent(); return e ? e->text : QString{}; }
qint64 DocumentContext::activeStartMs() const { const auto *e = activeEvent(); return e ? e->startMs : 0; }
qint64 DocumentContext::activeEndMs() const { const auto *e = activeEvent(); return e ? e->endMs : 0; }
QString DocumentContext::activeStyle() const { const auto *e = activeEvent(); return e ? e->style : QString{}; }
QString DocumentContext::activeActor() const { const auto *e = activeEvent(); return e ? e->actor : QString{}; }
QString DocumentContext::activeEffect() const { const auto *e = activeEvent(); return e ? e->effect : QString{}; }
int DocumentContext::activeLayer() const { const auto *e = activeEvent(); return e ? e->layer : 0; }
int DocumentContext::activeMarginLeft() const { const auto *e = activeEvent(); return e ? e->marginLeft : 0; }
int DocumentContext::activeMarginRight() const { const auto *e = activeEvent(); return e ? e->marginRight : 0; }
int DocumentContext::activeMarginVertical() const { const auto *e = activeEvent(); return e ? e->marginVertical : 0; }
bool DocumentContext::activeComment() const { const auto *e = activeEvent(); return e && e->comment; }

void DocumentContext::setActiveText(const QString &v) { if (const auto *e = activeEvent()) editEvent(e->id, models::SubtitleModel::TextRole, v); }
void DocumentContext::setActiveStartMs(qint64 v) { if (const auto *e = activeEvent()) editEvent(e->id, models::SubtitleModel::StartMsRole, v); }
void DocumentContext::setActiveEndMs(qint64 v) { if (const auto *e = activeEvent()) editEvent(e->id, models::SubtitleModel::EndMsRole, v); }
void DocumentContext::setActiveStyle(const QString &v) { if (const auto *e = activeEvent()) editEvent(e->id, models::SubtitleModel::StyleRole, v); }
void DocumentContext::setActiveActor(const QString &v) { if (const auto *e = activeEvent()) editEvent(e->id, models::SubtitleModel::ActorRole, v); }
void DocumentContext::setActiveEffect(const QString &v) { if (const auto *e = activeEvent()) editEvent(e->id, models::SubtitleModel::EffectRole, v); }
void DocumentContext::setActiveLayer(int v) { if (const auto *e = activeEvent()) editEvent(e->id, models::SubtitleModel::LayerRole, v); }
void DocumentContext::setActiveMarginLeft(int v) { if (const auto *e = activeEvent()) editEvent(e->id, models::SubtitleModel::MarginLeftRole, v); }
void DocumentContext::setActiveMarginRight(int v) { if (const auto *e = activeEvent()) editEvent(e->id, models::SubtitleModel::MarginRightRole, v); }
void DocumentContext::setActiveMarginVertical(int v) { if (const auto *e = activeEvent()) editEvent(e->id, models::SubtitleModel::MarginVerticalRole, v); }
void DocumentContext::setActiveComment(bool v) { if (const auto *e = activeEvent()) editEvent(e->id, models::SubtitleModel::CommentRole, v); }

QString DocumentContext::resolveLinkedPath(const QString &value) const
{
    const QString trimmed = value.trimmed();
    if (trimmed.isEmpty() || trimmed.startsWith(QStringLiteral("?dummy"), Qt::CaseInsensitive)
        || trimmed.startsWith(QStringLiteral("dummy-audio:"), Qt::CaseInsensitive)) {
        return {};
    }
    QString pathValue = trimmed;
    if (pathValue.startsWith(QStringLiteral("?script"), Qt::CaseInsensitive)) {
        pathValue.remove(0, 7);
        while (pathValue.startsWith(u'/') || pathValue.startsWith(u'\\'))
            pathValue.removeFirst();
    }
    const QUrl asUrl(pathValue);
    if (asUrl.isLocalFile())
        return QFileInfo(asUrl.toLocalFile()).absoluteFilePath();
    const QString native = QDir::fromNativeSeparators(pathValue);
    if (QFileInfo(native).isAbsolute())
        return QFileInfo(native).absoluteFilePath();
    if (!m_fileUrl.isLocalFile())
        return {};
    return QFileInfo(QDir(QFileInfo(m_fileUrl.toLocalFile()).absolutePath()).absoluteFilePath(native))
        .absoluteFilePath();
}

void DocumentContext::loadLinkedMedia()
{
    const auto &project = m_document.projectProperties();
    const QString linkedValue = !project.videoFile.isEmpty() ? project.videoFile : project.audioFile;
    const QString path = resolveLinkedPath(linkedValue);
    QString error;
    if (path.isEmpty()) {
        error = tr("The ASS linked-media entry is empty, unsupported, or uses dummy media: %1")
                    .arg(linkedValue);
    } else if (!QFileInfo(path).isFile()) {
        error = tr("The media referenced by the ASS file was not found: %1").arg(path);
    }
    if (!error.isEmpty()) {
        m_pendingLinkedVideoFrame = -1;
        if (m_linkedMediaError != error) {
            m_linkedMediaError = error;
            emit linkedMediaStatusChanged();
        }
        return;
    }

    if (!m_linkedMediaError.isEmpty()) {
        m_linkedMediaError.clear();
        emit linkedMediaStatusChanged();
    }
    m_pendingLinkedVideoFrame = project.videoFile.isEmpty() ? -1 : project.videoPosition;
    m_media->open(QUrl::fromLocalFile(path));
}

void DocumentContext::undo()
{
    if (!m_visualEditId.isNull()) {
        cancelVisualTextEdit();
        return;
    }
    if (m_karaoke->active() || !m_undo.canUndo())
        return;
    ++m_mergeEpoch;
    m_undo.undo();
}

void DocumentContext::redo()
{
    if (!m_visualEditId.isNull()) {
        cancelVisualTextEdit();
        return;
    }
    if (m_karaoke->active() || !m_undo.canRedo())
        return;
    ++m_mergeEpoch;
    m_undo.redo();
}

void DocumentContext::editEvent(const QUuid &id, int role, const QVariant &value)
{
    if (!m_visualEditId.isNull())
        cancelVisualTextEdit();
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
        {models::SubtitleModel::LayerRole, tr("Set layer")},
        {models::SubtitleModel::MarginLeftRole, tr("Set left margin")},
        {models::SubtitleModel::MarginRightRole, tr("Set right margin")},
        {models::SubtitleModel::MarginVerticalRole, tr("Set vertical margin")},
        {models::SubtitleModel::CommentRole, tr("Toggle comment")}
    };
    if (role == models::SubtitleModel::TextRole && m_findMatchId == id) {
        m_findMatchId = {};
        m_findMatchStart = -1;
        m_findMatchLength = 0;
        m_findQuery.clear();
        emit findMatchChanged();
    }
    const auto selection = m_lines->selectionSnapshot();
    m_undo.push(new EditEventCommand(this, id, role, before, after,
        selection, selection, descriptions.value(role, tr("Edit subtitle line"))));
}

bool DocumentContext::beginVisualTextEdit(const QString &eventId)
{
    if (m_karaoke->active() || m_saving || !m_visualEditId.isNull())
        return false;
    const QUuid id(eventId);
    const int row = m_document.eventIndex(id);
    const ass::Event *active = activeEvent();
    if (row < 0 || !active || id != active->id)
        return false;
    m_visualEditId = id;
    m_visualEditBefore = m_document.events().at(row).text;
    m_visualEditCurrent = m_visualEditBefore;
    ++m_mergeEpoch;
    return true;
}

void DocumentContext::previewVisualTextEdit(const QString &text)
{
    if (m_visualEditId.isNull() || text == m_visualEditCurrent)
        return;
    if (m_document.eventIndex(m_visualEditId) < 0) {
        cancelVisualTextEdit();
        return;
    }
    m_visualEditCurrent = text;
    m_lines->applyField(m_visualEditId, models::SubtitleModel::TextRole, text);
    emit rendererRevisionChanged(++m_rendererRevision);
}

void DocumentContext::commitVisualTextEdit()
{
    if (m_visualEditId.isNull())
        return;
    const QUuid id = std::exchange(m_visualEditId, QUuid{});
    const QString before = std::exchange(m_visualEditBefore, QString{});
    const QString after = std::exchange(m_visualEditCurrent, QString{});
    if (before != after && m_document.eventIndex(id) >= 0) {
        const auto selection = m_lines->selectionSnapshot();
        ++m_mergeEpoch;
        m_undo.push(new EditEventCommand(this, id, models::SubtitleModel::TextRole,
            before, after, selection, selection, tr("Visual subtitle edit")));
        ++m_mergeEpoch;
    }
}

void DocumentContext::cancelVisualTextEdit()
{
    if (m_visualEditId.isNull())
        return;
    const QUuid id = std::exchange(m_visualEditId, QUuid{});
    const QString before = std::exchange(m_visualEditBefore, QString{});
    const QString current = std::exchange(m_visualEditCurrent, QString{});
    if (current != before && m_document.eventIndex(id) >= 0) {
        m_lines->applyField(id, models::SubtitleModel::TextRole, before);
        emit rendererRevisionChanged(++m_rendererRevision);
    }
    ++m_mergeEpoch;
}

void DocumentContext::commitKaraokeText(const QString &value)
{
    const ass::Event *event = activeEvent();
    if (!event || event->text == value)
        return;
    ++m_mergeEpoch;
    const auto selection = m_lines->selectionSnapshot();
    m_undo.push(new EditEventCommand(this, event->id, models::SubtitleModel::TextRole,
        event->text, value, selection, selection, tr("Commit karaoke timing")));
    ++m_mergeEpoch;
}

void DocumentContext::replaceEvents(QVector<ass::Event> events,
    const QString &description, const QUuid &preferredActive,
    const QVector<QUuid> &preferredSelection)
{
    if (!m_visualEditId.isNull())
        cancelVisualTextEdit();
    if (events == m_document.events())
        return;
    auto beforeSelection = m_lines->selectionSnapshot();
    auto afterSelection = beforeSelection;
    if (!preferredSelection.isEmpty()) {
        afterSelection.activeId = preferredActive.isNull() ? preferredSelection.back() : preferredActive;
        afterSelection.selectedIds.clear();
        for (const QUuid &id : preferredSelection)
            afterSelection.selectedIds.insert(id);
        afterSelection.anchorId = afterSelection.activeId;
    } else if (!preferredActive.isNull()) {
        afterSelection.activeId = preferredActive;
        afterSelection.selectedIds = {preferredActive};
        const auto it = std::find_if(events.cbegin(), events.cend(),
            [&preferredActive](const ass::Event &event) { return event.id == preferredActive; });
        afterSelection.anchorId = it == events.cend() ? QUuid{} : preferredActive;
    }
    m_undo.push(new ReplaceEventsCommand(this, m_document.events(), beforeSelection,
        std::move(events), std::move(afterSelection), description));
}

void DocumentContext::insertBeforeActive() { insertAtActive(true); }
void DocumentContext::insertAfterActive() { insertAtActive(false); }

void DocumentContext::insertAtActive(bool before)
{
    if (karaokeOwnsLine())
        return;
    QVector<ass::Event> events = m_document.events();
    const int activeRow = m_lines->activeRow();
    const int row = activeRow < 0 ? 0 : activeRow;
    ass::Event event;
    if (const ass::Event *active = activeEvent()) {
        // Use an adjacent gap when one exists. With no gap, insert a zero-duration
        // line at the boundary instead of creating negative or inverted timing.
        constexpr qint64 preferredDurationMs = 2000;
        const ass::Event *neighbor = before
            ? (row > 0 ? &events[row - 1] : nullptr)
            : (row + 1 < events.size() ? &events[row + 1] : nullptr);
        if (before) {
            event.endMs = active->startMs;
            const qint64 lowerBound = neighbor ? neighbor->endMs : 0;
            event.startMs = event.endMs >= lowerBound
                ? std::max(lowerBound, event.endMs - preferredDurationMs) : event.endMs;
        } else {
            event.startMs = active->endMs;
            const qint64 preferredEnd = event.startMs > std::numeric_limits<qint64>::max() - preferredDurationMs
                ? std::numeric_limits<qint64>::max()
                : event.startMs + preferredDurationMs;
            if (neighbor) {
                event.endMs = neighbor->startMs >= event.startMs
                    ? std::min(neighbor->startMs, preferredEnd)
                    : event.startMs;
            } else {
                event.endMs = preferredEnd;
            }
        }
        event.style = active->style;
        event.actor = active->actor;
        event.layer = active->layer;
        event.marginLeft = active->marginLeft;
        event.marginRight = active->marginRight;
        event.marginVertical = active->marginVertical;
    }
    events.insert(before ? row : std::min(row + 1, static_cast<int>(events.size())), event);
    replaceEvents(std::move(events), tr("Insert subtitle line"), event.id);
}

void DocumentContext::duplicateSelected()
{
    if (karaokeOwnsLine())
        return;
    QVector<ass::Event> events = m_document.events();
    const QVector<int> selected = m_lines->selectedRows();
    if (selected.isEmpty())
        return;
    int offset = 0;
    QUuid lastId;
    QVector<QUuid> copies;
    for (int row : selected) {
        ass::Event copy = events[row + offset];
        copy.id = QUuid::createUuid();
        events.insert(row + offset + 1, copy);
        lastId = copy.id;
        copies.push_back(copy.id);
        ++offset;
    }
    replaceEvents(std::move(events), tr("Duplicate subtitle lines"), lastId, copies);
}

void DocumentContext::deleteSelected()
{
    if (karaokeOwnsLine())
        return;
    QVector<ass::Event> events = m_document.events();
    QVector<int> selected = m_lines->selectedRows();
    if (selected.isEmpty())
        return;
    const int oldActiveRow = m_lines->activeRow();
    const int removedBeforeActive = static_cast<int>(std::count_if(selected.cbegin(), selected.cend(),
        [oldActiveRow](int row) { return row < oldActiveRow; }));
    std::sort(selected.begin(), selected.end(), std::greater<>());
    for (int row : selected)
        events.removeAt(row);
    QUuid active;
    QVector<QUuid> selection;
    if (!events.isEmpty()) {
        const int fallbackRow = std::clamp(oldActiveRow - removedBeforeActive, 0,
            static_cast<int>(events.size()) - 1);
        active = events[fallbackRow].id;
        selection.push_back(active);
    }
    replaceEvents(std::move(events), tr("Delete subtitle lines"), active, selection);
}

void DocumentContext::copySelected()
{
    const QVector<int> rows = m_lines->selectedRows();
    if (rows.isEmpty() || !QGuiApplication::clipboard())
        return;
    QVector<ass::Event> selectedEvents;
    selectedEvents.reserve(rows.size());
    for (int row : rows) {
        selectedEvents.push_back(m_document.events()[row]);
    }
    auto *mime = new QMimeData;
    mime->setData(ass::EventClipboard::mimeType(), ass::EventClipboard::encode(selectedEvents));
    mime->setText(ass::EventClipboard::plainText(selectedEvents));
    QGuiApplication::clipboard()->setMimeData(mime);
}

void DocumentContext::cutSelected()
{
    if (karaokeOwnsLine())
        return;
    copySelected();
    deleteSelected();
}

bool DocumentContext::canPasteRows() const
{
    const QClipboard *clipboard = QGuiApplication::clipboard();
    return clipboard && clipboard->mimeData()
        && clipboard->mimeData()->hasFormat(ass::EventClipboard::mimeType());
}

void DocumentContext::pasteRows()
{
    if (karaokeOwnsLine() || !canPasteRows())
        return;
    const QByteArray data = QGuiApplication::clipboard()->mimeData()->data(ass::EventClipboard::mimeType());
    QVector<ass::Event> pasted;
    if (!ass::EventClipboard::decode(data, &pasted) || pasted.isEmpty())
        return;
    QVector<ass::Event> events = m_document.events();
    // Pasted rows always follow the active event; an empty document starts at row zero.
    const int insertionRow = m_lines->activeRow() < 0
        ? 0 : m_lines->activeRow() + 1;
    QVector<QUuid> selected;
    selected.reserve(pasted.size());
    for (const ass::Event &event : pasted)
        selected.push_back(event.id);
    for (int offset = 0; offset < pasted.size(); ++offset)
        events.insert(insertionRow + offset, pasted[offset]);
    replaceEvents(std::move(events), tr("Paste subtitle lines"), selected.back(), selected);
}

void DocumentContext::toggleSelectedComments()
{
    if (karaokeOwnsLine())
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

void DocumentContext::joinSelected()
{
    if (karaokeOwnsLine())
        return;
    const QVector<int> selected = m_lines->selectedRows();
    if (selected.size() < 2)
        return;
    QVector<ass::Event> events = m_document.events();
    ass::Event joined = events[selected.front()];
    joined.startMs = events[selected.front()].startMs;
    joined.endMs = events[selected.front()].endMs;
    QStringList texts;
    for (int row : selected) {
        joined.startMs = std::min(joined.startMs, events[row].startMs);
        joined.endMs = std::max(joined.endMs, events[row].endMs);
        texts.push_back(events[row].text);
    }
    // The first selected event supplies style, actor, effect, margins, layer,
    // and comment state. Explicit ASS line breaks keep the joined dialogue readable.
    joined.text = texts.join(QStringLiteral("\\N"));
    const QUuid survivor = joined.id;
    QSet<QUuid> selectedIds;
    for (int row : selected)
        selectedIds.insert(events[row].id);
    QVector<ass::Event> result;
    result.reserve(events.size() - selected.size() + 1);
    for (const ass::Event &event : events) {
        if (event.id == survivor)
            result.push_back(joined);
        else if (!selectedIds.contains(event.id))
            result.push_back(event);
    }
    replaceEvents(std::move(result), tr("Join subtitle lines"), survivor);
}

void DocumentContext::splitActiveAtCursor(int utf16Position)
{
    if (karaokeOwnsLine())
        return;
    const ass::Event *active = activeEvent();
    if (!active)
        return;
    QVector<ass::Event> events = m_document.events();
    const int row = m_lines->activeRow();
    const auto split = ass::EventEdits::splitAtCursor(*active, utf16Position);
    if (!split)
        return;
    events[row] = split->first;
    events.insert(row + 1, split->second);
    replaceEvents(std::move(events), tr("Split subtitle line"), split->second.id,
        {split->first.id, split->second.id});
}

void DocumentContext::splitActiveAtCursorAtPosition(int utf16Position)
{
    if (karaokeOwnsLine())
        return;
    const ass::Event *active = activeEvent();
    if (!active)
        return;
    const qint64 position = m_media->positionMs();
    if (!m_media->hasMedia() || position <= active->startMs || position >= active->endMs)
        return;
    QVector<ass::Event> events = m_document.events();
    const int row = m_lines->activeRow();
    const auto split = ass::EventEdits::splitAtCursor(*active, utf16Position, position);
    if (!split)
        return;
    events[row] = split->first;
    events.insert(row + 1, split->second);
    replaceEvents(std::move(events), tr("Split subtitle at current position"), split->second.id,
        {split->first.id, split->second.id});
}

void DocumentContext::moveSelectedUp()
{
    if (karaokeOwnsLine())
        return;
    QVector<ass::Event> events = m_document.events();
    const QSet<QUuid> selected = m_lines->selectionSnapshot().selectedIds;
    if (selected.isEmpty())
        return;
    for (int row = 1; row < events.size(); ++row) {
        if (selected.contains(events[row].id) && !selected.contains(events[row - 1].id))
            events.swapItemsAt(row, row - 1);
    }
    const auto snapshot = m_lines->selectionSnapshot();
    QVector<QUuid> selectedIds;
    for (int row = 0; row < events.size(); ++row) {
        if (snapshot.selectedIds.contains(events[row].id))
            selectedIds.push_back(events[row].id);
    }
    replaceEvents(std::move(events), tr("Move subtitle lines up"), snapshot.activeId, selectedIds);
}

void DocumentContext::moveSelectedDown()
{
    if (karaokeOwnsLine())
        return;
    QVector<ass::Event> events = m_document.events();
    const QSet<QUuid> selected = m_lines->selectionSnapshot().selectedIds;
    if (selected.isEmpty())
        return;
    for (int row = static_cast<int>(events.size()) - 2; row >= 0; --row) {
        if (selected.contains(events[row].id) && !selected.contains(events[row + 1].id))
            events.swapItemsAt(row, row + 1);
    }
    const auto snapshot = m_lines->selectionSnapshot();
    QVector<QUuid> selectedIds;
    for (const ass::Event &event : events) {
        if (snapshot.selectedIds.contains(event.id))
            selectedIds.push_back(event.id);
    }
    replaceEvents(std::move(events), tr("Move subtitle lines down"), snapshot.activeId, selectedIds);
}

void DocumentContext::setSelectedTiming(qint64 startMs, qint64 endMs)
{
    if (karaokeOwnsLine())
        return;
    const QVector<int> rows = m_lines->selectedRows();
    if (rows.isEmpty())
        return;
    QVector<ass::Event> events = m_document.events();
    const qint64 normalizedStart = std::max<qint64>(0, startMs);
    const qint64 normalizedEnd = std::max(normalizedStart, endMs);
    for (int row : rows) {
        events[row].startMs = normalizedStart;
        events[row].endMs = normalizedEnd;
    }
    replaceEvents(std::move(events), tr("Set subtitle timing"));
}

void DocumentContext::setSelectedStartToPosition()
{
    if (karaokeOwnsLine() || !m_media->hasMedia())
        return;
    const qint64 position = std::max<qint64>(0, m_media->positionMs());
    QVector<ass::Event> events = m_document.events();
    const QVector<int> rows = m_lines->selectedRows();
    for (int row : rows)
        events[row].startMs = std::min(position, events[row].endMs);
    if (!rows.isEmpty())
        replaceEvents(std::move(events), tr("Set subtitle start to current position"));
}

void DocumentContext::setSelectedEndToPosition()
{
    if (karaokeOwnsLine() || !m_media->hasMedia())
        return;
    const qint64 position = std::max<qint64>(0, m_media->positionMs());
    QVector<ass::Event> events = m_document.events();
    const QVector<int> rows = m_lines->selectedRows();
    for (int row : rows)
        events[row].endMs = std::max(position, events[row].startMs);
    if (!rows.isEmpty())
        replaceEvents(std::move(events), tr("Set subtitle end to current position"));
}

void DocumentContext::shiftSelectedTiming(qint64 deltaMs)
{
    if (karaokeOwnsLine() || deltaMs == 0)
        return;
    QVector<ass::Event> events = m_document.events();
    const QVector<int> rows = m_lines->selectedRows();
    for (int row : rows) {
        ass::Event &event = events[row];
        const qint64 duration = event.endMs - event.startMs;
        qint64 shiftedStart;
        if (deltaMs < -event.startMs)
            shiftedStart = 0;
        else if (deltaMs > 0 && event.startMs > std::numeric_limits<qint64>::max() - deltaMs)
            shiftedStart = std::numeric_limits<qint64>::max() - duration;
        else
            shiftedStart = std::max<qint64>(0, event.startMs + deltaMs);
        event.startMs = shiftedStart;
        event.endMs = shiftedStart + duration;
    }
    if (!rows.isEmpty())
        replaceEvents(std::move(events), tr("Shift subtitle timing"));
}

bool DocumentContext::findText(const QString &query, bool caseSensitive, bool backwards)
{
    return findTextFrom(query, caseSensitive ? Qt::CaseSensitive : Qt::CaseInsensitive, backwards);
}

bool DocumentContext::findTextFrom(const QString &query, Qt::CaseSensitivity sensitivity, bool backwards)
{
    if (query.isEmpty() || m_document.events().isEmpty()) {
        if (!m_findMatchId.isNull() || m_findMatchStart >= 0 || m_findMatchLength > 0) {
            m_findQuery = query;
            m_findMatchId = {};
            m_findMatchStart = -1;
            m_findMatchLength = 0;
            emit findMatchChanged();
        }
        return false;
    }
    const int count = static_cast<int>(m_document.events().size());
    const int activeRow = m_lines->activeRow();
    if (activeRow < 0)
        return false;
    if (m_karaoke->active() && m_karaoke->dirty())
        return false;

    int initialOffset = backwards
        ? m_document.events()[activeRow].text.size() - query.size() : 0;
    if (m_findMatchId == m_document.events()[activeRow].id
        && m_findQuery == query && m_findMatchStart >= 0) {
        initialOffset = backwards ? m_findMatchStart - 1
                                  : m_findMatchStart + m_findMatchLength;
    }
    for (int step = 0; step <= count; ++step) {
        const int row = (activeRow + (backwards ? -step : step) + count * 2) % count;
        const QString &text = m_document.events()[row].text;
        int from = backwards ? text.size() - query.size() : 0;
        if (step == 0)
            from = initialOffset;
        const int position = backwards
            ? (from >= 0 ? text.lastIndexOf(query, from, sensitivity) : -1)
            : text.indexOf(query, std::max(0, from), sensitivity);
        if (position < 0)
            continue;
        m_findQuery = query;
        m_findMatchId = m_document.events()[row].id;
        m_findMatchStart = position;
        m_findMatchLength = query.size();
        m_lines->selectRow(row);
        emit findMatchChanged();
        return true;
    }
    const bool hadMatch = !m_findMatchId.isNull() || m_findMatchStart >= 0 || m_findMatchLength > 0;
    m_findQuery = query;
    m_findMatchId = {};
    m_findMatchStart = -1;
    m_findMatchLength = 0;
    if (hadMatch)
        emit findMatchChanged();
    return false;
}

bool DocumentContext::replaceCurrent(const QString &query, const QString &replacement, bool caseSensitive)
{
    if (query.isEmpty() || karaokeOwnsLine())
        return false;
    const Qt::CaseSensitivity sensitivity = caseSensitive ? Qt::CaseSensitive : Qt::CaseInsensitive;
    const ass::Event *event = activeEvent();
    if (!event || m_findMatchId != event->id || m_findQuery != query
        || m_findMatchStart < 0 || m_findMatchStart + m_findMatchLength > event->text.size()
        || event->text.mid(m_findMatchStart, m_findMatchLength).compare(query, sensitivity) != 0) {
        if (!findTextFrom(query, sensitivity, false))
            return false;
        event = activeEvent();
    }
    if (!event)
        return false;
    const int start = m_findMatchStart;
    QString changed = event->text;
    changed.replace(start, m_findMatchLength, replacement);
    if (changed == event->text)
        return false;
    const QUuid id = event->id;
    ++m_mergeEpoch;
    editEvent(id, models::SubtitleModel::TextRole, changed);
    ++m_mergeEpoch;
    m_findMatchStart = start;
    m_findMatchLength = replacement.size();
    m_findQuery = query;
    m_findMatchId = id;
    emit findMatchChanged();
    return true;
}

bool DocumentContext::replaceNext(const QString &query, const QString &replacement, bool caseSensitive)
{
    if (!replaceCurrent(query, replacement, caseSensitive))
        return false;
    m_findMatchStart += replacement.size();
    m_findMatchLength = 0;
    emit findMatchChanged();
    findTextFrom(query, caseSensitive ? Qt::CaseSensitive : Qt::CaseInsensitive, false);
    return true;
}

int DocumentContext::replaceAll(const QString &query, const QString &replacement, bool caseSensitive)
{
    if (query.isEmpty() || query == replacement || karaokeOwnsLine())
        return 0;
    const Qt::CaseSensitivity sensitivity = caseSensitive ? Qt::CaseSensitive : Qt::CaseInsensitive;
    QVector<ass::Event> events = m_document.events();
    int replacements = 0;
    for (ass::Event &event : events) {
        int from = 0;
        while (from <= event.text.size()) {
            const int position = event.text.indexOf(query, from, sensitivity);
            if (position < 0)
                break;
            event.text.replace(position, query.size(), replacement);
            from = position + replacement.size();
            ++replacements;
        }
    }
    if (replacements == 0)
        return 0;
    replaceEvents(std::move(events), tr("Replace all subtitle text"));
    m_findMatchId = {};
    m_findMatchStart = -1;
    m_findMatchLength = 0;
    m_findQuery.clear();
    emit findMatchChanged();
    return replacements;
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
    if (!m_visualEditId.isNull())
        commitVisualTextEdit();
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
