#include "media/media_session.h"

#include "media/waveform_worker.h"

#include <QtCore/QMetaObject>
#include <QtMultimedia/QMediaMetaData>

#include <algorithm>
#include <cmath>

namespace yoake::media {

MediaSession::MediaSession(QObject *parent)
    : QObject(parent), m_waveform(this)
{
    m_player.setAudioOutput(&m_audioOutput);
    m_waveformThread.setObjectName(QStringLiteral("Yoake waveform decoder"));

    connect(&m_player, &QMediaPlayer::durationChanged, this, &MediaSession::durationChanged);
    connect(&m_player, &QMediaPlayer::positionChanged, this, &MediaSession::positionChanged);
    connect(&m_player, &QMediaPlayer::positionChanged, this, [this](qint64 position) {
        if (m_rangeEndMs >= 0 && position >= m_rangeEndMs) {
            m_rangeEndMs = -1;
            m_player.pause();
        }
    });
    connect(&m_player, &QMediaPlayer::playbackStateChanged, this, &MediaSession::playbackStateChanged);
    connect(&m_player, &QMediaPlayer::metaDataChanged, this, &MediaSession::updateMetadata);
    connect(&m_player, &QMediaPlayer::errorOccurred, this,
        [this](QMediaPlayer::Error, const QString &message) {
            m_errorString = message;
            emit errorChanged();
        });
}

MediaSession::~MediaSession()
{
    m_player.stop();
    if (m_waveformWorker && m_waveformThread.isRunning())
        QMetaObject::invokeMethod(m_waveformWorker, &WaveformWorker::cancel, Qt::BlockingQueuedConnection);
    m_waveformThread.quit();
    m_waveformThread.wait();
}

qint64 MediaSession::currentFrame() const
{
    return static_cast<qint64>(std::floor(positionMs() * m_frameRate / 1000.0));
}

void MediaSession::open(const QUrl &source)
{
    if (source.isEmpty())
        return;
    ++m_generation;
    emit generationChanged(m_generation);
    m_player.stop();
    m_rangeEndMs = -1;
    if (!qFuzzyCompare(m_frameRate, 24.0)) {
        m_frameRate = 24.0;
        emit frameRateChanged();
    }
    m_source = source;
    m_errorString.clear();
    emit errorChanged();
    emit sourceChanged();
    m_player.setSource(source);
    m_waveform.beginDecode(m_generation);
    ensureWaveformWorker();
    QMetaObject::invokeMethod(m_waveformWorker,
        [worker = m_waveformWorker, source, generation = m_generation] { worker->decode(source, generation); },
        Qt::QueuedConnection);
}

void MediaSession::close()
{
    ++m_generation;
    emit generationChanged(m_generation);
    m_player.stop();
    m_rangeEndMs = -1;
    m_player.setSource({});
    m_source = {};
    m_waveform.clear();
    if (m_waveformWorker)
        QMetaObject::invokeMethod(m_waveformWorker, &WaveformWorker::cancel, Qt::QueuedConnection);
    if (!qFuzzyCompare(m_frameRate, 24.0)) {
        m_frameRate = 24.0;
        emit frameRateChanged();
    }
    if (!m_errorString.isEmpty()) {
        m_errorString.clear();
        emit errorChanged();
    }
    emit sourceChanged();
}

void MediaSession::play() { m_rangeEndMs = -1; m_player.play(); }
void MediaSession::pause() { m_player.pause(); }
void MediaSession::stop() { m_rangeEndMs = -1; m_player.stop(); }
void MediaSession::togglePlayback() { playing() ? pause() : play(); }

void MediaSession::playRange(qint64 startMs, qint64 endMs)
{
    if (endMs <= startMs)
        return;
    seek(startMs);
    m_rangeEndMs = endMs;
    m_player.play();
}

void MediaSession::seek(qint64 positionMs)
{
    m_player.setPosition(std::clamp<qint64>(positionMs, 0, std::max<qint64>(0, durationMs())));
}

void MediaSession::stepFrames(int amount)
{
    if (m_frameRate <= 0.0)
        return;
    const qint64 targetFrame = std::max<qint64>(0, currentFrame() + amount);
    seek(static_cast<qint64>(std::llround(targetFrame * 1000.0 / m_frameRate)));
}

void MediaSession::attachVideoOutput(QObject *output)
{
    m_videoOutput = output;
    m_player.setVideoOutput(output);
}

void MediaSession::detachVideoOutput(QObject *output)
{
    if (m_videoOutput != output)
        return;
    m_player.setVideoOutput(nullptr);
    m_videoOutput = nullptr;
}

void MediaSession::ensureWaveformWorker()
{
    if (m_waveformWorker)
        return;
    m_waveformWorker = new WaveformWorker;
    m_waveformWorker->moveToThread(&m_waveformThread);
    connect(&m_waveformThread, &QThread::finished, m_waveformWorker, &QObject::deleteLater);
    connect(m_waveformWorker, &WaveformWorker::peaksReady,
        &m_waveform, &WaveformModel::updatePeaks, Qt::QueuedConnection);
    connect(m_waveformWorker, &WaveformWorker::failed,
        &m_waveform, &WaveformModel::fail, Qt::QueuedConnection);
    m_waveformThread.start();
}

void MediaSession::updateMetadata()
{
    bool ok = false;
    const double rate = m_player.metaData().value(QMediaMetaData::VideoFrameRate).toDouble(&ok);
    if (ok && rate > 0.0 && !qFuzzyCompare(rate, m_frameRate)) {
        m_frameRate = rate;
        emit frameRateChanged();
    }
}

} // namespace yoake::media
