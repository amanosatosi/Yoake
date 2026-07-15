#pragma once

#include <QtCore/QByteArray>
#include <QtCore/QObject>
#include <QtCore/QPointF>
#include <QtCore/QVector>

#include <atomic>

struct FFMS_AudioSource;
struct FFMS_Index;
struct SwrContext;

namespace yoake::media {

enum class PcmSampleFormat : int {
    UInt8,
    Int16,
    Int32,
    Float32,
};

class FfmsAudioWorker final : public QObject {
    Q_OBJECT

public:
    explicit FfmsAudioWorker(QObject *parent = nullptr);
    ~FfmsAudioWorker() override;

    void invalidate(quint64 generation) noexcept;

public slots:
    void open(quint64 generation,
        const QString &sourcePath,
        const QString &indexPath,
        int audioTrack,
        bool generateWaveform);
    void requestPcm(quint64 generation,
        quint64 playbackId,
        qint64 startSample,
        qint64 endSample,
        int maximumFrames,
        int outputSampleRate,
        int outputChannels,
        PcmSampleFormat outputSampleFormat);
    void close(quint64 generation);
    void shutdown();

signals:
    void opened(quint64 generation,
        int sampleRate,
        int channels,
        qint64 totalSamples,
        qint64 firstTimeMs,
        qint64 durationMs,
        int sourceSampleRate,
        int sourceChannels);
    void waveformChunk(quint64 generation,
        qint64 startBucket,
        const QVector<QPointF> &peaks,
        int samplesPerPeak,
        int sampleRate,
        qint64 firstTimeMs,
        bool complete);
    void pcmReady(quint64 generation,
        quint64 playbackId,
        qint64 startSample,
        int frameCount,
        const QByteArray &pcm,
        bool finished);
    void pcmFailed(quint64 generation, quint64 playbackId, const QString &message);
    void failed(quint64 generation, const QString &message);

private slots:
    void processWaveformChunk();

private:
    void clearSource();
    void clearResampler();
    [[nodiscard]] bool stillWanted(quint64 generation) const noexcept;

    FFMS_Index *m_index = nullptr;
    FFMS_AudioSource *m_audio = nullptr;
    SwrContext *m_resampler = nullptr;
    quint64 m_generation = 0;
    qint64 m_totalSamples = 0;
    qint64 m_firstTimeMs = 0;
    qint64 m_waveformCursor = 0;
    qint64 m_waveformBucket = 0;
    int m_sampleRate = 0;
    int m_channels = 2;
    int m_outputSampleRate = 0;
    int m_outputChannels = 0;
    PcmSampleFormat m_outputSampleFormat = PcmSampleFormat::Int16;
    quint64 m_resamplerPlaybackId = 0;
    qint64 m_resamplerNextSample = -1;
    bool m_waveformScheduled = false;
    bool m_generateWaveform = false;
    std::atomic<quint64> m_wantedGeneration{0};
};

} // namespace yoake::media
