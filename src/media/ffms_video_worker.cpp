#include "media/ffms_video_worker.h"

#include <ffms.h>

#include <QtCore/QCryptographicHash>
#include <QtCore/QDir>
#include <QtCore/QFile>
#include <QtCore/QFileInfo>
#include <QtCore/QStandardPaths>
#include <QtCore/QTimer>
#include <QtCore/QVariantMap>
#include <QtGui/QTransform>

#include <algorithm>
#include <cmath>
#include <cstring>
#include <mutex>

namespace yoake::media {
namespace {

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
    std::call_once(once, [] {
        FFMS_Init(0, 0);
        FFMS_SetLogLevel(FFMS_LOG_WARNING);
    });
}

QByteArray nativePath(const QString &path)
{
    return QDir::toNativeSeparators(path).toUtf8();
}

QVariantList tracksOfType(FFMS_Indexer *indexer, FFMS_TrackType type)
{
    QVariantList result;
    const int count = FFMS_GetNumTracksI(indexer);
    for (int track = 0; track < count; ++track) {
        if (FFMS_GetTrackTypeI(indexer, track) != type)
            continue;
        QVariantMap item;
        item.insert(QStringLiteral("track"), track);
        const char *codec = FFMS_GetCodecNameI(indexer, track);
        item.insert(QStringLiteral("codec"), codec ? QString::fromUtf8(codec) : QStringLiteral("unknown"));
        item.insert(QStringLiteral("label"), QStringLiteral("Track %1 - %2")
            .arg(track).arg(item.value(QStringLiteral("codec")).toString()));
        result.push_back(item);
    }
    return result;
}

int chooseTrack(const QVariantList &available, int preferred)
{
    for (const QVariant &entry : available) {
        const int track = entry.toMap().value(QStringLiteral("track")).toInt();
        if (track == preferred)
            return track;
    }
    return available.isEmpty() ? -1 : available.front().toMap().value(QStringLiteral("track")).toInt();
}

QString indexPathFor(const QString &sourcePath)
{
    const QString canonical = QFileInfo(sourcePath).canonicalFilePath();
    const QByteArray identity = (canonical.isEmpty() ? QFileInfo(sourcePath).absoluteFilePath() : canonical)
                                    .toCaseFolded().toUtf8();
    const QString key = QString::fromLatin1(
        QCryptographicHash::hash(identity, QCryptographicHash::Sha256).toHex().left(32));
    const QString directory = QDir(QStandardPaths::writableLocation(QStandardPaths::CacheLocation))
                                  .absoluteFilePath(QStringLiteral("ffms2"));
    QDir().mkpath(directory);
    return QDir(directory).absoluteFilePath(key + QStringLiteral(".ffindex"));
}

} // namespace

FfmsVideoWorker::FfmsVideoWorker(QObject *parent) : QObject(parent)
{
    initializeFfms();
}

FfmsVideoWorker::~FfmsVideoWorker()
{
    clearSource();
}

void FfmsVideoWorker::invalidate(quint64 generation, quint64 cancellationId) noexcept
{
    m_wantedGeneration.store(generation, std::memory_order_release);
    m_latestCancellation.store(cancellationId, std::memory_order_release);
}

bool FfmsVideoWorker::stillWanted(quint64 generation) const noexcept
{
    return m_wantedGeneration.load(std::memory_order_acquire) == generation;
}

void FfmsVideoWorker::clearSource()
{
    m_pendingFrame.valid = false;
    if (m_video) {
        FFMS_DestroyVideoSource(m_video);
        m_video = nullptr;
    }
    if (m_index) {
        FFMS_DestroyIndex(m_index);
        m_index = nullptr;
    }
    m_frameStartsMs.clear();
    m_durationMs = 0;
    m_width = 0;
    m_height = 0;
    m_rotation = 0;
    m_flip = 0;
}

