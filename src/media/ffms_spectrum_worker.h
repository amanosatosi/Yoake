#pragma once

#include <QtCore/QObject>
#include <QtCore/QByteArray>
#include <QtGui/QColor>
#include <QtGui/QImage>

#include <atomic>
#include <memory>

struct FFMS_AudioSource;
struct FFMS_Index;

namespace yoake::media {

class FfmsSpectrumWorker final : public QObject {
    Q_OBJECT

public:
    explicit FfmsSpectrumWorker(QObject *parent = nullptr);
    ~FfmsSpectrumWorker() override;

    // Atomic-only invalidation is safe from the GUI thread and prevents an old
    // tab/source/viewport result from reaching presentation.
    void invalidate(quint64 generation, quint64 requestId) noexcept;

public slots:
    void open(quint64 generation,
        const QString &sourcePath,
        const QString &indexPath,
        int audioTrack);
    void renderViewport(quint64 generation,
        quint64 requestId,
        qint64 startMs,
        qint64 endMs,
        int width,
        int height,
        const QColor &lowColor,
        const QColor &midColor,
        const QColor &highColor);
    void close(quint64 generation);
    void shutdown();

signals:
    void spectrumReady(quint64 generation,
        quint64 requestId,
        const QImage &image,
        int cacheLevel);
    void failed(quint64 generation, quint64 requestId, const QString &message);

private:
    void clearSource();
    [[nodiscard]] bool stillWanted(quint64 generation, quint64 requestId) const noexcept;
    [[nodiscard]] QByteArray buildTile(
        quint64 requestId, int level, qint64 tileIndex, bool *ok);

    FFMS_Index *m_index = nullptr;
    FFMS_AudioSource *m_audio = nullptr;
    class TileCache;
    std::unique_ptr<TileCache> m_tiles;
    quint64 m_generation = 0;
    qint64 m_totalSamples = 0;
    qint64 m_firstTimeMs = 0;
    int m_sampleRate = 0;
    int m_channels = 2;
    std::atomic<quint64> m_wantedGeneration{0};
    std::atomic<quint64> m_latestRequest{0};
};

} // namespace yoake::media
