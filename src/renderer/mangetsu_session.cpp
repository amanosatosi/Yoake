#include "renderer/mangetsu_session.h"

#include "renderer/mangetsu_library.h"

#include <QtCore/QDebug>

#include <algorithm>
#include <cstdio>
#include <cmath>
#include <utility>

namespace yoake::renderer {
namespace {

void messageCallback(int level, const char *format, std::va_list arguments, void *)
{
    if (level >= 5)
        return;
    char message[2048]{};
    std::vsnprintf(message, sizeof(message), format, arguments);
    qWarning().noquote() << "Mangetsu:" << QString::fromUtf8(message).trimmed();
}

inline int red(std::uint32_t color) { return static_cast<int>(color >> 24); }
inline int green(std::uint32_t color) { return static_cast<int>((color >> 16) & 0xffU); }
inline int blue(std::uint32_t color) { return static_cast<int>((color >> 8) & 0xffU); }
inline int alpha(std::uint32_t color) { return static_cast<int>(color & 0xffU); }

QRgb compositePremultiplied(QRgb destination, int sourceRed, int sourceGreen,
    int sourceBlue, int sourceAlpha)
{
    const int inverse = 255 - sourceAlpha;
    return qRgba(
        std::min(255, sourceRed + qRed(destination) * inverse / 255),
        std::min(255, sourceGreen + qGreen(destination) * inverse / 255),
        std::min(255, sourceBlue + qBlue(destination) * inverse / 255),
        std::min(255, sourceAlpha + qAlpha(destination) * inverse / 255));
}

} // namespace

class MangetsuRenderWorker final : public QObject {
    Q_OBJECT

public slots:
    void enqueue(quint64 clientId, quint64 requestId, const QByteArray &script,
        const QSize &frameSize, qint64 timeMs)
    {
        m_pending = {clientId, requestId, script, frameSize, timeMs};
        if (!m_renderScheduled) {
            m_renderScheduled = true;
            QMetaObject::invokeMethod(this, &MangetsuRenderWorker::renderPending, Qt::QueuedConnection);
        }
    }

    void renderPending()
    {
        m_renderScheduled = false;
        const Pending request = std::exchange(m_pending, {});
        QString error;
        QImage image;
        if (request.frameSize.width() <= 0 || request.frameSize.height() <= 0) {
            error = QStringLiteral("Invalid subtitle preview size");
        } else if (ensureInitialized(&error) && loadTrack(request.script, &error)) {
            image = renderTrack(request.frameSize, request.timeMs, &error);
        }
        emit rendered(request.clientId, request.requestId, image, error);
    }

    void shutdown()
    {
        if (!m_api)
            return;
        if (m_track) {
            m_api->freeTrack(m_track);
            m_track = nullptr;
        }
        if (m_renderer) {
            m_api->rendererDone(m_renderer);
            m_renderer = nullptr;
        }
        if (m_library) {
            m_api->libraryDone(m_library);
            m_library = nullptr;
        }
        m_api = nullptr;
    }

signals:
    void rendered(quint64 clientId, quint64 requestId, const QImage &image, const QString &error);

private:
    struct Pending {
        quint64 clientId = 0;
        quint64 requestId = 0;
        QByteArray script;
        QSize frameSize;
        qint64 timeMs = 0;
    };

    bool ensureInitialized(QString *error)
    {
        if (m_renderer)
            return true;
        m_api = MangetsuLibrary::instance().api(error);
        if (!m_api)
            return false;
        m_library = m_api->libraryInit();
        if (!m_library) {
            *error = QStringLiteral("Mangetsu failed to create a document library");
            return false;
        }
        m_api->setMessageCallback(m_library, messageCallback, nullptr);
        m_renderer = m_api->rendererInit(m_library);
        if (!m_renderer) {
            *error = QStringLiteral("Mangetsu failed to create a document renderer");
            shutdown();
            return false;
        }
        m_api->setFontScale(m_renderer, 1.0);
        m_api->setFonts(m_renderer, nullptr, "Sans", 1, nullptr, 1);
        return true;
    }

    bool loadTrack(const QByteArray &script, QString *error)
    {
        if (script == m_script && m_track)
            return true;
        if (m_track) {
            m_api->freeTrack(m_track);
            m_track = nullptr;
        }
        m_script = script;
        QByteArray mutableScript = script;
        m_track = m_api->readMemory(m_library, mutableScript.data(),
            static_cast<std::size_t>(mutableScript.size()), nullptr);
        if (!m_track) {
            *error = QStringLiteral("Mangetsu rejected the subtitle document");
            return false;
        }
        return true;
    }

