#include "media/waveform_worker.h"

#include <QtMultimedia/QAudioDecoder>
#include <QtMultimedia/QAudioFormat>

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <limits>

namespace yoake::media {
namespace {

float sampleValue(const char *data, const QAudioFormat &format, qsizetype index)
{
    return std::clamp(format.normalizedSampleValue(data + index * format.bytesPerSample()), -1.0F, 1.0F);
}

} // namespace

WaveformWorker::WaveformWorker(QObject *parent) : QObject(parent) { }

WaveformWorker::~WaveformWorker() = default;

void WaveformWorker::decode(const QUrl &source, quint64 generation)
{
    m_decoder.reset();
    m_generation = generation;
    m_peaks.clear();
    m_framesPerPeak = 512;
    m_frameCount = 0;
    m_minimum = 1.0F;
    m_maximum = -1.0F;
    m_lastPublished = 0;
    if (source.isEmpty())
        return;
    m_decoder = std::make_unique<QAudioDecoder>();
    connect(m_decoder.get(), &QAudioDecoder::bufferReady, this, &WaveformWorker::consumeBuffer);
    connect(m_decoder.get(), &QAudioDecoder::finished, this, &WaveformWorker::finish);
    connect(m_decoder.get(), qOverload<QAudioDecoder::Error>(&QAudioDecoder::error),
        this, [this](QAudioDecoder::Error) { decoderError(); });
    m_decoder->setSource(source);
    m_decoder->start();
}

void WaveformWorker::cancel()
{
    m_decoder.reset();
}

void WaveformWorker::consumeBuffer()
{
    if (!m_decoder)
        return;
    const QAudioBuffer buffer = m_decoder->read();
    if (buffer.isValid())
        appendBuffer(buffer);
    if (m_peaks.size() - m_lastPublished >= 512) {
        m_lastPublished = m_peaks.size();
        emit peaksReady(m_generation, m_peaks, false);
    }
}

void WaveformWorker::appendBuffer(const QAudioBuffer &buffer)
{
    const QAudioFormat format = buffer.format();
    const int channels = std::max(1, format.channelCount());
    const int bytesPerSample = format.bytesPerSample();
    if (bytesPerSample <= 0)
        return;
    const qsizetype sampleCount = buffer.byteCount() / bytesPerSample;
    const char *bytes = buffer.constData<char>();
    for (qsizetype sample = 0; sample < sampleCount; sample += channels) {
        float minimum = 1.0F;
        float maximum = -1.0F;
        for (int channel = 0; channel < channels && sample + channel < sampleCount; ++channel) {
            const float value = sampleValue(bytes, format, sample + channel);
            minimum = std::min(minimum, value);
            maximum = std::max(maximum, value);
        }
        appendSample(minimum);
        appendSample(maximum);
    }
}

void WaveformWorker::appendSample(float sample)
{
    m_minimum = std::min(m_minimum, sample);
    m_maximum = std::max(m_maximum, sample);
    if (++m_frameCount < m_framesPerPeak * 2)
        return;
    m_peaks.push_back(QPointF(m_minimum, m_maximum));
    m_frameCount = 0;
    m_minimum = 1.0F;
    m_maximum = -1.0F;
    if (m_peaks.size() > 8192)
        compact();
}

void WaveformWorker::compact()
{
    QVector<QPointF> compacted;
    compacted.reserve((m_peaks.size() + 1) / 2);
    for (int index = 0; index < m_peaks.size(); index += 2) {
        if (index + 1 == m_peaks.size()) {
            compacted.push_back(m_peaks[index]);
        } else {
            compacted.push_back(QPointF(
                std::min(m_peaks[index].x(), m_peaks[index + 1].x()),
                std::max(m_peaks[index].y(), m_peaks[index + 1].y())));
        }
    }
    m_peaks = std::move(compacted);
    m_framesPerPeak *= 2;
    m_lastPublished = 0;
}

void WaveformWorker::finish()
{
    if (m_frameCount > 0)
        m_peaks.push_back(QPointF(m_minimum, m_maximum));
    emit peaksReady(m_generation, m_peaks, true);
}

void WaveformWorker::decoderError()
{
    if (m_decoder && m_decoder->error() != QAudioDecoder::NoError)
        emit failed(m_generation, m_decoder->errorString());
}

} // namespace yoake::media
