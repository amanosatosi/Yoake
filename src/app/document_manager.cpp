#include "app/document_manager.h"

#include "app/document_context.h"
#include "ass/ass_document.h"

#include <QtConcurrent/QtConcurrentRun>
#include <QtCore/QFile>
#include <QtCore/QFutureWatcher>
#include <QtCore/QMetaObject>

#include <algorithm>

namespace yoake::app {
namespace {

struct OpenResult {
    ass::Document document;
    QString error;
};

OpenResult readDocument(const QString &path)
{
    QFile file(path);
    if (!file.open(QIODevice::ReadOnly))
        return {{}, file.errorString()};
    QString error;
    ass::Document document = ass::Document::parse(file.readAll(), &error);
    return {std::move(document), std::move(error)};
}

} // namespace

DocumentManager::DocumentManager(QObject *parent) : QAbstractListModel(parent)
{
    createDocument();
}

int DocumentManager::rowCount(const QModelIndex &parent) const
{
    return parent.isValid() ? 0 : m_documents.size();
}

QVariant DocumentManager::data(const QModelIndex &index, int role) const
{
    DocumentContext *document = documentAt(index.row());
    if (!document)
        return {};
    switch (role) {
    case TitleRole: return document->title();
    case ModifiedRole: return document->modified();
    case ContextRole: return QVariant::fromValue(document);
    case FileUrlRole: return document->fileUrl();
    default: return {};
    }
}

QHash<int, QByteArray> DocumentManager::roleNames() const
{
    return {{TitleRole, "documentTitle"}, {ModifiedRole, "documentModified"},
        {ContextRole, "documentContext"}, {FileUrlRole, "documentFileUrl"}};
}

DocumentContext *DocumentManager::currentDocument() const
{
    return documentAt(m_currentIndex);
}

void DocumentManager::setCurrentIndex(int index)
{
    if (m_documents.isEmpty())
        index = -1;
    else
        index = qBound(0, index, static_cast<int>(m_documents.size()) - 1);
    if (m_currentIndex == index)
        return;
    m_currentIndex = index;
    emit currentIndexChanged();
    emit currentDocumentChanged();
}

DocumentContext *DocumentManager::documentAt(int index) const
{
    return index >= 0 && index < m_documents.size() ? m_documents[index] : nullptr;
}

void DocumentManager::createDocument()
{
    addDocument(new DocumentContext(ass::Document::createDefault(), {}, this));
}

void DocumentManager::openDocument(const QUrl &source)
{
    if (!source.isLocalFile()) {
        emit operationFailed(tr("Yoake can currently open local subtitle files only."));
        return;
    }
    ++m_pendingLoads;
    emit loadingChanged();
    auto *watcher = new QFutureWatcher<OpenResult>(this);
    connect(watcher, &QFutureWatcher<OpenResult>::finished, this, [this, watcher, source] {
        const OpenResult result = watcher->result();
        watcher->deleteLater();
        --m_pendingLoads;
        emit loadingChanged();
        if (!result.error.isEmpty()) {
            emit operationFailed(tr("Could not open %1: %2").arg(source.toLocalFile(), result.error));
            return;
        }
        addDocument(new DocumentContext(result.document, source, this));
    });
    watcher->setFuture(QtConcurrent::run(readDocument, source.toLocalFile()));
}

void DocumentManager::saveCurrent(const QUrl &target)
{
    saveDocument(currentDocument(), target, false);
}

void DocumentManager::save(DocumentContext *document, const QUrl &target)
{
    saveDocument(document, target, false);
}

void DocumentManager::requestClose(DocumentContext *document)
{
    if (indexOf(document) < 0)
        return;
    if (document->modified())
        emit closeConfirmationRequested(document, document->title());
    else
        closeNow(indexOf(document));
}

void DocumentManager::discardAndClose(DocumentContext *document)
{
    if (document == m_closePromptDocument)
        m_closePromptDocument = nullptr;
    closeNow(indexOf(document));
}

void DocumentManager::saveAndClose(DocumentContext *document, const QUrl &target)
{
    if (document == m_closePromptDocument)
        m_closePromptDocument = nullptr;
    saveDocument(document, target, true);
}

bool DocumentManager::requestApplicationClose()
{
    if (m_applicationCloseReady)
        return true;
    const bool hasModifiedDocument = std::any_of(m_documents.cbegin(), m_documents.cend(),
        [](const DocumentContext *document) { return document->modified(); });
    if (!hasModifiedDocument)
        return true;
    m_applicationCloseRequested = true;
    continueApplicationClose();
    return false;
}

void DocumentManager::cancelApplicationClose()
{
    m_applicationCloseRequested = false;
    m_closePromptDocument = nullptr;
}

void DocumentManager::addDocument(DocumentContext *document)
{
    const int row = m_documents.size();
    beginInsertRows({}, row, row);
    m_documents.push_back(document);
    endInsertRows();

    const auto update = [this, document] {
        if (m_applicationCloseReady && document->modified())
            m_applicationCloseReady = false;
        const int changedRow = indexOf(document);
        if (changedRow >= 0) {
            const QModelIndex changed = index(changedRow);
            emit dataChanged(changed, changed, {TitleRole, ModifiedRole, FileUrlRole});
        }
    };
    connect(document, &DocumentContext::titleChanged, this, update);
    connect(document, &DocumentContext::modifiedChanged, this, update);
    connect(document, &DocumentContext::fileUrlChanged, this, update);
    connect(document, &DocumentContext::saveFinished, this,
        [this, document](bool success, const QString &error) {
            if (!success) {
                m_closeAfterSave.remove(document);
                cancelApplicationClose();
                emit operationFailed(tr("Could not save %1: %2").arg(document->title(), error));
                return;
            }
            const auto pending = m_closeAfterSave.constFind(document);
            if (pending == m_closeAfterSave.cend())
                return;
            const QUrl target = pending.value();
            if (document->modified() || document->fileUrl() != target) {
                document->save(target);
                return;
            }
            m_closeAfterSave.remove(document);
            closeNow(indexOf(document));
        });
    setCurrentIndex(row);
}

int DocumentManager::indexOf(const DocumentContext *document) const
{
    return m_documents.indexOf(const_cast<DocumentContext *>(document));
}

void DocumentManager::closeNow(int index)
{
    if (index < 0 || index >= m_documents.size())
        return;
    DocumentContext *previousCurrent = currentDocument();
    beginRemoveRows({}, index, index);
    DocumentContext *document = m_documents.takeAt(index);
    endRemoveRows();
    m_closeAfterSave.remove(document);
    if (m_closePromptDocument == document)
        m_closePromptDocument = nullptr;
    document->deleteLater();

    if (m_documents.isEmpty()) {
        m_currentIndex = -1;
        emit currentIndexChanged();
        emit currentDocumentChanged();
        createDocument();
    } else {
        const int next = previousCurrent == document
            ? std::min(index, static_cast<int>(m_documents.size()) - 1)
            : indexOf(previousCurrent);
        if (m_currentIndex != next) {
            m_currentIndex = next;
            emit currentIndexChanged();
            emit currentDocumentChanged();
        }
    }
    scheduleApplicationCloseContinuation();
}

void DocumentManager::saveDocument(DocumentContext *document, const QUrl &target, bool closeAfterSave)
{
    if (indexOf(document) < 0)
        return;
    const QUrl destination = target.isEmpty() ? document->fileUrl() : target;
    if (!destination.isLocalFile()) {
        emit savePathRequested(document, closeAfterSave);
        return;
    }
    if (closeAfterSave)
        m_closeAfterSave.insert(document, destination);
    if (!document->saving())
        document->save(destination);
}

void DocumentManager::continueApplicationClose()
{
    if (!m_applicationCloseRequested || m_closePromptDocument)
        return;
    for (DocumentContext *document : m_documents) {
        if (document->modified()) {
            if (m_closeAfterSave.contains(document))
                return;
            m_closePromptDocument = document;
            emit closeConfirmationRequested(document, document->title());
            return;
        }
    }
    m_applicationCloseRequested = false;
    m_applicationCloseReady = true;
    emit applicationCloseReady();
}

void DocumentManager::scheduleApplicationCloseContinuation()
{
    if (!m_applicationCloseRequested)
        return;
    QMetaObject::invokeMethod(this, &DocumentManager::continueApplicationClose, Qt::QueuedConnection);
}

} // namespace yoake::app
