#pragma once

#include <QtCore/QByteArray>
#include <QtCore/QVector>
#include <QtCore/QtGlobal>

#include <complex>

namespace yoake::media {

class SpectrumAnalyzer final {
public:
    SpectrumAnalyzer(int fftSize, int sampleRate, int frequencyBands);

    [[nodiscard]] QByteArray analyzeInterleaved(
        const qint16 *samples, int frameCount, int channels);

    [[nodiscard]] static QVector<float> powerSpectrum(const QVector<float> &samples);
    [[nodiscard]] static int chooseLevel(double millisecondsPerPixel, int sampleRate);
    [[nodiscard]] static qint64 hopForLevel(int level);
    [[nodiscard]] static int fftSizeForLevel(int level);

private:
    static void transform(QVector<std::complex<float>> &values);
    [[nodiscard]] QByteArray mapFrequencyBands() const;

    int m_fftSize = 0;
    int m_sampleRate = 0;
    int m_frequencyBands = 0;
    QVector<float> m_window;
    QVector<std::complex<float>> m_fft;
};

} // namespace yoake::media
