#pragma once

#include "media/waveform_model.h"

#include <QtCore/QObject>
#include <QtCore/QThread>
#include <QtCore/QUrl>
#include <QtMultimedia/QAudioOutput>
#include <QtMultimedia/QMediaPlayer>

namespace yoake::media {

class WaveformWorker;

class MediaSession final : public QObject {
    Q_OBJECT
    Q_PROPERTY(QUrl source READ source NOTIFY sourceChanged)
    Q_PROPERTY(qint64 durationMs READ durationMs NOTIFY durationChanged)
    Q_PROPERTY(qint64 positionMs READ positionMs WRITE seek NOTIFY positionChanged)
    Q_PROPERTY(qint64 currentFrame READ currentFrame NOTIFY positionChanged)
    Q_PROPERTY(double frameRate READ frameRate NOTIFY frameRateChanged)
    Q_PROPERTY(bool hasMedia READ hasMedia NOTIFY sourceChanged)
    Q_PROPERTY(bool playing READ playing NOTIFY playbackStateChanged)
    Q_PROPERTY(QString errorString READ errorString NOTIFY errorChanged)
    Q_PROPERTY(yoake::media::WaveformModel *waveform READ waveform CONSTANT)

public:
    explicit MediaSession(QObject *parent = nullptr);
    ~MediaSession() override;

    [[nodiscard]] QUrl source() const { return m_source; }
    [[nodiscard]] qint64 durationMs() const { return m_player.duration(); }
    [[nodiscard]] qint64 positionMs() const { return m_player.position(); }
    [[nodiscard]] qint64 currentFrame() const;
    [[nodiscard]] double frameRate() const { return m_frameRate; }
    [[nodiscard]] bool hasMedia() const { return !m_source.isEmpty(); }
    [[nodiscard]] bool playing() const { return m_player.playbackState() == QMediaPlayer::PlayingState; }
    [[nodiscard]] QString errorString() const { return m_errorString; }
    [[nodiscard]] WaveformModel *waveform() { return &m_waveform; }

    Q_INVOKABLE void open(const QUrl &source);
    Q_INVOKABLE void close();
    Q_INVOKABLE void play();
    Q_INVOKABLE void pause();
    Q_INVOKABLE void stop();
    Q_INVOKABLE void togglePlayback();
    Q_INVOKABLE void playRange(qint64 startMs, qint64 endMs);
    Q_INVOKABLE void seek(qint64 positionMs);
    Q_INVOKABLE void stepFrames(int amount);
    Q_INVOKABLE void attachVideoOutput(QObject *output);
    Q_INVOKABLE void detachVideoOutput(QObject *output);

signals:
    void sourceChanged();
    void durationChanged();
    void positionChanged();
    void frameRateChanged();
    void playbackStateChanged();
    void errorChanged();
    void generationChanged(quint64 generation);

private:
    void ensureWaveformWorker();
    void updateMetadata();

    QUrl m_source;
    QMediaPlayer m_player;
    QAudioOutput m_audioOutput;
    WaveformModel m_waveform;
    QThread m_waveformThread;
    WaveformWorker *m_waveformWorker = nullptr;
    QObject *m_videoOutput = nullptr;
    double m_frameRate = 24.0;
    QString m_errorString;
    quint64 m_generation = 0;
    qint64 m_rangeEndMs = -1;
};

} // namespace yoake::media
