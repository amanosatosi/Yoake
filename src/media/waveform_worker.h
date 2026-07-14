#pragma once

#include <QtCore/QObject>
#include <QtCore/QPointF>
#include <QtCore/QUrl>
#include <QtMultimedia/QAudioBuffer>

#include <memory>

QT_BEGIN_NAMESPACE
class QAudioDecoder;
QT_END_NAMESPACE

namespace yoake::media {

class WaveformWorker final : public QObject {
    Q_OBJECT

public:
    explicit WaveformWorker(QObject *parent = nullptr);
    ~WaveformWorker() override;

public slots:
    void decode(const QUrl &source, quint64 generation);
    void cancel();

signals:
    void peaksReady(quint64 generation, const QVector<QPointF> &peaks, bool complete);
    void failed(quint64 generation, const QString &error);

private slots:
    void consumeBuffer();
    void finish();
    void decoderError();

private:
    void appendBuffer(const QAudioBuffer &buffer);
    void appendSample(float sample);
    void compact();

    std::unique_ptr<QAudioDecoder> m_decoder;
    QVector<QPointF> m_peaks;
    quint64 m_generation = 0;
    int m_framesPerPeak = 512;
    int m_frameCount = 0;
    float m_minimum = 1.0F;
    float m_maximum = -1.0F;
    int m_lastPublished = 0;
};

} // namespace yoake::media
