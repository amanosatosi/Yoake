#pragma once

#include "media/frame_time_map.h"
#include "media/waveform_model.h"

#include <QtCore/QElapsedTimer>
#include <QtCore/QObject>
#include <QtCore/QThread>
#include <QtCore/QTimer>
#include <QtCore/QUrl>
#include <QtCore/QVariantList>
#include <QtGui/QImage>
#include <QtMultimedia/QAudioSink>

#include <memory>

namespace yoake::media {

class FfmsAudioWorker;
class FfmsVideoWorker;

class MediaSession final : public QObject {
    Q_OBJECT
    Q_PROPERTY(QUrl source READ source NOTIFY sourceChanged)
    Q_PROPERTY(qint64 durationMs READ durationMs NOTIFY durationChanged)
    Q_PROPERTY(qint64 positionMs READ positionMs NOTIFY positionChanged)
    Q_PROPERTY(qint64 displayedFrameStartMs READ positionMs NOTIFY positionChanged)
    Q_PROPERTY(qint64 displayedFrameEndMs READ displayedFrameEndMs NOTIFY positionChanged)
    Q_PROPERTY(int currentFrame READ currentFrame NOTIFY positionChanged)
    Q_PROPERTY(int frameCount READ frameCount NOTIFY metadataChanged)
    Q_PROPERTY(double frameRate READ frameRate NOTIFY metadataChanged)
    Q_PROPERTY(bool variableFrameRate READ variableFrameRate NOTIFY metadataChanged)
    Q_PROPERTY(int sourceWidth READ sourceWidth NOTIFY metadataChanged)
    Q_PROPERTY(int sourceHeight READ sourceHeight NOTIFY metadataChanged)
    Q_PROPERTY(int sourcePixelFormat READ sourcePixelFormat NOTIFY metadataChanged)
    Q_PROPERTY(bool hasMedia READ hasMedia NOTIFY sourceChanged)
    Q_PROPERTY(bool hasVideo READ hasVideo NOTIFY metadataChanged)
    Q_PROPERTY(bool hasAudio READ hasAudio NOTIFY metadataChanged)
    Q_PROPERTY(bool indexing READ indexing NOTIFY indexingChanged)
    Q_PROPERTY(double indexingProgress READ indexingProgress NOTIFY indexingProgressChanged)
    Q_PROPERTY(bool framePending READ framePending NOTIFY framePendingChanged)
    Q_PROPERTY(bool playing READ playing NOTIFY playbackStateChanged)
    Q_PROPERTY(QString errorString READ errorString NOTIFY errorChanged)
    Q_PROPERTY(QVariantList videoTracks READ videoTracks NOTIFY tracksChanged)
    Q_PROPERTY(QVariantList audioTracks READ audioTracks NOTIFY tracksChanged)
    Q_PROPERTY(int selectedVideoTrack READ selectedVideoTrack WRITE selectVideoTrack NOTIFY tracksChanged)
    Q_PROPERTY(int selectedAudioTrack READ selectedAudioTrack WRITE selectAudioTrack NOTIFY tracksChanged)
    Q_PROPERTY(yoake::media::WaveformModel *waveform READ waveform CONSTANT)

public:
    explicit MediaSession(QObject *parent = nullptr);
    ~MediaSession() override;

    [[nodiscard]] QUrl source() const { return m_source; }
    [[nodiscard]] qint64 durationMs() const { return m_durationMs; }
    [[nodiscard]] qint64 positionMs() const { return m_positionMs; }
    [[nodiscard]] qint64 displayedFrameEndMs() const { return m_displayedFrameEndMs; }
    [[nodiscard]] int currentFrame() const { return m_currentFrame; }
    [[nodiscard]] int frameCount() const { return m_timeMap.frameCount(); }
    [[nodiscard]] double frameRate() const { return m_sourceFrameRate; }
    [[nodiscard]] bool variableFrameRate() const { return m_timeMap.variableFrameRate(); }
    [[nodiscard]] int sourceWidth() const { return m_sourceWidth; }
    [[nodiscard]] int sourceHeight() const { return m_sourceHeight; }
    [[nodiscard]] int sourcePixelFormat() const { return m_sourcePixelFormat; }
    [[nodiscard]] bool hasMedia() const { return !m_source.isEmpty(); }
    [[nodiscard]] bool hasVideo() const { return !m_timeMap.isEmpty(); }
    [[nodiscard]] bool hasAudio() const { return m_selectedAudioTrack >= 0; }
    [[nodiscard]] bool indexing() const { return m_indexing; }
    [[nodiscard]] double indexingProgress() const { return m_indexingProgress; }
    [[nodiscard]] bool framePending() const { return m_framePending; }
    [[nodiscard]] bool playing() const { return m_playing; }
    [[nodiscard]] QString errorString() const { return m_errorString; }
    [[nodiscard]] QVariantList videoTracks() const { return m_videoTracks; }
    [[nodiscard]] QVariantList audioTracks() const { return m_audioTracks; }
    [[nodiscard]] int selectedVideoTrack() const { return m_selectedVideoTrack; }
    [[nodiscard]] int selectedAudioTrack() const { return m_selectedAudioTrack; }
    [[nodiscard]] WaveformModel *waveform() { return &m_waveform; }
    [[nodiscard]] const QImage &frameImage() const { return m_frameImage; }

