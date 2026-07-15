#include "media/ffms_audio_worker.h"

#include <ffms.h>

extern "C" {
#include <libavutil/channel_layout.h>
#include <libavutil/error.h>
#include <libavutil/samplefmt.h>
#include <libswresample/swresample.h>
}

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

constexpr int decodedOutputChannels = 2;
constexpr int bytesPerOutputFrame = decodedOutputChannels * static_cast<int>(sizeof(qint16));
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

AVSampleFormat avSampleFormat(PcmSampleFormat format)
{
    switch (format) {
    case PcmSampleFormat::UInt8:
        return AV_SAMPLE_FMT_U8;
    case PcmSampleFormat::Int16:
        return AV_SAMPLE_FMT_S16;
    case PcmSampleFormat::Int32:
        return AV_SAMPLE_FMT_S32;
    case PcmSampleFormat::Float32:
        return AV_SAMPLE_FMT_FLT;
    }
    return AV_SAMPLE_FMT_NONE;
}

QString ffmpegError(int code, const QString &fallback)
{
    char text[AV_ERROR_MAX_STRING_SIZE]{};
    return av_strerror(code, text, sizeof(text)) == 0
        ? QString::fromUtf8(text) : fallback;
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
    clearResampler();
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

void FfmsAudioWorker::clearResampler()
{
    if (m_resampler)
        swr_free(&m_resampler);
    m_outputSampleRate = 0;
    m_outputChannels = 0;
    m_outputSampleFormat = PcmSampleFormat::Int16;
    m_resamplerPlaybackId = 0;
    m_resamplerNextSample = -1;
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
    if (sourceSampleRate <= 0 || sourceChannels <= 0) {
        emit failed(generation, QStringLiteral("FFMS2 did not expose a valid source audio format"));
        clearSource();
        return;
    }
    FFMS_ResampleOptions *options = FFMS_CreateResampleOptions(m_audio);
    if (!options) {
        emit failed(generation, QStringLiteral("FFMS2 could not create audio resampling options"));
        clearSource();
        return;
    }
    // FFMS2 deliberately does not implement sample-rate conversion. Keep the
    // indexed source rate and only normalize the sample format/channel layout;
    // forcing 48 kHz here rejects ordinary 44.1 kHz tracks before any PCM can
    // reach the waveform, spectrum, or platform sink.
    options->SampleFormat = FFMS_FMT_S16;
    options->SampleRate = sourceSampleRate;
    options->ChannelLayout = FFMS_CH_FRONT_LEFT | FFMS_CH_FRONT_RIGHT;
    options->ForceResample = 0;
    const int formatResult = FFMS_SetOutputFormatA(m_audio, options, &error.info);
    FFMS_DestroyResampleOptions(options);
    if (formatResult != 0) {
        emit failed(generation, error.message(QStringLiteral("FFMS2 could not produce stereo signed 16-bit PCM")));
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
    // FFMS_AudioProperties describes the indexed source rather than the
    // post-conversion channel layout. The byte stream requested above is
    // always stereo, so the Qt sink and all PCM consumers must use 2 here.
    m_channels = decodedOutputChannels;
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
            for (int channel = 0; channel < decodedOutputChannels; ++channel) {
                const qint16 sample = samples[frame * decodedOutputChannels + channel];
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
    int maximumFrames,
    int outputSampleRate,
    int outputChannels,
    PcmSampleFormat outputSampleFormat)
{
    if (!m_audio || generation != m_generation || !stillWanted(generation))
        return;
    const qint64 boundedStart = std::clamp<qint64>(startSample, 0, m_totalSamples);
    const qint64 boundedEnd = std::clamp<qint64>(endSample, boundedStart, m_totalSamples);
    const int frameCount = static_cast<int>(std::min<qint64>(
        std::max(0, maximumFrames), boundedEnd - boundedStart));
    if (frameCount <= 0) {
        clearResampler();
        emit pcmReady(generation, playbackId, boundedStart, 0, {}, true);
        return;
    }

    QByteArray pcm;
    pcm.resize(static_cast<qsizetype>(frameCount) * bytesPerOutputFrame);
    ErrorBuffer error;
    if (FFMS_GetAudio(m_audio, pcm.data(), boundedStart, frameCount, &error.info) != 0) {
        emit pcmFailed(generation, playbackId,
            error.message(QStringLiteral("FFMS2 range playback decoding failed")));
        return;
    }

    const bool finished = boundedStart + frameCount >= boundedEnd;
    const AVSampleFormat targetFormat = avSampleFormat(outputSampleFormat);
    if (outputSampleRate <= 0 || outputChannels <= 0 || targetFormat == AV_SAMPLE_FMT_NONE) {
        emit pcmFailed(generation, playbackId,
            QStringLiteral("The platform audio sink requested an invalid PCM format"));
        return;
    }

    const bool directCopy = outputSampleRate == m_sampleRate
        && outputChannels == m_channels
        && outputSampleFormat == PcmSampleFormat::Int16;
    if (directCopy) {
        clearResampler();
        emit pcmReady(generation, playbackId, boundedStart, frameCount, pcm, finished);
        return;
    }

    const bool resamplerChanged = !m_resampler
        || m_resamplerPlaybackId != playbackId
        || m_resamplerNextSample != boundedStart
        || m_outputSampleRate != outputSampleRate
        || m_outputChannels != outputChannels
        || m_outputSampleFormat != outputSampleFormat;
    if (resamplerChanged) {
        clearResampler();
        AVChannelLayout inputLayout{};
        AVChannelLayout outputLayout{};
        av_channel_layout_default(&inputLayout, m_channels);
        av_channel_layout_default(&outputLayout, outputChannels);
        int result = swr_alloc_set_opts2(&m_resampler,
            &outputLayout, targetFormat, outputSampleRate,
            &inputLayout, AV_SAMPLE_FMT_S16, m_sampleRate,
            0, nullptr);
        av_channel_layout_uninit(&inputLayout);
        av_channel_layout_uninit(&outputLayout);
        if (result >= 0)
            result = swr_init(m_resampler);
        if (result < 0) {
            clearResampler();
            emit pcmFailed(generation, playbackId,
                ffmpegError(result, QStringLiteral("FFmpeg could not initialize audio output conversion")));
            return;
        }
        m_resamplerPlaybackId = playbackId;
        m_outputSampleRate = outputSampleRate;
        m_outputChannels = outputChannels;
        m_outputSampleFormat = outputSampleFormat;
        m_resamplerNextSample = boundedStart;
    }

    const int bytesPerSample = av_get_bytes_per_sample(targetFormat);
    const int outputCapacity = swr_get_out_samples(m_resampler, frameCount);
    const qint64 outputBytes = static_cast<qint64>(outputCapacity)
        * outputChannels * bytesPerSample;
    if (bytesPerSample <= 0 || outputCapacity < 0
        || outputBytes > std::numeric_limits<int>::max()) {
        emit pcmFailed(generation, playbackId,
            QStringLiteral("FFmpeg reported an invalid converted audio buffer size"));
        clearResampler();
        return;
    }

    QByteArray converted;
    converted.resize(static_cast<int>(outputBytes));
    auto *outputData = reinterpret_cast<uint8_t *>(converted.data());
    const auto *inputData = reinterpret_cast<const uint8_t *>(pcm.constData());
    const int outputFrames = swr_convert(m_resampler,
        &outputData, outputCapacity, &inputData, frameCount);
    if (outputFrames < 0) {
        emit pcmFailed(generation, playbackId,
            ffmpegError(outputFrames, QStringLiteral("FFmpeg audio output conversion failed")));
        clearResampler();
        return;
    }
    const int outputBytesPerFrame = outputChannels * bytesPerSample;
    converted.resize(outputFrames * outputBytesPerFrame);
    m_resamplerNextSample = boundedStart + frameCount;

    if (finished) {
        for (;;) {
            const int flushCapacity = swr_get_out_samples(m_resampler, 0);
            if (flushCapacity <= 0)
                break;
            QByteArray tail;
            tail.resize(flushCapacity * outputBytesPerFrame);
            auto *tailData = reinterpret_cast<uint8_t *>(tail.data());
            const int flushed = swr_convert(m_resampler, &tailData, flushCapacity, nullptr, 0);
            if (flushed < 0) {
                emit pcmFailed(generation, playbackId,
                    ffmpegError(flushed, QStringLiteral("FFmpeg audio conversion flush failed")));
                clearResampler();
                return;
            }
            if (flushed == 0)
                break;
            converted.append(tail.constData(), flushed * outputBytesPerFrame);
        }
        clearResampler();
    }
    emit pcmReady(generation, playbackId, boundedStart, frameCount, converted, finished);
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