void FfmsVideoWorker::open(
    quint64 generation, const QString &path, int preferredVideoTrack, int preferredAudioTrack)
{
    clearSource();
    m_generation = generation;
    if (!stillWanted(generation))
        return;

    const QFileInfo sourceInfo(path);
    if (!sourceInfo.isFile()) {
        emit failed(generation, QStringLiteral("Media source does not exist: %1").arg(path));
        return;
    }

    ErrorBuffer error;
    const QByteArray encodedSource = nativePath(sourceInfo.absoluteFilePath());
    FFMS_Indexer *indexer = FFMS_CreateIndexer(encodedSource.constData(), &error.info);
    if (!indexer) {
        emit failed(generation, error.message(QStringLiteral("FFMS2 could not open the media source")));
        return;
    }

    const QVariantList videoTracks = tracksOfType(indexer, FFMS_TYPE_VIDEO);
    const QVariantList audioTracks = tracksOfType(indexer, FFMS_TYPE_AUDIO);
    const int videoTrack = chooseTrack(videoTracks, preferredVideoTrack);
    const int audioTrack = chooseTrack(audioTracks, preferredAudioTrack);
    if (videoTrack < 0 && audioTrack < 0) {
        FFMS_CancelIndexing(indexer);
        emit failed(generation, QStringLiteral("The source has no FFMS2 video or audio tracks"));
        return;
    }

    const QString cachePath = indexPathFor(sourceInfo.absoluteFilePath());
    const QByteArray encodedCache = nativePath(cachePath);
    m_index = FFMS_ReadIndex(encodedCache.constData(), &error.info);
    if (m_index && FFMS_IndexBelongsToFile(m_index, encodedSource.constData(), &error.info) != 0) {
        FFMS_DestroyIndex(m_index);
        m_index = nullptr;
        QFile::remove(cachePath);
    }
    const bool indexCacheReused = m_index != nullptr;

    if (!m_index) {
        FFMS_TrackTypeIndexSettings(indexer, FFMS_TYPE_VIDEO, 1, 0);
        FFMS_TrackTypeIndexSettings(indexer, FFMS_TYPE_AUDIO, 1, 0);
        struct ProgressContext {
            FfmsVideoWorker *worker;
            quint64 generation;
        } progress{this, generation};
        FFMS_SetProgressCallback(indexer,
            [](int64_t current, int64_t total, void *opaque) -> int {
                auto *context = static_cast<ProgressContext *>(opaque);
                if (!context->worker->stillWanted(context->generation))
                    return 1;
                const double fraction = total > 0 ? static_cast<double>(current) / static_cast<double>(total) : 0.0;
                emit context->worker->indexingProgress(context->generation, fraction);
                return 0;
            },
            &progress);
        m_index = FFMS_DoIndexing2(indexer, FFMS_IEH_STOP_TRACK, &error.info);
        indexer = nullptr;
        if (!m_index) {
            if (stillWanted(generation))
                emit failed(generation, error.message(QStringLiteral("FFMS2 indexing failed")));
            return;
        }
        if (!stillWanted(generation)) {
            clearSource();
            return;
        }

        const QString temporaryPath = cachePath + QStringLiteral(".tmp-%1").arg(generation);
        const QByteArray encodedTemporary = nativePath(temporaryPath);
        QFile::remove(temporaryPath);
        if (FFMS_WriteIndex(encodedTemporary.constData(), m_index, &error.info) == 0) {
            QFile::remove(cachePath);
            if (!QFile::rename(temporaryPath, cachePath))
                QFile::remove(temporaryPath);
        } else {
            QFile::remove(temporaryPath);
        }
    } else {
        FFMS_CancelIndexing(indexer);
        indexer = nullptr;
    }

    int width = 0;
    int height = 0;
    int sourcePixelFormat = -1;
    double sourceFrameRate = 0.0;
    if (videoTrack >= 0) {
        m_video = FFMS_CreateVideoSource(encodedSource.constData(), videoTrack, m_index,
            0, FFMS_SEEK_NORMAL, &error.info);
        if (!m_video) {
            emit failed(generation, error.message(QStringLiteral("FFMS2 could not create the video source")));
            clearSource();
            return;
        }
        const FFMS_VideoProperties *properties = FFMS_GetVideoProperties(m_video);
        const FFMS_Frame *first = FFMS_GetFrame(m_video, 0, &error.info);
        if (!properties || !first) {
            emit failed(generation, error.message(QStringLiteral("FFMS2 could not decode the first frame")));
            clearSource();
            return;
        }
        width = first->EncodedWidth;
        height = first->EncodedHeight;
        sourcePixelFormat = first->EncodedPixelFormat;
        sourceFrameRate = properties->FPSDenominator > 0
            ? static_cast<double>(properties->FPSNumerator) / properties->FPSDenominator : 0.0;
        const int outputFormats[] = {FFMS_GetPixFmt("bgra"), -1};
        if (FFMS_SetOutputFormatV2(m_video, outputFormats, width, height,
                FFMS_RESIZER_BICUBIC, &error.info) != 0) {
            emit failed(generation, error.message(QStringLiteral("FFMS2 could not convert video frames to BGRA")));
            clearSource();
            return;
        }

        FFMS_Track *track = FFMS_GetTrackFromVideo(m_video);
        const FFMS_TrackTimeBase *timeBase = track ? FFMS_GetTimeBase(track) : nullptr;
        if (!track || !timeBase || timeBase->Den == 0) {
            emit failed(generation, QStringLiteral("FFMS2 did not expose video frame timing information"));
            clearSource();
            return;
        }
        m_frameStartsMs.reserve(properties->NumFrames);
        for (int frame = 0; frame < properties->NumFrames; ++frame) {
            const FFMS_FrameInfo *info = FFMS_GetFrameInfo(track, frame);
            if (!info) {
                emit failed(generation, QStringLiteral("FFMS2 omitted timing for source frame %1").arg(frame));
                clearSource();
                return;
            }
            const long double timestamp = static_cast<long double>(info->PTS)
                * static_cast<long double>(timeBase->Num) / static_cast<long double>(timeBase->Den);
            m_frameStartsMs.push_back(static_cast<qint64>(std::llround(timestamp)));
        }
        for (qsizetype frame = 1; frame < m_frameStartsMs.size(); ++frame)
            m_frameStartsMs[frame] = std::max(m_frameStartsMs[frame], m_frameStartsMs[frame - 1]);
        m_durationMs = std::max<qint64>(
            static_cast<qint64>(std::llround(properties->LastEndTime * 1000.0)),
            m_frameStartsMs.isEmpty() ? 0 : m_frameStartsMs.back());
        m_width = width;
        m_height = height;
        m_rotation = properties->Rotation;
        m_flip = properties->Flip;
    }

    emit indexingProgress(generation, 1.0);
    emit opened(generation, cachePath, indexCacheReused, videoTracks, audioTracks, videoTrack, audioTrack,
        m_frameStartsMs, m_durationMs, width, height, sourcePixelFormat, sourceFrameRate);
}

