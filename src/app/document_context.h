#pragma once

#include "ass/ass_document.h"
#include "media/media_session.h"
#include "models/subtitle_model.h"
#include "timing/karaoke_session.h"

#include <QtCore/QObject>
#include <QtCore/QStringList>
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
    Q_PROPERTY(int activeMarginLeft READ activeMarginLeft WRITE setActiveMarginLeft NOTIFY activeLineChanged)
    Q_PROPERTY(int activeMarginRight READ activeMarginRight WRITE setActiveMarginRight NOTIFY activeLineChanged)
    Q_PROPERTY(int activeMarginVertical READ activeMarginVertical WRITE setActiveMarginVertical NOTIFY activeLineChanged)
    Q_PROPERTY(bool activeComment READ activeComment WRITE setActiveComment NOTIFY activeLineChanged)
    Q_PROPERTY(QString linkedVideoFile READ linkedVideoFile CONSTANT)
    Q_PROPERTY(QString linkedAudioFile READ linkedAudioFile CONSTANT)
    Q_PROPERTY(QString linkedMediaError READ linkedMediaError NOTIFY linkedMediaStatusChanged)
    Q_PROPERTY(QStringList styleNames READ styleNames CONSTANT)
    Q_PROPERTY(QStringList actorSuggestions READ actorSuggestions NOTIFY suggestionsChanged)
    Q_PROPERTY(QStringList effectSuggestions READ effectSuggestions NOTIFY suggestionsChanged)
    Q_PROPERTY(int findMatchStart READ findMatchStart NOTIFY findMatchChanged)
    Q_PROPERTY(int findMatchLength READ findMatchLength NOTIFY findMatchChanged)
    Q_PROPERTY(bool canPasteRows READ canPasteRows NOTIFY clipboardChanged)

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
    [[nodiscard]] int activeMarginLeft() const;
    [[nodiscard]] int activeMarginRight() const;
    [[nodiscard]] int activeMarginVertical() const;
    [[nodiscard]] bool activeComment() const;
    [[nodiscard]] QString linkedVideoFile() const { return m_document.projectProperties().videoFile; }
    [[nodiscard]] QString linkedAudioFile() const { return m_document.projectProperties().audioFile; }
    [[nodiscard]] QString linkedMediaError() const { return m_linkedMediaError; }
    [[nodiscard]] QStringList styleNames() const { return m_document.styleNames(); }
    [[nodiscard]] QStringList actorSuggestions() const;
    [[nodiscard]] QStringList effectSuggestions() const;
    [[nodiscard]] int findMatchStart() const { return m_findMatchStart; }
    [[nodiscard]] int findMatchLength() const { return m_findMatchLength; }

    void setActiveText(const QString &value);
    void setActiveStartMs(qint64 value);
    void setActiveEndMs(qint64 value);
    void setActiveStyle(const QString &value);
    void setActiveActor(const QString &value);
    void setActiveEffect(const QString &value);
    void setActiveLayer(int value);
    void setActiveMarginLeft(int value);
    void setActiveMarginRight(int value);
    void setActiveMarginVertical(int value);
    void setActiveComment(bool value);

    Q_INVOKABLE void undo();
    Q_INVOKABLE void redo();
    Q_INVOKABLE void insertBeforeActive();
    Q_INVOKABLE void insertAfterActive();
    Q_INVOKABLE void duplicateSelected();
    Q_INVOKABLE void deleteSelected();
    Q_INVOKABLE void copySelected();
    Q_INVOKABLE void cutSelected();
    Q_INVOKABLE void pasteRows();
    Q_INVOKABLE void toggleSelectedComments();
    Q_INVOKABLE void joinSelected();
    Q_INVOKABLE void splitActiveAtCursor(int utf16Position);
    Q_INVOKABLE void splitActiveAtCursorAtPosition(int utf16Position);
    Q_INVOKABLE void moveSelectedUp();
    Q_INVOKABLE void moveSelectedDown();
    Q_INVOKABLE void setSelectedTiming(qint64 startMs, qint64 endMs);
    Q_INVOKABLE void setSelectedStartToPosition();
    Q_INVOKABLE void setSelectedEndToPosition();
    Q_INVOKABLE void shiftSelectedTiming(qint64 deltaMs);
    Q_INVOKABLE bool findText(const QString &query, bool caseSensitive = false, bool backwards = false);
    Q_INVOKABLE bool replaceCurrent(const QString &query, const QString &replacement, bool caseSensitive = false);
    Q_INVOKABLE bool replaceNext(const QString &query, const QString &replacement, bool caseSensitive = false);
    Q_INVOKABLE int replaceAll(const QString &query, const QString &replacement, bool caseSensitive = false);
    bool canPasteRows() const;
    Q_INVOKABLE void loadLinkedMedia();
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
    void linkedMediaStatusChanged();
    void suggestionsChanged();
    void findMatchChanged();
    void clipboardChanged();
    void rendererRevisionChanged(quint64 revision);
    void savePathRequired();
    void saveFinished(bool success, const QString &error);

private:
    friend class EditEventCommand;
    friend class ReplaceEventsCommand;

    const ass::Event *activeEvent() const;
    void replaceEvents(QVector<ass::Event> events,
        const QString &description,
        const QUuid &preferredActive = {},
        const QVector<QUuid> &preferredSelection = {});
    void insertAtActive(bool before);
    bool findTextFrom(const QString &query, Qt::CaseSensitivity sensitivity, bool backwards);
    bool karaokeOwnsLine() const { return m_karaoke && m_karaoke->active(); }
    void transitionToState(quint64 stateId);
    [[nodiscard]] QString resolveLinkedPath(const QString &value) const;
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
    int m_pendingLinkedVideoFrame = -1;
    QString m_linkedMediaError;
    QString m_findQuery;
    QUuid m_findMatchId;
    int m_findMatchStart = -1;
    int m_findMatchLength = 0;
    bool m_saving = false;
};

} // namespace yoake::app
