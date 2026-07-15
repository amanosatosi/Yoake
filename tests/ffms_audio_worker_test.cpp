#include "media/ffms_audio_worker.h"

#include <ffms.h>

#include <QtCore/QDataStream>
#include <QtCore/QDir>
#include <QtCore/QFile>
#include <QtCore/QPointF>
#include <QtCore/QTemporaryDir>
#include <QtTest/QSignalSpy>
#include <QtTest/QTest>

#include <cmath>

namespace {

struct ErrorBuffer {
    char text[2048]{};
    FFMS_ErrorInfo info{FFMS_ERROR_SUCCESS, FFMS_ERROR_SUCCESS, static_cast<int>(sizeof(text)), text};
};

QString createWaveFile(const QString &directory)
{
    constexpr int sampleRate = 48000;
    constexpr int channels = 2;
    constexpr int seconds = 2;
    constexpr int frames = sampleRate * seconds;
    constexpr quint32 dataBytes = frames * channels * sizeof(qint16);

    const QString path = directory + QStringLiteral("/indexed-audio.wav");
    QFile file(path);
    if (!file.open(QIODevice::WriteOnly))
        return {};
    QDataStream stream(&file);
    stream.setByteOrder(QDataStream::LittleEndian);
    stream.writeRawData("RIFF", 4);
    stream << quint32{36 + dataBytes};
    stream.writeRawData("WAVEfmt ", 8);
    stream << quint32{16} << quint16{1} << quint16{channels} << quint32{sampleRate}
           << quint32{sampleRate * channels * sizeof(qint16)}
           << quint16{channels * sizeof(qint16)} << quint16{16};
    stream.writeRawData("data", 4);
    stream << dataBytes;
    for (int frame = 0; frame < frames; ++frame) {
        const double phase = 2.0 * 3.14159265358979323846 * 440.0 * frame / sampleRate;
        const qint16 sample = static_cast<qint16>(std::sin(phase) * 12000.0);
        stream << sample << sample;
    }
    file.close();
    return path;
}

QString createIndex(const QString &sourcePath, const QString &directory, int *audioTrack)
{
    ErrorBuffer error;
    const QByteArray source = QDir::toNativeSeparators(sourcePath).toUtf8();
    FFMS_Indexer *indexer = FFMS_CreateIndexer(source.constData(), &error.info);
    if (!indexer)
        return {};
    *audioTrack = -1;
    for (int track = 0; track < FFMS_GetNumTracksI(indexer); ++track) {
        if (FFMS_GetTrackTypeI(indexer, track) == FFMS_TYPE_AUDIO) {
            *audioTrack = track;
            break;
        }
    }
    if (*audioTrack < 0) {
        FFMS_CancelIndexing(indexer);
        return {};
    }
    FFMS_TrackIndexSettings(indexer, *audioTrack, 1, 0);
    FFMS_Index *index = FFMS_DoIndexing2(indexer, FFMS_IEH_STOP_TRACK, &error.info);
    if (!index)
        return {};
    const QString indexPath = directory + QStringLiteral("/indexed-audio.ffindex");
    const QByteArray encodedIndex = QDir::toNativeSeparators(indexPath).toUtf8();
    const int result = FFMS_WriteIndex(encodedIndex.constData(), index, &error.info);
    FFMS_DestroyIndex(index);
    return result == 0 ? indexPath : QString{};
}

} // namespace

class FfmsAudioWorkerTest final : public QObject {
    Q_OBJECT

private slots:
    void independentPlaybackAndWaveformSources()
    {
        qRegisterMetaType<QVector<QPointF>>();
        FFMS_Init(0, 0);
        QTemporaryDir directory;
        QVERIFY(directory.isValid());
        const QString sourcePath = createWaveFile(directory.path());
        QVERIFY2(!sourcePath.isEmpty(), "Could not create the temporary PCM WAV fixture");
        int audioTrack = -1;
        const QString indexPath = createIndex(sourcePath, directory.path(), &audioTrack);
        QVERIFY2(!indexPath.isEmpty(), "FFMS2 could not index the temporary PCM WAV fixture");

        yoake::media::FfmsAudioWorker playback;
        yoake::media::FfmsAudioWorker waveform;
        QSignalSpy playbackOpened(&playback, &yoake::media::FfmsAudioWorker::opened);
        QSignalSpy waveformOpened(&waveform, &yoake::media::FfmsAudioWorker::opened);
        QSignalSpy pcmReady(&playback, &yoake::media::FfmsAudioWorker::pcmReady);
        QSignalSpy waveformChunks(&waveform, &yoake::media::FfmsAudioWorker::waveformChunk);

        playback.invalidate(1);
        waveform.invalidate(1);
        playback.open(1, sourcePath, indexPath, audioTrack, false);
        waveform.open(1, sourcePath, indexPath, audioTrack, true);
        QCOMPARE(playbackOpened.count(), 1);
        QCOMPARE(waveformOpened.count(), 1);

        playback.requestPcm(1, 7, 24000, 32000, 4096);
        QCOMPARE(pcmReady.count(), 1);
        const QList<QVariant> decoded = pcmReady.takeFirst();
        QCOMPARE(decoded.at(1).toULongLong(), quint64{7});
        QCOMPARE(decoded.at(2).toLongLong(), qint64{24000});
        QCOMPARE(decoded.at(3).toInt(), 4096);
        QCOMPARE(decoded.at(4).toByteArray().size(), 4096 * 2 * static_cast<int>(sizeof(qint16)));

        QTRY_VERIFY_WITH_TIMEOUT(!waveformChunks.isEmpty(), 5000);
        QTRY_VERIFY_WITH_TIMEOUT(waveformChunks.constLast().at(6).toBool(), 5000);
    }
};

QTEST_GUILESS_MAIN(FfmsAudioWorkerTest)
#include "ffms_audio_worker_test.moc"