    Q_INVOKABLE void open(const QUrl &source);
    Q_INVOKABLE void close();
    Q_INVOKABLE void play();
    Q_INVOKABLE void pause();
    Q_INVOKABLE void stop();
    Q_INVOKABLE void togglePlayback();
    Q_INVOKABLE void playRange(qint64 startMs, qint64 endMs);
    Q_INVOKABLE void seek(qint64 positionMs);
    Q_INVOKABLE void requestFrame(int frameNumber);
    Q_INVOKABLE void stepFrames(int amount);
    Q_INVOKABLE int frameAtTime(qint64 timeMs) const { return m_timeMap.frameAtTime(timeMs); }
    Q_INVOKABLE qint64 timestampForFrame(int frame) const { return m_timeMap.frameStartMs(frame); }
    Q_INVOKABLE qint64 frameEndForFrame(int frame) const { return m_timeMap.frameEndMs(frame); }
    Q_INVOKABLE void selectVideoTrack(int track);
    Q_INVOKABLE void selectAudioTrack(int track);

signals:
    void sourceChanged();
    void durationChanged();
    void positionChanged();
    void metadataChanged();
    void tracksChanged();
    void indexingChanged();
    void indexingProgressChanged();
    void framePendingChanged();
    void frameImageChanged();
    void playbackStateChanged();
    void errorChanged();
    void generationChanged(quint64 generation);

private slots:
    void playbackTick();
    void pumpAudio();

private:
    void ensureWorkers();
    void beginOpen(const QString &path);
    void resetMediaState();
    void requestFrameInternal(int frameNumber);
    void setError(const QString &error);
    void setFramePending(bool pending);
    void setPlaying(bool playing);
    void stopPlayback(bool keepPosition);
    void startAudioSink();
    [[nodiscard]] qint64 sampleForTime(qint64 timeMs, bool roundUp) const;

    QUrl m_source;
    QString m_sourcePath;
    QString m_indexPath;
    FrameTimeMap m_timeMap;
    WaveformModel m_waveform;
    QThread m_videoThread;
    QThread m_audioThread;
    FfmsVideoWorker *m_videoWorker = nullptr;
    FfmsAudioWorker *m_audioWorker = nullptr;
    QTimer m_playbackTimer;
    QElapsedTimer m_silentPlaybackClock;
    std::unique_ptr<QAudioSink> m_audioSink;
    QIODevice *m_audioDevice = nullptr;
    QImage m_frameImage;
    QVariantList m_videoTracks;
    QVariantList m_audioTracks;
    qint64 m_durationMs = 0;
    qint64 m_videoDurationMs = 0;
    qint64 m_positionMs = 0;
    qint64 m_displayedFrameEndMs = 0;
    qint64 m_audioTotalSamples = 0;
    qint64 m_audioFirstTimeMs = 0;
    qint64 m_playbackStartMs = 0;
    qint64 m_playbackEndMs = 0;
    qint64 m_nextPcmSample = 0;
    qint64 m_endPcmSample = 0;
    quint64 m_generation = 0;
    quint64 m_frameRequestId = 0;
    quint64 m_playbackId = 0;
    int m_currentFrame = -1;
    int m_requestedFrame = -1;
    int m_selectedVideoTrack = -1;
    int m_selectedAudioTrack = -1;
    int m_sourceWidth = 0;
    int m_sourceHeight = 0;
    int m_sourcePixelFormat = -1;
    int m_audioSampleRate = 48000;
    int m_audioChannels = 2;
    double m_sourceFrameRate = 0.0;
    double m_indexingProgress = 0.0;
    QString m_errorString;
    bool m_indexing = false;
    bool m_framePending = false;
    bool m_playing = false;
    bool m_audioReady = false;
    bool m_pcmPending = false;
    bool m_audioFinished = false;
};

} // namespace yoake::media
