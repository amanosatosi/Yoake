#pragma once

#include "app/document_context.h"

#include <QtCore/QAbstractListModel>
#include <QtCore/QHash>
#include <QtCore/QUrl>

namespace yoake::app {

class DocumentManager final : public QAbstractListModel {
    Q_OBJECT
    Q_PROPERTY(int currentIndex READ currentIndex WRITE setCurrentIndex NOTIFY currentIndexChanged)
    Q_PROPERTY(yoake::app::DocumentContext *currentDocument READ currentDocument NOTIFY currentDocumentChanged)
    Q_PROPERTY(bool loading READ loading NOTIFY loadingChanged)

public:
    enum Role { TitleRole = Qt::UserRole + 1, ModifiedRole, ContextRole, FileUrlRole };
    Q_ENUM(Role)

    explicit DocumentManager(QObject *parent = nullptr);

    int rowCount(const QModelIndex &parent = {}) const override;
    QVariant data(const QModelIndex &index, int role) const override;
    QHash<int, QByteArray> roleNames() const override;

    [[nodiscard]] int currentIndex() const { return m_currentIndex; }
    [[nodiscard]] DocumentContext *currentDocument() const;
    [[nodiscard]] bool loading() const { return m_pendingLoads > 0; }
    void setCurrentIndex(int index);

    Q_INVOKABLE yoake::app::DocumentContext *documentAt(int index) const;
    Q_INVOKABLE void createDocument();
    Q_INVOKABLE void openDocument(const QUrl &source);
    Q_INVOKABLE void saveCurrent(const QUrl &target = {});
    Q_INVOKABLE void save(yoake::app::DocumentContext *document, const QUrl &target);
    Q_INVOKABLE void requestClose(yoake::app::DocumentContext *document);
    Q_INVOKABLE void discardAndClose(yoake::app::DocumentContext *document);
    Q_INVOKABLE void saveAndClose(yoake::app::DocumentContext *document, const QUrl &target = {});
    Q_INVOKABLE bool requestApplicationClose();
    Q_INVOKABLE void cancelApplicationClose();

signals:
    void currentIndexChanged();
    void currentDocumentChanged();
    void loadingChanged();
    void closeConfirmationRequested(yoake::app::DocumentContext *document, const QString &title);
    void savePathRequested(yoake::app::DocumentContext *document, bool closeAfterSave);
    void operationFailed(const QString &message);
    void applicationCloseReady();

private:
    void addDocument(DocumentContext *document);
    int indexOf(const DocumentContext *document) const;
    void closeNow(int index);
    void saveDocument(DocumentContext *document, const QUrl &target, bool closeAfterSave);
    void continueApplicationClose();
    void scheduleApplicationCloseContinuation();

    QVector<DocumentContext *> m_documents;
    QHash<DocumentContext *, QUrl> m_closeAfterSave;
    DocumentContext *m_closePromptDocument = nullptr;
    int m_currentIndex = -1;
    int m_pendingLoads = 0;
    bool m_applicationCloseRequested = false;
    bool m_applicationCloseReady = false;
};

} // namespace yoake::app
