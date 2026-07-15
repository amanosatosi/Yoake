#include "media/ffms_spectrum_worker.h"

#include "media/spectrum_analyzer.h"

#include <ffms.h>

#include <QtCore/QCache>
#include <QtCore/QDir>
#include <QtCore/QFileInfo>

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <mutex>

namespace yoake::media {
namespace {

constexpr int outputChannels = 2;
constexpr int bytesPerOutputFrame = outputChannels * static_cast<int>(sizeof(qint16));
constexpr int frequencyBands = 128;
constexpr int tileColumns = 128;
constexpr int maximumViewportWidth = 1024;
constexpr int maximumViewportHeight = 512;
constexpr qint64 maximumContiguousDecodeBytes = 8 * 1024 * 1024;
constexpr int cacheBytes = 96 * 1024 * 1024;

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

quint64 tileKey(int level, qint64 tileIndex)
{
    return (static_cast<quint64>(level) << 56)
        | (static_cast<quint64>(tileIndex) & UINT64_C(0x00ffffffffffffff));
}

QRgb interpolate(const QColor &first, const QColor &second, float amount)
{
    const float bounded = std::clamp(amount, 0.0f, 1.0f);
    const auto channel = [bounded](int a, int b) {
        return std::clamp(static_cast<int>(std::lround(a + (b - a) * bounded)), 0, 255);
    };
    return qRgb(channel(first.red(), second.red()),
        channel(first.green(), second.green()), channel(first.blue(), second.blue()));
}

} // namespace

class FfmsSpectrumWorker::TileCache final : public QCache<quint64, QByteArray> {
public:
    TileCache() { setMaxCost(cacheBytes); }
};

FfmsSpectrumWorker::FfmsSpectrumWorker(QObject *parent)
    : QObject(parent), m_tiles(std::make_unique<TileCache>())
{
    initializeFfms();
}

FfmsSpectrumWorker::~FfmsSpectrumWorker()
{
    clearSource();
}

void FfmsSpectrumWorker::invalidate(quint64 generation, quint64 requestId) noexcept
{
    m_wantedGeneration.store(generation, std::memory_order_release);
    m_latestRequest.store(requestId, std::memory_order_release);
}

bool FfmsSpectrumWorker::stillWanted(quint64 generation, quint64 requestId) const noexcept
{
    return m_wantedGeneration.load(std::memory_order_acquire) == generation
        && m_latestRequest.load(std::memory_order_acquire) == requestId;
}

void FfmsSpectrumWorker::clearSource()
{
    m_tiles->clear();
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
    m_sampleRate = 0;
}

void FfmsSpectrumWorker::open(
    quint64 generation, const QString &sourcePath, const QString &indexPath, int audioTrack)
{
    clearSource();
    m_generation = generation;
    if (m_wantedGeneration.load(std::memory_order_acquire) != generation || audioTrack < 0)
        return;

    ErrorBuffer error;
    const QByteArray encodedSource = nativePath(QFileInfo(sourcePath).absoluteFilePath());
    const QByteArray encodedIndex = nativePath(indexPath);
    m_index = FFMS_ReadIndex(encodedIndex.constData(), &error.info);
    if (!m_index || FFMS_IndexBelongsToFile(m_index, encodedSource.constData(), &error.info) != 0) {
        emit failed(generation, m_latestRequest.load(std::memory_order_acquire),
            error.message(QStringLiteral("The FFMS2 spectrum index is missing or stale")));
        clearSource();
        return;
    }

    m_audio = FFMS_CreateAudioSource2(encodedSource.constData(), audioTrack, m_index,
        FFMS_DELAY_TIME_ZERO, FFMS_GAP_FILL_ENABLED, 1.0, &error.info);
    if (!m_audio) {
        emit failed(generation, m_latestRequest.load(std::memory_order_acquire),
            error.message(QStringLiteral("FFMS2 could not create the spectrum audio source")));
        clearSource();
        return;
    }

    const FFMS_AudioProperties *sourceProperties = FFMS_GetAudioProperties(m_audio);
    if (!sourceProperties || sourceProperties->SampleRate <= 0) {
        emit failed(generation, m_latestRequest.load(std::memory_order_acquire),
            QStringLiteral("FFMS2 did not expose spectrum audio timing"));
        clearSource();
        return;
    }
    FFMS_ResampleOptions *options = FFMS_CreateResampleOptions(m_audio);
    if (!options) {
        emit failed(generation, m_latestRequest.load(std::memory_order_acquire),
            QStringLiteral("FFMS2 could not create spectrum format options"));
        clearSource();
        return;
    }
    options->SampleFormat = FFMS_FMT_S16;
    options->SampleRate = sourceProperties->SampleRate;
    options->ChannelLayout = FFMS_CH_FRONT_LEFT | FFMS_CH_FRONT_RIGHT;
    options->ForceResample = 0;
    const int formatResult = FFMS_SetOutputFormatA(m_audio, options, &error.info);
    FFMS_DestroyResampleOptions(options);
    if (formatResult != 0) {
        emit failed(generation, m_latestRequest.load(std::memory_order_acquire),
            error.message(QStringLiteral("FFMS2 could not normalize spectrum PCM")));
        clearSource();
        return;
    }

    const FFMS_AudioProperties *properties = FFMS_GetAudioProperties(m_audio);
    m_sampleRate = properties->SampleRate;
    m_channels = outputChannels;
    m_totalSamples = properties->NumSamples;
    m_firstTimeMs = static_cast<qint64>(std::llround(properties->FirstTime * 1000.0));
}

QByteArray FfmsSpectrumWorker::buildTile(
    quint64 requestId, int level, qint64 tileIndex, bool *ok)
{
    *ok = false;
    const int fftSize = SpectrumAnalyzer::fftSizeForLevel(level);
    const qint64 hop = SpectrumAnalyzer::hopForLevel(level);
    const qint64 firstWindowStart = tileIndex * tileColumns * hop - fftSize / 2;
    const qint64 spanFrames = (tileColumns - 1) * hop + fftSize;
    SpectrumAnalyzer analyzer(fftSize, m_sampleRate, frequencyBands);
    QByteArray result(tileColumns * frequencyBands, '\0');
    ErrorBuffer error;

    if (spanFrames * bytesPerOutputFrame <= maximumContiguousDecodeBytes) {
        QByteArray pcm(static_cast<qsizetype>(spanFrames * bytesPerOutputFrame), '\0');
        const qint64 validStart = std::clamp<qint64>(firstWindowStart, 0, m_totalSamples);
        const qint64 validEnd = std::clamp<qint64>(firstWindowStart + spanFrames, validStart, m_totalSamples);
        if (validEnd > validStart) {
            char *destination = pcm.data() + (validStart - firstWindowStart) * bytesPerOutputFrame;
            if (FFMS_GetAudio(m_audio, destination, validStart, validEnd - validStart, &error.info) != 0) {
                emit failed(m_generation, requestId,
                    error.message(QStringLiteral("FFMS2 spectrum tile decoding failed")));
                return {};
            }
        }
        for (int column = 0; column < tileColumns; ++column) {
            if (!stillWanted(m_generation, requestId))
                return {};
            const auto *window = reinterpret_cast<const qint16 *>(
                pcm.constData() + column * hop * bytesPerOutputFrame);
            const QByteArray energy = analyzer.analyzeInterleaved(window, fftSize, m_channels);
            std::copy(energy.cbegin(), energy.cend(), result.begin() + column * frequencyBands);
        }
    } else {
        QByteArray pcm(fftSize * bytesPerOutputFrame, '\0');
        for (int column = 0; column < tileColumns; ++column) {
            if (!stillWanted(m_generation, requestId))
                return {};
            pcm.fill('\0');
            const qint64 windowStart = firstWindowStart + column * hop;
            const qint64 validStart = std::clamp<qint64>(windowStart, 0, m_totalSamples);
            const qint64 validEnd = std::clamp<qint64>(windowStart + fftSize, validStart, m_totalSamples);
            if (validEnd > validStart) {
                char *destination = pcm.data() + (validStart - windowStart) * bytesPerOutputFrame;
                if (FFMS_GetAudio(m_audio, destination, validStart, validEnd - validStart, &error.info) != 0) {
                    emit failed(m_generation, requestId,
                        error.message(QStringLiteral("FFMS2 sparse spectrum tile decoding failed")));
                    return {};
                }
            }
            const QByteArray energy = analyzer.analyzeInterleaved(
                reinterpret_cast<const qint16 *>(pcm.constData()), fftSize, m_channels);
            std::copy(energy.cbegin(), energy.cend(), result.begin() + column * frequencyBands);
        }
    }
    *ok = true;
    return result;
}

void FfmsSpectrumWorker::renderViewport(quint64 generation,
    quint64 requestId,
    qint64 startMs,
    qint64 endMs,
    int width,
    int height,
    const QColor &lowColor,
    const QColor &midColor,
    const QColor &highColor)
{
    if (!m_audio || generation != m_generation || !stillWanted(generation, requestId)
        || endMs <= startMs || m_totalSamples <= 0 || m_sampleRate <= 0)
        return;

    const int imageWidth = std::clamp(width, 1, maximumViewportWidth);
    const int imageHeight = std::clamp(height, 1, maximumViewportHeight);
    const double millisecondsPerPixel = static_cast<double>(endMs - startMs) / imageWidth;
    const int level = SpectrumAnalyzer::chooseLevel(millisecondsPerPixel, m_sampleRate);
    const qint64 hop = SpectrumAnalyzer::hopForLevel(level);

    std::array<QRgb, 256> palette{};
    for (int value = 0; value < 256; ++value) {
        const float normalized = static_cast<float>(value) / 255.0f;
        palette[value] = normalized < 0.56f
            ? interpolate(lowColor, midColor, normalized / 0.56f)
            : interpolate(midColor, highColor, (normalized - 0.56f) / 0.44f);
    }

    QImage image(imageWidth, imageHeight, QImage::Format_RGB32);
    for (int x = 0; x < imageWidth; ++x) {
        if (!stillWanted(generation, requestId))
            return;
        const long double timeMs = startMs
            + (static_cast<long double>(x) + 0.5L) * (endMs - startMs) / imageWidth;
        const long double exactSample = (timeMs - m_firstTimeMs) * m_sampleRate / 1000.0L;
        const qint64 sample = std::clamp<qint64>(
            static_cast<qint64>(std::floor(exactSample)), 0, m_totalSamples - 1);
        const qint64 absoluteColumn = sample / hop;
        const qint64 tileIndex = absoluteColumn / tileColumns;
        const int column = static_cast<int>(absoluteColumn % tileColumns);
        const quint64 key = tileKey(level, tileIndex);
        QByteArray *tile = m_tiles->object(key);
        if (!tile) {
            bool ok = false;
            QByteArray generated = buildTile(requestId, level, tileIndex, &ok);
            if (!ok || !stillWanted(generation, requestId))
                return;
            m_tiles->insert(key, new QByteArray(std::move(generated)), tileColumns * frequencyBands);
            tile = m_tiles->object(key);
            if (!tile)
                return;
        }
        for (int y = 0; y < imageHeight; ++y) {
            const int band = imageHeight == 1 ? 0
                : (imageHeight - 1 - y) * (frequencyBands - 1) / (imageHeight - 1);
            const auto energy = static_cast<unsigned char>(tile->at(column * frequencyBands + band));
            reinterpret_cast<QRgb *>(image.scanLine(y))[x] = palette[energy];
        }
    }
    if (stillWanted(generation, requestId))
        emit spectrumReady(generation, requestId, image, level);
}

void FfmsSpectrumWorker::close(quint64 generation)
{
    if (generation != m_wantedGeneration.load(std::memory_order_acquire))
        return;
    m_generation = generation;
    clearSource();
}

void FfmsSpectrumWorker::shutdown()
{
    clearSource();
}

} // namespace yoake::media
