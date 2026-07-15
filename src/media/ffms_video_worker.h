#pragma once

#include <QtCore/QObject>
#include <QtCore/QVariantList>
#include <QtCore/QVector>
#include <QtGui/QImage>

#include <atomic>

struct FFMS_Index;
struct FFMS_VideoSource;

namespace yoake::media {

class FfmsVideoWorker final : public QObject {
    Q_OBJECT

public:
    explicit FfmsVideoWorker(QObject *parent = nullptr);
    ~FfmsVideoWorker() override;

    // These only touch atomics and are intentionally safe to call from the GUI thread.
    void invalidate(quint64 generation, quint64 cancellationId) noexcept;

public slots:
    void open(quint64 generation, const QString &path, int preferredVideoTrack, int preferredAudioTrack);
    void requestFrame(
        quint64 generation, quint64 requestId, quint64 cancellationId, int frameNumber);
    void close(quint64 generation);
    void shutdown();

signals:
    void indexingProgress(quint64 generation, double progress);
    void opened(quint64 generation,
        const QString &indexPath,
        const QVariantList &videoTracks,
        const QVariantList &audioTracks,
        int videoTrack,
        int audioTrack,
        const QVector<qint64> &frameStartsMs,
        qint64 durationMs,
        int width,
        int height,
        int sourcePixelFormat,
        double sourceFrameRate);
    void frameReady(quint64 generation,
        quint64 requestId,
        quint64 cancellationId,
        int frameNumber,
        qint64 startMs,
        qint64 endMs,
        const QImage &image);
    void failed(quint64 generation, const QString &message);

private slots:
    void processPendingFrame();

private:
    struct PendingFrame {
        quint64 generation = 0;
        quint64 requestId = 0;
        quint64 cancellationId = 0;
        int frameNumber = 0;
        bool valid = false;
    };

    void clearSource();
    [[nodiscard]] bool stillWanted(quint64 generation) const noexcept;

    FFMS_Index *m_index = nullptr;
    FFMS_VideoSource *m_video = nullptr;
    QVector<qint64> m_frameStartsMs;
    qint64 m_durationMs = 0;
    int m_width = 0;
    int m_height = 0;
    int m_rotation = 0;
    int m_flip = 0;
    quint64 m_generation = 0;
    PendingFrame m_pendingFrame;
    bool m_frameScheduled = false;
    std::atomic<quint64> m_wantedGeneration{0};
    std::atomic<quint64> m_latestCancellation{0};
};

} // namespace yoake::media