    QImage renderTrack(const QSize &size, qint64 timeMs, QString *error)
    {
        QImage output(size, QImage::Format_ARGB32_Premultiplied);
        output.fill(Qt::transparent);
        m_api->setFrameSize(m_renderer, size.width(), size.height());
        m_api->setStorageSize(m_renderer, size.width(), size.height());
        int changed = 0;
        const abi::RenderResult result = m_api->renderFrameAuto(m_renderer, m_track, timeMs, &changed);

        if (result.useRgba && result.rgbaImages) {
            for (abi::AssImageRgba *image = result.rgbaImages; image; image = image->next)
                blendRgba(output, *image);
        } else {
            for (abi::AssImage *image = result.images; image; image = image->next)
                blendMask(output, *image);
        }
        if (result.rgbaImages)
            m_api->freeImagesRgba(result.rgbaImages);
        error->clear();
        return output;
    }

    static void blendRgba(QImage &destination, const abi::AssImageRgba &source)
    {
        const int left = std::max(0, source.destinationX);
        const int top = std::max(0, source.destinationY);
        const int right = std::min(destination.width(), source.destinationX + source.width);
        const int bottom = std::min(destination.height(), source.destinationY + source.height);
        for (int y = top; y < bottom; ++y) {
            auto *target = reinterpret_cast<QRgb *>(destination.scanLine(y));
            const auto *pixels = source.rgba
                + (y - source.destinationY) * source.stride
                + (left - source.destinationX) * 4;
            for (int x = left; x < right; ++x, pixels += 4) {
                target[x] = compositePremultiplied(target[x], pixels[0], pixels[1], pixels[2], pixels[3]);
            }
        }
    }

    static void blendMask(QImage &destination, const abi::AssImage &source)
    {
        const int left = std::max(0, source.destinationX);
        const int top = std::max(0, source.destinationY);
        const int right = std::min(destination.width(), source.destinationX + source.width);
        const int bottom = std::min(destination.height(), source.destinationY + source.height);
        const int opacity = 255 - alpha(source.color);
        for (int y = top; y < bottom; ++y) {
            auto *target = reinterpret_cast<QRgb *>(destination.scanLine(y));
            const auto *mask = source.bitmap
                + (y - source.destinationY) * source.stride
                + (left - source.destinationX);
            for (int x = left; x < right; ++x, ++mask) {
                const int coverage = static_cast<int>(*mask) * opacity / 255;
                target[x] = compositePremultiplied(target[x],
                    red(source.color) * coverage / 255,
                    green(source.color) * coverage / 255,
                    blue(source.color) * coverage / 255,
                    coverage);
            }
        }
    }

    const abi::Api *m_api = nullptr;
    abi::AssLibrary *m_library = nullptr;
    abi::AssRenderer *m_renderer = nullptr;
    abi::AssTrack *m_track = nullptr;
    QByteArray m_script;
    Pending m_pending;
    bool m_renderScheduled = false;
};

MangetsuSession::MangetsuSession(QObject *parent)
    : QObject(parent)
{
    m_renderThread.setObjectName(QStringLiteral("Yoake Mangetsu session"));
}

MangetsuSession::~MangetsuSession()
{
    if (m_renderThread.isRunning()) {
        QMetaObject::invokeMethod(m_worker, &MangetsuRenderWorker::shutdown, Qt::BlockingQueuedConnection);
        m_renderThread.quit();
        m_renderThread.wait();
    }
    m_worker = nullptr;
}

void MangetsuSession::requestRender(quint64 clientId, quint64 requestId,
    QByteArray script, QSize frameSize, qint64 timeMs)
{
    if (!m_worker) {
        m_worker = new MangetsuRenderWorker;
        m_worker->moveToThread(&m_renderThread);
        connect(&m_renderThread, &QThread::finished, m_worker, &QObject::deleteLater);
        connect(this, &MangetsuSession::renderRequested,
            m_worker, &MangetsuRenderWorker::enqueue, Qt::QueuedConnection);
        connect(m_worker, &MangetsuRenderWorker::rendered,
            this, &MangetsuSession::renderReady, Qt::QueuedConnection);
        m_renderThread.start();
    }
    emit renderRequested(clientId, requestId, script, frameSize, timeMs);
}

} // namespace yoake::renderer

#include "mangetsu_session.moc"