void FfmsVideoWorker::requestFrame(
    quint64 generation, quint64 requestId, quint64 cancellationId, int frameNumber)
{
    if (generation != m_generation || !m_video || !stillWanted(generation)
        || m_latestCancellation.load(std::memory_order_acquire) != cancellationId)
        return;
    m_pendingFrame = {generation, requestId, cancellationId, frameNumber, true};
    if (!m_frameScheduled) {
        m_frameScheduled = true;
        QTimer::singleShot(0, this, &FfmsVideoWorker::processPendingFrame);
    }
}

void FfmsVideoWorker::processPendingFrame()
{
    m_frameScheduled = false;
    if (!m_pendingFrame.valid || !m_video)
        return;
    const PendingFrame request = m_pendingFrame;
    m_pendingFrame.valid = false;
    if (!stillWanted(request.generation)
        || m_latestCancellation.load(std::memory_order_acquire) != request.cancellationId)
        return;

    const int last = static_cast<int>(m_frameStartsMs.size()) - 1;
    if (last < 0)
        return;
    const int frameNumber = std::clamp(request.frameNumber, 0, last);
    ErrorBuffer error;
    const FFMS_Frame *frame = FFMS_GetFrame(m_video, frameNumber, &error.info);
    if (!frame) {
        emit failed(request.generation,
            error.message(QStringLiteral("FFMS2 failed to decode source frame %1").arg(frameNumber)));
        return;
    }

    QImage image(frame->ScaledWidth, frame->ScaledHeight, QImage::Format_ARGB32);
    const int bytesPerLine = frame->ScaledWidth * 4;
    for (int row = 0; row < frame->ScaledHeight; ++row) {
        const auto *source = frame->Data[0] + static_cast<qsizetype>(row) * frame->Linesize[0];
        std::memcpy(image.scanLine(row), source, static_cast<size_t>(bytesPerLine));
    }
    if (m_flip > 0)
        image = image.mirrored(true, false);
    else if (m_flip < 0)
        image = image.mirrored(false, true);
    if (m_rotation % 360 != 0)
        image = image.transformed(QTransform().rotate(m_rotation));

    if (stillWanted(request.generation)
        && m_latestCancellation.load(std::memory_order_acquire) == request.cancellationId) {
        const qint64 start = m_frameStartsMs[frameNumber];
        const qint64 end = frameNumber + 1 < m_frameStartsMs.size()
            ? m_frameStartsMs[frameNumber + 1] : m_durationMs;
        emit frameReady(request.generation, request.requestId, request.cancellationId,
            frameNumber, start, end, image);
    }
    if (m_pendingFrame.valid && !m_frameScheduled) {
        m_frameScheduled = true;
        QTimer::singleShot(0, this, &FfmsVideoWorker::processPendingFrame);
    }
}

void FfmsVideoWorker::close(quint64 generation)
{
    if (generation != m_wantedGeneration.load(std::memory_order_acquire))
        return;
    m_generation = generation;
    clearSource();
}

void FfmsVideoWorker::shutdown()
{
    clearSource();
}

} // namespace yoake::media
