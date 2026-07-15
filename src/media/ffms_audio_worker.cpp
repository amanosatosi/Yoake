#include "media/ffms_audio_worker.h"

#include <ffms.h>

#include <QtCore/QDir>
#include <QtCore/QFileInfo>
#include <QtCore/QTimer>

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <limits>
#include <mutex>

namespace yoake::media {
namespace {

constexpr int outputSampleRate = 48000;
constexpr int outputChannels = 2;
constexpr int bytesPerOutputFrame = outputChannels * static_cast<int>(sizeof(qint16));
constexpr int samplesPerWaveformPeak = 256;
constexpr int waveformFramesPerChunk = 64 * 1024;

struct ErrorBuffer {
    char text[2048]{};
    FFMS_ErrorInfo info{FFMS_ERROR_SUCCESS, FFMS_ERROR_SUCCESS, static_cast<int>(sizeof(text)), text};

    [[nodiscard]] QString message(const QString &fallback) const
    {
        return text[0] ? QString::fromUtf8(text) : fallback;
    }
};

void initializeFfms()
{
    static std::once_flag once;
    std::call_once(once, [] { FFMS_Init(0, 0); });
}

QByteArray nativePath(const QString &path)
{
    return QDir::toNativeSeparators(path).toUtf8();
}

} // namespace

FfmsAudioWorker::FfmsAudioWorker(QObject *parent) : QObject(parent)
{
    initializeFfms();
}

FfmsAudioWorker::~FfmsAudioWorker()
{
    clearSource();
}

void FfmsAudioWorker::invalidate(quint64 generation) noexcept
{
    m_wantedGeneration.store(generation, std::memory_order_release);
}

bool FfmsAudioWorker::stillWanted(quint64 generation) const noexcept
{
    return m_wantedGeneration.load(std::memory_order_acquire) == generation;
}

void FfmsAudioWorker::clearSource()
{
    m_waveformScheduled = false;
    if (m_audio) {
        FFMS_DestroyAudioSource(m_audio);
        m_audio = nullptr;
    }
    if (m_index) {
        FFMS_DestroyIndex(m_index);
        m_index = nullptr;
    }
    m_totalSamples = 0;
    m_firstTimeMs = 0;
    m_waveformCursor = 0;
    m_waveformBucket = 0;
    m_generateWaveform = false;
}

void FfmsAudioWorker::open(
    quint64 generation,
    const QString &sourcePath,
    const QString &indexPath,
    int audioTrack,
    bool generateWaveform)
{
    clearSource();
    m_generation = generation;
    m_generateWaveform = generateWaveform;
    if (!stillWanted(generation) || audioTrack < 0)
        return;

    ErrorBuffer error;
    const QByteArray encodedSource = nativePath(QFileInfo(sourcePath).absoluteFilePath());
    const QByteArray encodedIndex = nativePath(indexPath);
    m_index = FFMS_ReadIndex(encodedIndex.constData(), &error.info);
    if (!m_index || FFMS_IndexBelongsToFile(m_index, encodedSource.constData(), &error.info) != 0) {
        emit failed(generation, error.message(QStringLiteral("The FFMS2 audio index is missing or stale")));
        clearSource();
        return;
    }

    m_audio = FFMS_CreateAudioSource2(encodedSource.constData(), audioTrack, m_index,
        FFMS_DELAY_TIME_ZERO, FFMS_GAP_FILL_ENABLED, 1.0, &error.info);
    if (!m_audio) {
        emit failed(generation, error.message(QStringLiteral("FFMS2 could not create the audio source")));
        clearSource();
        return;
    }

    const FFMS_AudioProperties *sourceProperties = FFMS_GetAudioProperties(m_audio);
    const int sourceSampleRate = sourceProperties ? sourceProperties->SampleRate : 0;
    const int sourceChannels = sourceProperties ? sourceProperties->Channels : 0;
    FFMS_ResampleOptions *options = FFMS_CreateResampleOptions(m_audio);
    if (!options) {
        emit failed(generation, QStringLiteral("FFMS2 could not create audio resampling options"));
        clearSource();
        return;
    }
    options->SampleFormat = FFMS_FMT_S16;
    options->SampleRate = outputSampleRate;
    options->ChannelLayout = FFMS_CH_FRONT_LEFT | FFMS_CH_FRONT_RIGHT;
    options->ForceResample = 1;
    const int formatResult = FFMS_SetOutputFormatA(m_audio, options, &error.info);
    FFMS_DestroyResampleOptions(options);
    if (formatResult != 0) {
        emit failed(generation, error.message(QStringLiteral("FFMS2 could not produce 48 kHz stereo PCM")));
        clearSource();
        return;
    }

    const FFMS_AudioProperties *properties = FFMS_GetAudioProperties(m_audio);
    if (!properties) {
        emit failed(generation, QStringLiteral("FFMS2 did not expose audio timing information"));
        clearSource();
        return;
    }
    m_sampleRate = properties->SampleRate;
    m_channels = properties->Channels;
    m_totalSamples = properties->NumSamples;
    m_firstTimeMs = static_cast<qint64>(std::llround(properties->FirstTime * 1000.0));
    const qint64 durationMs = static_cast<qint64>(std::llround(properties->LastEndTime * 1000.0));
    emit opened(generation, m_sampleRate, m_channels, m_totalSamples, m_firstTimeMs,
        durationMs, sourceSampleRate, sourceChannels);

    if (m_generateWaveform) {
        m_waveformScheduled = true;
        QTimer::singleShot(0, this, &FfmsAudioWorker::processWaveformChunk);
    }
}

void FfmsAudioWorker::processWaveformChunk()
{
    m_waveformScheduled = false;
    if (!m_audio || !m_generateWaveform || !stillWanted(m_generation))
        return;
    if (m_waveformCursor >= m_totalSamples) {
        emit waveformChunk(m_generation, m_waveformBucket, {}, samplesPerWaveformPeak,
            m_sampleRate, m_firstTimeMs, true);
        return;
    }

    const int frameCount = static_cast<int>(std::min<qint64>(
        waveformFramesPerChunk, m_totalSamples - m_waveformCursor));
    QByteArray pcm;
    pcm.resize(static_cast<qsizetype>(frameCount) * bytesPerOutputFrame);
    ErrorBuffer error;
    if (FFMS_GetAudio(m_audio, pcm.data(), m_waveformCursor, frameCount, &error.info) != 0) {
        emit failed(m_generation, error.message(QStringLiteral("FFMS2 waveform decoding failed")));
        return;
    }

    QVector<QPointF> peaks;
    peaks.reserve((frameCount + samplesPerWaveformPeak - 1) / samplesPerWaveformPeak);
    const auto *samples = reinterpret_cast<const qint16 *>(pcm.constData());
    for (int firstFrame = 0; firstFrame < frameCount; firstFrame += samplesPerWaveformPeak) {
        const int endFrame = std::min(frameCount, firstFrame + samplesPerWaveformPeak);
        qint16 minimum = std::numeric_limits<qint16>::max();
        qint16 maximum = std::numeric_limits<qint16>::min();
        for (int frame = firstFrame; frame < endFrame; ++frame) {
            for (int channel = 0; channel < outputChannels; ++channel) {
                const qint16 sample = samples[frame * outputChannels + channel];
                minimum = std::min(minimum, sample);
                maximum = std::max(maximum, sample);
            }
        }
        peaks.push_back(QPointF(
            static_cast<double>(minimum) / 32768.0,
            static_cast<double>(maximum) / 32768.0));
    }

    const qint64 startBucket = m_waveformBucket;
    m_waveformBucket += peaks.size();
    m_waveformCursor += frameCount;
    const bool complete = m_waveformCursor >= m_totalSamples;
    emit waveformChunk(m_generation, startBucket, peaks, samplesPerWaveformPeak,
        m_sampleRate, m_firstTimeMs, complete);
    if (!complete && stillWanted(m_generation)) {
        m_waveformScheduled = true;
        QTimer::singleShot(0, this, &FfmsAudioWorker::processWaveformChunk);
    }
}

void FfmsAudioWorker::requestPcm(quint64 generation,
    quint64 playbackId,
    qint64 startSample,
    qint64 endSample,
    int maximumFrames)
{
    if (!m_audio || generation != m_generation || !stillWanted(generation))
        return;
    const qint64 boundedStart = std::clamp<qint64>(startSample, 0, m_totalSamples);
    const qint64 boundedEnd = std::clamp<qint64>(endSample, boundedStart, m_totalSamples);
    const int frameCount = static_cast<int>(std::min<qint64>(
        std::max(0, maximumFrames), boundedEnd - boundedStart));
    if (frameCount <= 0) {
        emit pcmReady(generation, playbackId, boundedStart, 0, {}, true);
        return;
    }

    QByteArray pcm;
    pcm.resize(static_cast<qsizetype>(frameCount) * bytesPerOutputFrame);
    ErrorBuffer error;
    if (FFMS_GetAudio(m_audio, pcm.data(), boundedStart, frameCount, &error.info) != 0) {
        emit failed(generation, error.message(QStringLiteral("FFMS2 range playback decoding failed")));
        return;
    }
    emit pcmReady(generation, playbackId, boundedStart, frameCount, pcm,
        boundedStart + frameCount >= boundedEnd);
}

void FfmsAudioWorker::close(quint64 generation)
{
    if (generation != m_wantedGeneration.load(std::memory_order_acquire))
        return;
    m_generation = generation;
    clearSource();
}

void FfmsAudioWorker::shutdown()
{
    clearSource();
}

} // namespace yoake::media
