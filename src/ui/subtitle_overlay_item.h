#pragma once

#include "app/document_context.h"

#include <QtCore/QMetaObject>
#include <QtCore/QPointer>
#include <QtCore/QTimer>
#include <QtGui/QImage>
#include <QtQuick/QQuickItem>

namespace yoake::ui {

class SubtitleOverlayItem : public QQuickItem {
    Q_OBJECT
    Q_PROPERTY(yoake::app::DocumentContext *document READ document WRITE setDocument NOTIFY documentChanged)
    Q_PROPERTY(qint64 timeMs READ timeMs WRITE setTimeMs NOTIFY timeMsChanged)
    Q_PROPERTY(bool rendering READ rendering NOTIFY renderingChanged)
    Q_PROPERTY(QString errorString READ errorString NOTIFY errorStringChanged)

public:
    explicit SubtitleOverlayItem(QQuickItem *parent = nullptr);

    [[nodiscard]] app::DocumentContext *document() const { return m_document.data(); }
    [[nodiscard]] qint64 timeMs() const { return m_timeMs; }
    [[nodiscard]] bool rendering() const { return m_rendering; }
    [[nodiscard]] QString errorString() const { return m_errorString; }

    void setDocument(app::DocumentContext *document);
    void setTimeMs(qint64 timeMs);

signals:
    void documentChanged();
    void timeMsChanged();
    void renderingChanged();
    void errorStringChanged();

protected:
    void geometryChange(const QRectF &newGeometry, const QRectF &oldGeometry) override;
    QSGNode *updatePaintNode(QSGNode *oldNode, UpdatePaintNodeData *) override;

private:
    void scheduleRender();
    void renderNow();
    void clearPresentation();

    QPointer<app::DocumentContext> m_document;
    QByteArray m_cachedScript;
    qint64 m_timeMs = 0;
    QImage m_image;
    QTimer m_coalesceTimer;
    QMetaObject::Connection m_documentConnection;
    QMetaObject::Connection m_destroyedConnection;
    QMetaObject::Connection m_rendererConnection;
    quint64 m_clientId = 0;
    quint64 m_requestId = 0;
    bool m_rendering = false;
    bool m_textureDirty = true;
    QString m_errorString;
};

} // namespace yoake::ui
