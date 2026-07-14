#pragma once

#include "ass/ass_document.h"
#include "media/media_session.h"
#include "models/subtitle_model.h"
#include "timing/karaoke_session.h"

#include <QtCore/QObject>
#include <QtCore/QUrl>
#include <QtCore/QVariant>
#include <QtGui/QUndoStack>

#include <memory>

namespace yoake::renderer { class MangetsuSession; }

namespace yoake::app {

class EditEventCommand;
class ReplaceEventsCommand;

class DocumentContext final : public QObject {
    Q_OBJECT
    Q_PROPERTY(QString title READ title NOTIFY titleChanged)
    Q_PROPERTY(QUrl fileUrl READ fileUrl NOTIFY fileUrlChanged)
    Q_PROPERTY(bool modified READ modified NOTIFY modifiedChanged)
    Q_PROPERTY(bool saving READ saving NOTIFY savingChanged)
    Q_PROPERTY(bool canUndo READ canUndo NOTIFY commandStateChanged)
    Q_PROPERTY(bool canRedo READ canRedo NOTIFY commandStateChanged)
    Q_PROPERTY(QString undoText READ undoText NOTIFY commandStateChanged)
    Q_PROPERTY(QString redoText READ redoText NOTIFY commandStateChanged)
    Q_PROPERTY(yoake::models::SubtitleModel *lines READ lines CONSTANT)
    Q_PROPERTY(yoake::media::MediaSession *media READ media CONSTANT)
    Q_PROPERTY(yoake::timing::KaraokeSession *karaoke READ karaoke CONSTANT)
    Q_PROPERTY(QString activeText READ activeText WRITE setActiveText NOTIFY activeLineChanged)
    Q_PROPERTY(qint64 activeStartMs READ activeStartMs WRITE setActiveStartMs NOTIFY activeLineChanged)
    Q_PROPERTY(qint64 activeEndMs READ activeEndMs WRITE setActiveEndMs NOTIFY activeLineChanged)
    Q_PROPERTY(QString activeStyle READ activeStyle WRITE setActiveStyle NOTIFY activeLineChanged)
    Q_PROPERTY(QString activeActor READ activeActor WRITE setActiveActor NOTIFY activeLineChanged)
    Q_PROPERTY(QString activeEffect READ activeEffect WRITE setActiveEffect NOTIFY activeLineChanged)
    Q_PROPERTY(int activeLayer READ activeLayer WRITE setActiveLayer NOTIFY activeLineChanged)
    Q_PROPERTY(bool activeComment READ activeComment WRITE setActiveComment NOTIFY activeLineChanged)

public:
    explicit DocumentContext(ass::Document document, QUrl fileUrl = {}, QObject *parent = nullptr);
    ~DocumentContext() override;

    [[nodiscard]] QString title() const;
    [[nodiscard]] QUrl fileUrl() const { return m_fileUrl; }
    [[nodiscard]] bool modified() const {
        return m_currentStateId != m_savedStateId || (m_karaoke && m_karaoke->dirty());
    }
    [[nodiscard]] bool saving() const { return m_saving; }
    [[nodiscard]] bool canUndo() const { return m_undo.canUndo(); }
    [[nodiscard]] bool canRedo() const { return m_undo.canRedo(); }
    [[nodiscard]] QString undoText() const { return m_undo.undoText(); }
    [[nodiscard]] QString redoText() const { return m_undo.redoText(); }
    [[nodiscard]] models::SubtitleModel *lines() const { return m_lines; }
    [[nodiscard]] media::MediaSession *media() const { return m_media; }
    [[nodiscard]] timing::KaraokeSession *karaoke() const { return m_karaoke; }

    [[nodiscard]] QString activeText() const;
    [[nodiscard]] qint64 activeStartMs() const;
    [[nodiscard]] qint64 activeEndMs() const;
    [[nodiscard]] QString activeStyle() const;
    [[nodiscard]] QString activeActor() const;
    [[nodiscard]] QString activeEffect() const;
    [[nodiscard]] int activeLayer() const;
    [[nodiscard]] bool activeComment() const;

    void setActiveText(const QString &value);
    void setActiveStartMs(qint64 value);
    void setActiveEndMs(qint64 value);
    void setActiveStyle(const QString &value);
    void setActiveActor(const QString &value);
    void setActiveEffect(const QString &value);
    void setActiveLayer(int value);
    void setActiveComment(bool value);

    Q_INVOKABLE void undo();
    Q_INVOKABLE void redo();
    Q_INVOKABLE void insertAfterActive();
    Q_INVOKABLE void duplicateSelected();
    Q_INVOKABLE void deleteSelected();
    Q_INVOKABLE void toggleSelectedComments();
    Q_INVOKABLE void setSelectedTiming(qint64 startMs, qint64 endMs);
    Q_INVOKABLE void save(const QUrl &target = {});

    void editEvent(const QUuid &id, int role, const QVariant &value);
    void commitKaraokeText(const QString &value);
    [[nodiscard]] QByteArray rendererSnapshot() const { return m_document.serialize(); }
    [[nodiscard]] std::shared_ptr<renderer::MangetsuSession> rendererSession() const { return m_renderer; }

signals:
    void titleChanged();
    void fileUrlChanged();
    void modifiedChanged();
    void savingChanged();
    void commandStateChanged();
    void activeLineChanged();
    void rendererRevisionChanged(quint64 revision);
    void savePathRequired();
    void saveFinished(bool success, const QString &error);

private:
    friend class EditEventCommand;
    friend class ReplaceEventsCommand;

    const ass::Event *activeEvent() const;
    void replaceEvents(QVector<ass::Event> events,
        const QString &description,
        const QUuid &preferredActive = {});
    void transitionToState(quint64 stateId);
    quint64 allocateStateId() { return m_nextStateId++; }

    ass::Document m_document;
    QUrl m_fileUrl;
    models::SubtitleModel *m_lines = nullptr;
    media::MediaSession *m_media = nullptr;
    timing::KaraokeSession *m_karaoke = nullptr;
    std::shared_ptr<renderer::MangetsuSession> m_renderer;
    QUndoStack m_undo;
    quint64 m_nextStateId = 1;
    quint64 m_currentStateId = 0;
    quint64 m_savedStateId = 0;
    quint64 m_rendererRevision = 0;
    quint64 m_mergeEpoch = 0;
    bool m_saving = false;
};

} // namespace yoake::app
