#include "media/media_session.h"

#include "media/ffms_audio_worker.h"
#include "media/ffms_spectrum_worker.h"
#include "media/ffms_video_worker.h"

#include <QtCore/QFileInfo>
#include <QtCore/QIODevice>
#include <QtCore/QMetaObject>
#include <QtMultimedia/QAudioDevice>
#include <QtMultimedia/QAudioFormat>
#include <QtMultimedia/QMediaDevices>

#include <algorithm>
#include <cmath>

namespace yoake::media {
namespace {
constexpr int playbackBufferFrames = 8192;

PcmSampleFormat workerSampleFormat(QAudioFormat::SampleFormat format)
{
    switch (format) {
    case QAudioFormat::UInt8:
        return PcmSampleFormat::UInt8;
    case QAudioFormat::Int16:
        return PcmSampleFormat::Int16;
    case QAudioFormat::Int32:
        return PcmSampleFormat::Int32;
    case QAudioFormat::Float:
        return PcmSampleFormat::Float32;
    case QAudioFormat::Unknown:
        break;
    }
    return PcmSampleFormat::Int16;
}
}

MediaSession::MediaSession(QObject *parent)
    : QObject(parent), m_waveform(this)
{
    m_videoThread.setObjectName(QStringLiteral("Yoake FFMS2 video source"));
    m_audioThread.setObjectName(QStringLiteral("Yoake FFMS2 audio playback source"));
    m_waveformThread.setObjectName(QStringLiteral("Yoake FFMS2 waveform source"));
    m_spectrumThread.setObjectName(QStringLiteral("Yoake FFMS2 spectrum source"));
    m_playbackTimer.setInterval(10);
    connect(&m_playbackTimer, &QTimer::timeout, this, &MediaSession::playbackTick);
}

MediaSession::~MediaSession()
{
    stopPlayback(true);
    if (m_videoWorker) {
        m_videoWorker->invalidate(++m_generation, ++m_frameCancellationId);
        if (m_videoThread.isRunning())
            QMetaObject::invokeMethod(m_videoWorker, &FfmsVideoWorker::shutdown, Qt::BlockingQueuedConnection);
        m_videoThread.quit();
        m_videoThread.wait();
    }
    if (m_audioWorker) {
        m_audioWorker->invalidate(m_generation);
        if (m_audioThread.isRunning())
            QMetaObject::invokeMethod(m_audioWorker, &FfmsAudioWorker::shutdown, Qt::BlockingQueuedConnection);
        m_audioThread.quit();
        m_audioThread.wait();
    }
    if (m_waveformWorker) {
        m_waveformWorker->invalidate(m_generation);
        if (m_waveformThread.isRunning())
            QMetaObject::invokeMethod(m_waveformWorker, &FfmsAudioWorker::shutdown, Qt::BlockingQueuedConnection);
        m_waveformThread.quit();
        m_waveformThread.wait();
    }
    if (m_spectrumWorker) {
        m_spectrumWorker->invalidate(m_generation, ++m_spectrumRequestId);
        if (m_spectrumThread.isRunning())
            QMetaObject::invokeMethod(m_spectrumWorker,
                &FfmsSpectrumWorker::shutdown, Qt::BlockingQueuedConnection);
        m_spectrumThread.quit();
        m_spectrumThread.wait();
    }
}

void MediaSession::ensureWorkers()
{
    if (m_videoWorker)
        return;

    m_videoWorker = new FfmsVideoWorker;
    m_videoWorker->moveToThread(&m_videoThread);
    connect(&m_videoThread, &QThread::finished, m_videoWorker, &QObject::deleteLater);
    connect(m_videoWorker, &FfmsVideoWorker::indexingProgress, this,
        [this](quint64 generation, double progress) {
            if (generation != m_generation)
                return;
            m_indexingProgress = std::clamp(progress, 0.0, 1.0);
            emit indexingProgressChanged();
        }, Qt::QueuedConnection);
    connect(m_videoWorker, &FfmsVideoWorker::opened, this,
        [this](quint64 generation,
            const QString &indexPath,
            bool indexCacheReused,
            const QVariantList &videoTracks,
            const QVariantList &audioTracks,
            int videoTrack,
            int audioTrack,
            const QVector<qint64> &frameStartsMs,
            qint64 durationMs,
            int width,
            int height,
            int sourcePixelFormat,
            double sourceFrameRate) {
            if (generation != m_generation)
                return;
            m_indexPath = indexPath;
            m_indexCacheReused = indexCacheReused;
            m_videoTracks = videoTracks;
            m_audioTracks = audioTracks;
            m_selectedVideoTrack = videoTrack;
            m_selectedAudioTrack = audioTrack;
            m_videoDurationMs = durationMs;
            m_durationMs = durationMs;
            m_timeMap.reset(frameStartsMs, durationMs);
            m_sourceWidth = width;
            m_sourceHeight = height;
            m_sourcePixelFormat = sourcePixelFormat;
            m_sourceFrameRate = sourceFrameRate;
            m_indexing = false;
            m_indexingProgress = 1.0;
            emit tracksChanged();
            emit metadataChanged();
            emit durationChanged();
            emit indexingChanged();
            emit indexingProgressChanged();
            if (hasVideo())
                requestFrameInternal(0);
            if (m_selectedAudioTrack >= 0) {
                QMetaObject::invokeMethod(m_audioWorker,
                    [worker = m_audioWorker, generation, path = m_sourcePath,
                        indexPath, track = m_selectedAudioTrack] {
                        worker->open(generation, path, indexPath, track, false);
                    },
                    Qt::QueuedConnection);
                QMetaObject::invokeMethod(m_waveformWorker,
                    [worker = m_waveformWorker, generation, path = m_sourcePath,
                        indexPath, track = m_selectedAudioTrack] {
                        worker->open(generation, path, indexPath, track, true);
                    },
                    Qt::QueuedConnection);
                QMetaObject::invokeMethod(m_spectrumWorker,
                    [worker = m_spectrumWorker, generation, path = m_sourcePath,
                        indexPath, track = m_selectedAudioTrack] {
                        worker->open(generation, path, indexPath, track);
                    },
                    Qt::QueuedConnection);
            } else {
                m_waveform.clear();
                m_audioReady = false;
                clearSpectrumState();
            }
        }, Qt::QueuedConnection);
    connect(m_videoWorker, &FfmsVideoWorker::frameReady, this,
        [this](quint64 generation, quint64 requestId, quint64 cancellationId, int frameNumber,
            qint64 startMs, qint64 endMs, const QImage &image) {
            if (generation != m_generation || cancellationId != m_frameCancellationId
                || requestId <= m_lastPresentedFrameRequest)
                return;
            // Playback requests share one cancellation epoch. This lets an
            // in-flight sequential decode complete while newer clock frames
            // replace only the pending request, avoiding the former starvation
            // loop where every decoded frame became stale before presentation.
            m_lastPresentedFrameRequest = requestId;
            m_frameImage = image;
            m_currentFrame = frameNumber;
            m_displayedFrameStartMs = startMs;
            m_displayedFrameEndMs = endMs;
            if (!m_playing)
                m_positionMs = startMs;
            if (requestId == m_frameRequestId)
                setFramePending(false);
            emit frameImageChanged();
            emit positionChanged();
        }, Qt::QueuedConnection);
    connect(m_videoWorker, &FfmsVideoWorker::failed, this,
        [this](quint64 generation, const QString &message) {
            if (generation != m_generation)
                return;
            if (m_indexing) {
                m_indexing = false;
                emit indexingChanged();
            }
            setFramePending(false);
            setError(message);
        }, Qt::QueuedConnection);

    m_audioWorker = new FfmsAudioWorker;
    m_audioWorker->moveToThread(&m_audioThread);
    connect(&m_audioThread, &QThread::finished, m_audioWorker, &QObject::deleteLater);
    connect(m_audioWorker, &FfmsAudioWorker::opened, this,
        [this](quint64 generation, int sampleRate, int channels, qint64 totalSamples,
            qint64 firstTimeMs, qint64 durationMs, int, int) {
            if (generation != m_generation)
                return;
            m_audioSampleRate = sampleRate;
            m_audioChannels = channels;
            m_audioTotalSamples = totalSamples;
            m_audioFirstTimeMs = firstTimeMs;
            m_audioReady = true;
            const qint64 oldDuration = m_durationMs;
            m_durationMs = std::max(m_videoDurationMs, durationMs);
            if (oldDuration != m_durationMs)
                emit durationChanged();
            emit metadataChanged();
            if (m_playing && !m_audioSink) {
                const qint64 resumeAt = std::min(
                    m_playbackEndMs, m_playbackStartMs + m_silentPlaybackClock.elapsed());
                m_playbackStartMs = resumeAt;
                m_positionMs = resumeAt;
                m_nextPcmSample = sampleForTime(resumeAt, false);
                m_endPcmSample = sampleForTime(m_playbackEndMs, true);
                m_audioFinished = false;
                m_pendingPcm.clear();
                m_pendingPcmOffset = 0;
                m_silentPlaybackClock.restart();
                if (!startAudioSink()) {
                    stopPlayback(true);
                    return;
                }
                emit positionChanged();
                pumpAudio();
            }
        }, Qt::QueuedConnection);
    connect(m_audioWorker, &FfmsAudioWorker::pcmReady, this,
        [this](quint64 generation, quint64 playbackId, qint64 startSample,
            int frameCount, const QByteArray &pcm, bool finished) {
            if (generation != m_generation || playbackId != m_playbackId || !m_playing)
                return;
            m_pcmPending = false;
            m_pendingPcm = pcm;
            m_pendingPcmOffset = 0;
            m_nextPcmSample = startSample + frameCount;
            m_audioFinished = finished;
            pumpAudio();
        }, Qt::QueuedConnection);
    connect(m_audioWorker, &FfmsAudioWorker::pcmFailed, this,
        [this](quint64 generation, quint64 playbackId, const QString &message) {
            if (generation != m_generation || playbackId != m_playbackId || !m_playing)
                return;
            m_pcmPending = false;
            setError(message);
            stopPlayback(true);
        }, Qt::QueuedConnection);
    connect(m_audioWorker, &FfmsAudioWorker::failed, this,
        [this](quint64 generation, const QString &message) {
            if (generation != m_generation)
                return;
            m_audioReady = false;
            emit metadataChanged();
            setError(message);
        }, Qt::QueuedConnection);

    m_waveformWorker = new FfmsAudioWorker;
    m_waveformWorker->moveToThread(&m_waveformThread);
    connect(&m_waveformThread, &QThread::finished, m_waveformWorker, &QObject::deleteLater);
    connect(m_waveformWorker, &FfmsAudioWorker::waveformChunk,
        &m_waveform, &WaveformModel::appendPeaks, Qt::QueuedConnection);
    connect(m_waveformWorker, &FfmsAudioWorker::failed, this,
        [this](quint64 generation, const QString &message) {
            if (generation == m_generation)
                m_waveform.fail(generation, message);
        }, Qt::QueuedConnection);

    m_spectrumWorker = new FfmsSpectrumWorker;
    m_spectrumWorker->moveToThread(&m_spectrumThread);
    connect(&m_spectrumThread, &QThread::finished, m_spectrumWorker, &QObject::deleteLater);
    connect(m_spectrumWorker, &FfmsSpectrumWorker::spectrumReady, this,
        [this](quint64 generation, quint64 requestId, const QImage &image, int cacheLevel) {
            if (generation != m_generation || requestId != m_spectrumRequestId)
                return;
            m_spectrumImage = image;
            m_spectrumCacheLevel = cacheLevel;
            m_spectrumBusy = false;
            m_spectrumErrorString.clear();
            emit spectrumImageChanged();
            emit spectrumStatusChanged();
        }, Qt::QueuedConnection);
    connect(m_spectrumWorker, &FfmsSpectrumWorker::failed, this,
        [this](quint64 generation, quint64 requestId, const QString &message) {
            if (generation != m_generation || requestId != m_spectrumRequestId)
                return;
            m_spectrumBusy = false;
            m_spectrumErrorString = message;
            emit spectrumStatusChanged();
        }, Qt::QueuedConnection);

    m_videoThread.start();
    m_audioThread.start();
    m_waveformThread.start();
    m_spectrumThread.start();
}

void MediaSession::open(const QUrl &source)
{
    if (!source.isLocalFile()) {
        setError(QStringLiteral("FFMS2 media sources must be local files"));
        return;
    }
    m_selectedVideoTrack = -1;
    m_selectedAudioTrack = -1;
    m_source = source;
    m_sourcePath = QFileInfo(source.toLocalFile()).absoluteFilePath();
    emit sourceChanged();
    beginOpen(m_sourcePath);
}

void MediaSession::beginOpen(const QString &path)
{
    ensureWorkers();
    stopPlayback(true);
    ++m_generation;
    ++m_frameRequestId;
    ++m_frameCancellationId;
    ++m_spectrumRequestId;
    emit generationChanged(m_generation);
    resetMediaState();
    m_sourcePath = path;
    m_indexing = true;
    m_indexingProgress = 0.0;
    m_waveform.beginDecode(m_generation);
    clearSpectrumState();
    emit indexingChanged();
    emit indexingProgressChanged();
    setError({});
    m_videoWorker->invalidate(m_generation, m_frameCancellationId);
    m_audioWorker->invalidate(m_generation);
    m_waveformWorker->invalidate(m_generation);
    m_spectrumWorker->invalidate(m_generation, m_spectrumRequestId);
    QMetaObject::invokeMethod(m_videoWorker,
        [worker = m_videoWorker, generation = m_generation, path,
            videoTrack = m_selectedVideoTrack, audioTrack = m_selectedAudioTrack] {
            worker->open(generation, path, videoTrack, audioTrack);
        },
        Qt::QueuedConnection);
}

void MediaSession::close()
{
    stopPlayback(true);
    ++m_generation;
    ++m_frameRequestId;
    ++m_frameCancellationId;
    ++m_spectrumRequestId;
    emit generationChanged(m_generation);
    if (m_videoWorker) {
        m_videoWorker->invalidate(m_generation, m_frameCancellationId);
        QMetaObject::invokeMethod(m_videoWorker,
            [worker = m_videoWorker, generation = m_generation] { worker->close(generation); },
            Qt::QueuedConnection);
    }
    if (m_audioWorker) {
        m_audioWorker->invalidate(m_generation);
        QMetaObject::invokeMethod(m_audioWorker,
            [worker = m_audioWorker, generation = m_generation] { worker->close(generation); },
            Qt::QueuedConnection);
    }
    if (m_waveformWorker) {
        m_waveformWorker->invalidate(m_generation);
        QMetaObject::invokeMethod(m_waveformWorker,
            [worker = m_waveformWorker, generation = m_generation] { worker->close(generation); },
            Qt::QueuedConnection);
    }
    if (m_spectrumWorker) {
        m_spectrumWorker->invalidate(m_generation, m_spectrumRequestId);
        QMetaObject::invokeMethod(m_spectrumWorker,
            [worker = m_spectrumWorker, generation = m_generation] { worker->close(generation); },
            Qt::QueuedConnection);
    }
    m_source = {};
    m_sourcePath.clear();
    m_selectedVideoTrack = -1;
    m_selectedAudioTrack = -1;
    resetMediaState();
    m_waveform.clear();
    clearSpectrumState();
    setError({});
    emit sourceChanged();
}

void MediaSession::resetMediaState()
{
    const bool hadFrame = !m_frameImage.isNull();
    m_indexPath.clear();
    m_indexCacheReused = false;
    m_timeMap.reset();
    m_videoTracks.clear();
    m_audioTracks.clear();
    m_videoDurationMs = 0;
    m_durationMs = 0;
    m_positionMs = 0;
    m_displayedFrameStartMs = 0;
    m_displayedFrameEndMs = 0;
    m_currentFrame = -1;
    m_requestedFrame = -1;
    m_lastPresentedFrameRequest = 0;
    m_sourceWidth = 0;
    m_sourceHeight = 0;
    m_sourcePixelFormat = -1;
    m_sourceFrameRate = 0.0;
    m_audioTotalSamples = 0;
    m_audioFirstTimeMs = 0;
    m_audioSampleRate = 0;
    m_audioChannels = 2;
    m_audioReady = false;
    m_pendingPcm.clear();
    m_pendingPcmOffset = 0;
    m_indexing = false;
    m_indexingProgress = 0.0;
    m_frameImage = {};
    setFramePending(false);
    emit tracksChanged();
    emit metadataChanged();
    emit durationChanged();
    emit positionChanged();
    emit indexingChanged();
    emit indexingProgressChanged();
    if (hadFrame)
        emit frameImageChanged();
}

void MediaSession::requestFrame(int frameNumber)
{
    if (m_playing)
        pause();
    requestFrameInternal(frameNumber);
}

void MediaSession::requestFrameInternal(int frameNumber, bool cancelInFlight)
{
    if (!m_videoWorker || m_timeMap.isEmpty())
        return;
    const int bounded = std::clamp(frameNumber, 0, m_timeMap.frameCount() - 1);
    if (m_framePending && bounded == m_requestedFrame)
        return;
    m_requestedFrame = bounded;
    ++m_frameRequestId;
    if (cancelInFlight) {
        ++m_frameCancellationId;
        m_lastPresentedFrameRequest = 0;
        m_videoWorker->invalidate(m_generation, m_frameCancellationId);
    }
    setFramePending(true);
    QMetaObject::invokeMethod(m_videoWorker,
        [worker = m_videoWorker, generation = m_generation,
            requestId = m_frameRequestId, cancellationId = m_frameCancellationId, bounded] {
            worker->requestFrame(generation, requestId, cancellationId, bounded);
        },
        Qt::QueuedConnection);
}

void MediaSession::seek(qint64 positionMs)
{
    const qint64 bounded = std::clamp<qint64>(positionMs, 0, std::max<qint64>(0, m_durationMs));
    if (m_playing)
        stopPlayback(true);
    if (m_positionMs != bounded) {
        m_positionMs = bounded;
        emit positionChanged();
    }
    if (hasVideo())
        requestFrameInternal(m_timeMap.frameAtTime(bounded));
}

void MediaSession::stepFrames(int amount)
{
    if (!hasVideo() || amount == 0)
        return;
    if (m_playing)
        pause();
    const int base = m_currentFrame >= 0 ? m_currentFrame : 0;
    requestFrameInternal(base + amount);
}

void MediaSession::selectVideoTrack(int track)
{
    if (track == m_selectedVideoTrack || m_sourcePath.isEmpty())
        return;
    m_selectedVideoTrack = track;
    beginOpen(m_sourcePath);
}

void MediaSession::selectAudioTrack(int track)
{
    if (track == m_selectedAudioTrack || m_sourcePath.isEmpty())
        return;
    m_selectedAudioTrack = track;
    beginOpen(m_sourcePath);
}

void MediaSession::requestSpectrumViewport(qint64 startMs,
    qint64 endMs,
    int width,
    int height,
    const QColor &lowColor,
    const QColor &midColor,
    const QColor &highColor)
{
    if (!m_spectrumWorker || m_indexPath.isEmpty() || m_selectedAudioTrack < 0 || endMs <= startMs
        || width <= 0 || height <= 0)
        return;
    ++m_spectrumRequestId;
    m_spectrumWorker->invalidate(m_generation, m_spectrumRequestId);
    m_spectrumBusy = true;
    m_spectrumErrorString.clear();
    emit spectrumStatusChanged();
    QMetaObject::invokeMethod(m_spectrumWorker,
        [worker = m_spectrumWorker, generation = m_generation,
            requestId = m_spectrumRequestId, startMs, endMs, width, height,
            lowColor, midColor, highColor] {
            worker->renderViewport(generation, requestId, startMs, endMs,
                width, height, lowColor, midColor, highColor);
        },
        Qt::QueuedConnection);
}

void MediaSession::clearSpectrumState()
{
    const bool hadImage = !m_spectrumImage.isNull();
    const bool statusChanged = m_spectrumBusy || !m_spectrumErrorString.isEmpty()
        || m_spectrumCacheLevel != 0;
    m_spectrumImage = {};
    m_spectrumBusy = false;
    m_spectrumErrorString.clear();
    m_spectrumCacheLevel = 0;
    if (hadImage)
        emit spectrumImageChanged();
    if (statusChanged)
        emit spectrumStatusChanged();
}

void MediaSession::play()
{
    playRange(m_positionMs, m_durationMs);
}

void MediaSession::playRange(qint64 startMs, qint64 endMs)
{
    const qint64 start = std::clamp<qint64>(startMs, 0, std::max<qint64>(0, m_durationMs));
    const qint64 end = std::clamp<qint64>(endMs, start, std::max(start, m_durationMs));
    if (end <= start)
        return;

    stopPlayback(true);
    ++m_playbackId;
    m_playbackStartMs = start;
    m_playbackEndMs = end;
    m_nextPcmSample = sampleForTime(start, false);
    m_endPcmSample = sampleForTime(end, true);
    m_pcmPending = false;
    m_audioFinished = false;
    m_pendingPcm.clear();
    m_pendingPcmOffset = 0;
    m_positionMs = start;
    emit positionChanged();
    if (hasVideo())
        requestFrameInternal(m_timeMap.frameAtTime(start));
    m_silentPlaybackClock.restart();
    if (m_audioReady && !startAudioSink())
        return;
    setPlaying(true);
    m_playbackTimer.start();
    pumpAudio();
}

void MediaSession::pause()
{
    stopPlayback(true);
}

void MediaSession::stop()
{
    stopPlayback(true);
    if (hasVideo())
        requestFrameInternal(0);
    else if (m_positionMs != 0) {
        m_positionMs = 0;
        emit positionChanged();
    }
}

void MediaSession::togglePlayback()
{
    m_playing ? pause() : play();
}

bool MediaSession::startAudioSink()
{
    const QAudioDevice device = QMediaDevices::defaultAudioOutput();
    if (device.isNull()) {
        setError(QStringLiteral("Windows did not provide a default audio output device"));
        return false;
    }

    QAudioFormat sourceFormat;
    sourceFormat.setSampleRate(m_audioSampleRate);
    sourceFormat.setChannelCount(m_audioChannels);
    sourceFormat.setSampleFormat(QAudioFormat::Int16);
    QAudioFormat format = device.isFormatSupported(sourceFormat)
        ? sourceFormat : device.preferredFormat();
    if (!format.isValid() || format.sampleFormat() == QAudioFormat::Unknown
        || format.bytesPerFrame() <= 0 || !device.isFormatSupported(format)) {
        setError(QStringLiteral("The default audio device did not expose a usable preferred PCM format"));
        return false;
    }

    m_audioOutputFormat = format;
    m_audioOutputBytesPerFrame = format.bytesPerFrame();
    m_audioSink = std::make_unique<QAudioSink>(device, format);
    m_audioSink->setBufferSize(format.sampleRate() * m_audioOutputBytesPerFrame / 4);
    QAudioSink *sink = m_audioSink.get();
    connect(sink, &QAudioSink::stateChanged, this,
        [this, sink](QAudio::State state) {
            if (!m_audioSink || m_audioSink.get() != sink || !m_playing
                || state != QAudio::StoppedState || sink->error() == QAudio::NoError)
                return;
            QMetaObject::invokeMethod(this, [this, sink] {
                if (!m_audioSink || m_audioSink.get() != sink)
                    return;
                setError(QStringLiteral("The Windows audio output stopped with error %1")
                    .arg(static_cast<int>(sink->error())));
                stopPlayback(true);
            }, Qt::QueuedConnection);
        });
    m_audioDevice = m_audioSink->start();
    if (!m_audioDevice) {
        setError(QStringLiteral("The platform audio sink could not start"));
        m_audioSink.reset();
        m_audioOutputFormat = {};
        m_audioOutputBytesPerFrame = 0;
        return false;
    }
    return true;
}

void MediaSession::pumpAudio()
{
    if (!m_playing || !m_audioReady || !m_audioSink || !m_audioDevice || !m_audioWorker)
        return;
    if (m_pendingPcmOffset < m_pendingPcm.size()) {
        const qint64 available = std::min<qint64>(
            m_audioSink->bytesFree(), m_pendingPcm.size() - m_pendingPcmOffset);
        if (available <= 0)
            return;
        const qint64 written = m_audioDevice->write(
            m_pendingPcm.constData() + m_pendingPcmOffset, available);
        if (written < 0) {
            setError(QStringLiteral("The platform audio sink rejected decoded FFMS2 PCM"));
            stopPlayback(true);
            return;
        }
        m_pendingPcmOffset += written;
        if (m_pendingPcmOffset < m_pendingPcm.size())
            return;
        m_pendingPcm.clear();
        m_pendingPcmOffset = 0;
    }
    if (m_pcmPending || m_audioFinished)
        return;
    if (m_audioOutputBytesPerFrame <= 0 || m_audioOutputFormat.sampleRate() <= 0)
        return;
    const int writableOutputFrames = static_cast<int>(
        m_audioSink->bytesFree() / m_audioOutputBytesPerFrame);
    if (writableOutputFrames <= 0)
        return;
    const qint64 estimatedSourceFrames = static_cast<qint64>(std::ceil(
        static_cast<long double>(writableOutputFrames) * m_audioSampleRate
        / m_audioOutputFormat.sampleRate()));
    const int requestFrames = std::min(playbackBufferFrames,
        static_cast<int>(std::max<qint64>(1024, estimatedSourceFrames)));
    m_pcmPending = true;
    QMetaObject::invokeMethod(m_audioWorker,
        [worker = m_audioWorker, generation = m_generation, playbackId = m_playbackId,
            start = m_nextPcmSample, end = m_endPcmSample, requestFrames,
            outputSampleRate = m_audioOutputFormat.sampleRate(),
            outputChannels = m_audioOutputFormat.channelCount(),
            outputSampleFormat = workerSampleFormat(m_audioOutputFormat.sampleFormat())] {
            worker->requestPcm(generation, playbackId, start, end, requestFrames,
                outputSampleRate, outputChannels, outputSampleFormat);
        },
        Qt::QueuedConnection);
}

void MediaSession::playbackTick()
{
    if (!m_playing)
        return;
    qint64 elapsedMs = m_silentPlaybackClock.elapsed();
    if (m_audioSink && m_audioDevice)
        elapsedMs = m_audioSink->processedUSecs() / 1000;
    const qint64 mediaTime = std::min(m_playbackEndMs, m_playbackStartMs + elapsedMs);
    if (mediaTime != m_positionMs) {
        m_positionMs = mediaTime;
        emit positionChanged();
    }
    if (hasVideo()) {
        const int frame = m_timeMap.frameAtTime(mediaTime);
        if (frame >= 0 && frame != m_currentFrame && frame != m_requestedFrame)
            requestFrameInternal(frame, false);
    }
    pumpAudio();
    if (mediaTime >= m_playbackEndMs) {
        stopPlayback(true);
        return;
    }
    if (m_audioFinished && m_pendingPcm.isEmpty() && !m_pcmPending && m_audioSink
        && m_audioSink->bytesFree() >= m_audioSink->bufferSize()) {
        stopPlayback(true);
    }
}

void MediaSession::stopPlayback(bool)
{
    ++m_playbackId;
    m_playbackTimer.stop();
    m_pcmPending = false;
    m_audioFinished = false;
    m_pendingPcm.clear();
    m_pendingPcmOffset = 0;
    if (m_audioSink)
        m_audioSink->stop();
    m_audioDevice = nullptr;
    m_audioSink.reset();
    m_audioOutputFormat = {};
    m_audioOutputBytesPerFrame = 0;
    setPlaying(false);
}

qint64 MediaSession::sampleForTime(qint64 timeMs, bool roundUp) const
{
    const long double relativeMs = static_cast<long double>(timeMs - m_audioFirstTimeMs);
    const long double exact = relativeMs * static_cast<long double>(m_audioSampleRate) / 1000.0L;
    const qint64 sample = static_cast<qint64>(roundUp ? std::ceil(exact) : std::floor(exact));
    return std::clamp<qint64>(sample, 0, m_audioTotalSamples);
}

void MediaSession::setError(const QString &error)
{
    if (m_errorString == error)
        return;
    m_errorString = error;
    emit errorChanged();
}

void MediaSession::setFramePending(bool pending)
{
    if (m_framePending == pending)
        return;
    m_framePending = pending;
    emit framePendingChanged();
}

void MediaSession::setPlaying(bool playing)
{
    if (m_playing == playing)
        return;
    m_playing = playing;
    emit playbackStateChanged();
}

} // namespace yoake::media
