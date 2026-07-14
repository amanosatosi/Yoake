#pragma once

#include <QtCore/QByteArray>
#include <QtCore/QObject>
#include <QtCore/QSize>
#include <QtCore/QThread>
#include <QtGui/QImage>

namespace yoake::renderer {

class MangetsuRenderWorker;

class MangetsuSession final : public QObject {
    Q_OBJECT

public:
    explicit MangetsuSession(QObject *parent = nullptr);
    ~MangetsuSession() override;

    void requestRender(quint64 clientId, quint64 requestId, QByteArray script,
        QSize frameSize, qint64 timeMs);

signals:
    void renderRequested(quint64 clientId, quint64 requestId, const QByteArray &script,
        const QSize &frameSize, qint64 timeMs);
    void renderReady(quint64 clientId, quint64 requestId, const QImage &image,
        const QString &error);

private:
    QThread m_renderThread;
    MangetsuRenderWorker *m_worker = nullptr;
};

} // namespace yoake::renderer
