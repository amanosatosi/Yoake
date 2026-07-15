#include "media/spectrum_analyzer.h"

#include <algorithm>
#include <cmath>
#include <numbers>

namespace yoake::media {
namespace {

constexpr qint64 baseHop = 256;
constexpr int maximumLevel = 16;

bool isPowerOfTwo(int value)
{
    return value > 0 && (value & (value - 1)) == 0;
}

} // namespace

SpectrumAnalyzer::SpectrumAnalyzer(int fftSize, int sampleRate, int frequencyBands)
    : m_fftSize(fftSize), m_sampleRate(sampleRate), m_frequencyBands(frequencyBands),
      m_window(fftSize), m_fft(fftSize)
{
    Q_ASSERT(isPowerOfTwo(fftSize));
    Q_ASSERT(sampleRate > 0);
    Q_ASSERT(frequencyBands > 0);
    for (int index = 0; index < m_fftSize; ++index) {
        m_window[index] = 0.5f - 0.5f * std::cos(
            2.0f * std::numbers::pi_v<float> * index / std::max(1, m_fftSize - 1));
    }
}

QByteArray SpectrumAnalyzer::analyzeInterleaved(
    const qint16 *samples, int frameCount, int channels)
{
    const int availableFrames = std::clamp(frameCount, 0, m_fftSize);
    for (int frame = 0; frame < m_fftSize; ++frame) {
        float mono = 0.0f;
        if (samples && frame < availableFrames && channels > 0) {
            for (int channel = 0; channel < channels; ++channel)
                mono += static_cast<float>(samples[frame * channels + channel]);
            mono /= static_cast<float>(channels) * 32768.0f;
        }
        m_fft[frame] = std::complex<float>(mono * m_window[frame], 0.0f);
    }
    transform(m_fft);
    return mapFrequencyBands();
}

QVector<float> SpectrumAnalyzer::powerSpectrum(const QVector<float> &samples)
{
    if (!isPowerOfTwo(samples.size()))
        return {};
    QVector<std::complex<float>> values(samples.size());
    for (qsizetype index = 0; index < samples.size(); ++index) {
        const float window = 0.5f - 0.5f * std::cos(
            2.0f * std::numbers::pi_v<float> * index / std::max<qsizetype>(1, samples.size() - 1));
        values[index] = std::complex<float>(samples[index] * window, 0.0f);
    }
    transform(values);
    QVector<float> magnitudes(samples.size() / 2);
    const float scale = 4.0f / static_cast<float>(samples.size());
    for (qsizetype bin = 0; bin < magnitudes.size(); ++bin)
        magnitudes[bin] = std::abs(values[bin]) * scale;
    return magnitudes;
}

int SpectrumAnalyzer::chooseLevel(double millisecondsPerPixel, int sampleRate)
{
    if (millisecondsPerPixel <= 0.0 || sampleRate <= 0)
        return 0;
    const double desiredHop = millisecondsPerPixel * sampleRate / 1000.0;
    int level = 0;
    qint64 hop = baseHop;
    while (level < maximumLevel && static_cast<double>(hop * 2) <= desiredHop * 1.5) {
        hop *= 2;
        ++level;
    }
    return level;
}

qint64 SpectrumAnalyzer::hopForLevel(int level)
{
    return baseHop << std::clamp(level, 0, maximumLevel);
}

int SpectrumAnalyzer::fftSizeForLevel(int level)
{
    if (level >= 6)
        return 8192;
    if (level >= 3)
        return 4096;
    return 2048;
}

void SpectrumAnalyzer::transform(QVector<std::complex<float>> &values)
{
    const int size = static_cast<int>(values.size());
    Q_ASSERT(isPowerOfTwo(size));
    for (int index = 1, reversed = 0; index < size; ++index) {
        int bit = size >> 1;
        while (reversed & bit) {
            reversed ^= bit;
            bit >>= 1;
        }
        reversed ^= bit;
        if (index < reversed)
            std::swap(values[index], values[reversed]);
    }

    for (int length = 2; length <= size; length <<= 1) {
        const float angle = -2.0f * std::numbers::pi_v<float> / length;
        const std::complex<float> step(std::cos(angle), std::sin(angle));
        for (int first = 0; first < size; first += length) {
            std::complex<float> phase(1.0f, 0.0f);
            for (int offset = 0; offset < length / 2; ++offset) {
                const std::complex<float> even = values[first + offset];
                const std::complex<float> odd = values[first + offset + length / 2] * phase;
                values[first + offset] = even + odd;
                values[first + offset + length / 2] = even - odd;
                phase *= step;
            }
        }
    }
}

QByteArray SpectrumAnalyzer::mapFrequencyBands() const
{
    QByteArray result(m_frequencyBands, '\0');
    const int fftBins = m_fftSize / 2;
    const float maximumFrequency = std::min(20000.0f, m_sampleRate * 0.5f);
    const float minimumFrequency = std::min(45.0f, maximumFrequency * 0.25f);
    const float magnitudeScale = 4.0f / m_fftSize;
    const auto frequencyAt = [=](float position) {
        return minimumFrequency + (maximumFrequency - minimumFrequency)
            * std::pow(std::clamp(position, 0.0f, 1.0f), 2.4f);
    };

    for (int band = 0; band < m_frequencyBands; ++band) {
        const float lowerFrequency = frequencyAt(static_cast<float>(band) / m_frequencyBands);
        const float upperFrequency = frequencyAt(static_cast<float>(band + 1) / m_frequencyBands);
        const int firstBin = std::clamp(static_cast<int>(std::floor(
            lowerFrequency * m_fftSize / m_sampleRate)), 1, fftBins - 1);
        const int afterLastBin = std::clamp(static_cast<int>(std::ceil(
            upperFrequency * m_fftSize / m_sampleRate)), firstBin + 1, fftBins);
        float magnitude = 0.0f;
        for (int bin = firstBin; bin < afterLastBin; ++bin)
            magnitude = std::max(magnitude, std::abs(m_fft[bin]) * magnitudeScale);
        const float decibels = 20.0f * std::log10(std::max(magnitude, 0.000001f));
        const float normalized = std::clamp((decibels + 90.0f) / 78.0f, 0.0f, 1.0f);
        result[band] = static_cast<char>(std::lround(normalized * 255.0f));
    }
    return result;
}

} // namespace yoake::media
