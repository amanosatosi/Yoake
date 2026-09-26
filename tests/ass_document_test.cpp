#include "ass/ass_document.h"
#include "ass/ass_event_clipboard.h"
#include "ass/ass_event_edits.h"
#include "ass/ass_text_boundaries.h"
#include "media/frame_time_map.h"
#include "media/spectrum_analyzer.h"

#include <QtTest/QTest>

#include <algorithm>
#include <cmath>
#include <numbers>

class AssDocumentTest final : public QObject {
    Q_OBJECT

private slots:
    void parsesAndPreservesUnknownSections();
    void editsEventsWithoutDiscardingMangetsuData();
    void handlesAssTimes();
    void selectsFontNameValues();
    void rejectsUnsafeEventFormats();
    void keepsValidEventlessDocumentsEventless();
    void exposesStyleNamesWithoutRewritingStyleRecords();
    void eventListEditsPreserveRawRecordsAndSupportZeroEvents();
    void structuredRowClipboardRetainsFieldsAndRegeneratesIds();
    void eventSplitPreservesTextAndProducesValidTimingHalves();
    void readsAegisubLinkedMediaMetadata();
    void mapsVariableFrameRateTimesWithoutFpsArithmetic();
    void findsSpeechBandToneWithFft();
    void selectsStableSpectrumZoomLevels();
};

void AssDocumentTest::parsesAndPreservesUnknownSections()
{
    const QByteArray input =
        "[Script Info]\nScriptType: v4.00+\nX-Yoake-Test: keep me\n\n"
        "[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n"
        "Dialogue: 2,0:00:01.20,0:00:03.45,Default,A,0010,0020,0030,fx,Hello, world\n\n"
        "[Aegisub Extradata]\nData: 1,key,evalue\n";

    const auto document = yoake::ass::Document::parse(input);
    QCOMPARE(document.events().size(), 1);
    QCOMPARE(document.events().front().text, QStringLiteral("Hello, world"));
    QCOMPARE(document.events().front().layer, 2);
    const QByteArray output = document.serialize();
    QVERIFY(output.contains("X-Yoake-Test: keep me"));
    QVERIFY(output.contains("[Aegisub Extradata]"));
    QVERIFY(output.contains("Data: 1,key,evalue"));
}

void AssDocumentTest::editsEventsWithoutDiscardingMangetsuData()
{
    auto document = yoake::ass::Document::parse(
        QByteArrayLiteral("[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n"
                          "Dialogue: 0,0:00:00.00,0:00:05.00,Default,,0000,0000,0000,,{\\pgrd(0,0,10,10,45)}x\n"));
    document.events().front().actor = QStringLiteral("Toshiki");
    const QByteArray output = document.serialize();
    QVERIFY(output.contains("Toshiki"));
    QVERIFY(output.contains("\\pgrd(0,0,10,10,45)"));
}

void AssDocumentTest::handlesAssTimes()
{
    bool ok = false;
    QCOMPARE(yoake::ass::parseTime(QStringLiteral("1:02:03.45"), &ok), 3723450);
    QVERIFY(ok);
    QCOMPARE(yoake::ass::formatTime(3723450), QStringLiteral("1:02:03.45"));
}

void AssDocumentTest::selectsFontNameValues()
{
    const QString text = QStringLiteral("{\\fnArial Narrow}Text");
    const auto range = yoake::ass::TextBoundaries::rangeAt(text, text.indexOf(QStringLiteral("Narrow")) + 2);
    QCOMPARE(text.mid(range.first, range.second - range.first), QStringLiteral("Narrow"));
}

void AssDocumentTest::rejectsUnsafeEventFormats()
{
    QString error;
    const auto plainText = yoake::ass::Document::parse(QByteArrayLiteral("not a subtitle file\n"), &error);
    QVERIFY(!error.isEmpty());
    QVERIFY(plainText.events().isEmpty());

    error.clear();
    const auto ssa = yoake::ass::Document::parse(
        QByteArrayLiteral("[Events]\nFormat: Marked, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n"
                          "Dialogue: Marked=0,0:00:00.00,0:00:01.00,Default,,0000,0000,0000,,Text\n"),
        &error);
    QVERIFY(!error.isEmpty());
    QVERIFY(ssa.events().isEmpty());
}

void AssDocumentTest::keepsValidEventlessDocumentsEventless()
{
    QString error;
    const auto document = yoake::ass::Document::parse(
        QByteArrayLiteral("[Script Info]\nScriptType: v4.00+\n\n[Events]\n"
                          "Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n"),
        &error);
    QVERIFY2(error.isEmpty(), qPrintable(error));
    QVERIFY(document.events().isEmpty());
    QVERIFY(!document.serialize().contains("Dialogue:"));
}

void AssDocumentTest::exposesStyleNamesWithoutRewritingStyleRecords()
{
    const QByteArray input = QByteArrayLiteral(
        "[V4+ Styles]\n"
        "; style comments stay raw\n"
        "Format: Fontname ,  Name , Fontsize, PrimaryColour\n"
        "Style: Anime Font, My Anime Style , 42, &H00FFFFFF\n"
        "Style: Arial, Unknown Existing Style, 50, &H000000FF\n"
        "[Events]\n"
        "Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n");
    QString error;
    const auto document = yoake::ass::Document::parse(input, &error);
    QVERIFY2(error.isEmpty(), qPrintable(error));
    QCOMPARE(document.styleNames(), QStringList({QStringLiteral("My Anime Style"),
        QStringLiteral("Unknown Existing Style")}));
    const QByteArray output = document.serialize();
    QVERIFY(output.contains("Format: Fontname ,  Name , Fontsize, PrimaryColour"));
    QVERIFY(output.contains("Style: Anime Font, My Anime Style , 42, &H00FFFFFF"));
    QVERIFY(output.contains("; style comments stay raw"));
}

void AssDocumentTest::eventListEditsPreserveRawRecordsAndSupportZeroEvents()
{
    QString error;
    auto document = yoake::ass::Document::parse(QByteArrayLiteral(
        "[Before Events]\nKeep: before\n\n"
        "[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n"
        "Dialogue: 0,0:00:00.00,0:00:01.00,Default,,0000,0000,0000,,{\\pgrd(0,0,1,1,0)}first\n"
        "; keep this comment between event slots\n"
        "Dialogue: 0,0:00:01.00,0:00:02.00,Default,,0000,0000,0000,,second\n\n"
        "[After Events]\nKeep: after\n"), &error);
    QVERIFY2(error.isEmpty(), qPrintable(error));
    auto inserted = document.events().front();
    inserted.id = QUuid::createUuid();
    inserted.text = QStringLiteral("inserted");
    document.events().insert(1, inserted);
    std::swap(document.events()[0], document.events()[2]);
    document.events().removeAt(1);
    QByteArray output = document.serialize();
    QVERIFY(output.contains("[Before Events]"));
    QVERIFY(output.contains("[After Events]"));
    QVERIFY(output.contains("Keep: before"));
    QVERIFY(output.contains("Keep: after"));
    QVERIFY(output.contains("{\\pgrd(0,0,1,1,0)}first"));
    const qsizetype firstSerializedEvent = output.indexOf("Dialogue:");
    const qsizetype rawComment = output.indexOf("; keep this comment between event slots");
    const qsizetype secondSerializedEvent = output.indexOf("Dialogue:", firstSerializedEvent + 1);
    QVERIFY(firstSerializedEvent < rawComment);
    QVERIFY(rawComment < secondSerializedEvent);
    document.events().clear();
    output = document.serialize();
    QVERIFY(!output.contains("Dialogue:"));
    const auto roundTrip = yoake::ass::Document::parse(output, &error);
    QVERIFY2(error.isEmpty(), qPrintable(error));
    QVERIFY(roundTrip.events().isEmpty());
    QVERIFY(output.contains("Keep: after"));
}

void AssDocumentTest::structuredRowClipboardRetainsFieldsAndRegeneratesIds()
{
    yoake::ass::Event original;
    original.comment = true;
    original.layer = 4;
    original.startMs = 1234;
    original.endMs = 2345;
    original.style = QStringLiteral("Unknown style");
    original.actor = QStringLiteral("Actor");
    original.marginLeft = 10;
    original.marginRight = 20;
    original.marginVertical = 30;
    original.effect = QStringLiteral("fx");
    original.text = QStringLiteral("{\\pgrd(0,0,1,1,0)}Mangetsu");
    const QVector<yoake::ass::Event> originalEvents{original};
    QVector<yoake::ass::Event> decoded;
    const auto encoded = yoake::ass::EventClipboard::encode(originalEvents);
    QVERIFY(yoake::ass::EventClipboard::decode(encoded, &decoded));
    QCOMPARE(decoded.size(), 1);
    const auto &copy = decoded.front();
    QVERIFY(copy.id != original.id);
    QCOMPARE(copy.comment, original.comment);
    QCOMPARE(copy.layer, original.layer);
    QCOMPARE(copy.startMs, original.startMs);
    QCOMPARE(copy.endMs, original.endMs);
    QCOMPARE(copy.style, original.style);
    QCOMPARE(copy.actor, original.actor);
    QCOMPARE(copy.marginLeft, original.marginLeft);
    QCOMPARE(copy.marginRight, original.marginRight);
    QCOMPARE(copy.marginVertical, original.marginVertical);
    QCOMPARE(copy.effect, original.effect);
    QCOMPARE(copy.text, original.text);
    QVERIFY(yoake::ass::EventClipboard::plainText(decoded).contains(QStringLiteral("Mangetsu")));
    QVERIFY(!yoake::ass::EventClipboard::decode(QByteArrayLiteral("not JSON"), &decoded));
}

void AssDocumentTest::eventSplitPreservesTextAndProducesValidTimingHalves()
{
    yoake::ass::Event original;
    original.startMs = 1000;
    original.endMs = 2000;
    original.text = QStringLiteral("{\\pgrd(0,0,10,10,45)}A\U0001F600B");
    original.actor = QStringLiteral("Actor");
    const QString tagText = QStringLiteral("{\\pgrd(0,0,10,10,45)}");
    const qsizetype cursor = original.text.indexOf(QStringLiteral("A\U0001F600")) + 3;
    const auto split = yoake::ass::EventEdits::splitAtCursor(original, cursor, 1500);
    QVERIFY(split.has_value());
    QCOMPARE(split->first.text + split->second.text, original.text);
    QVERIFY(split->first.text.contains(tagText));
    QCOMPARE(split->first.text, QStringLiteral("{\\pgrd(0,0,10,10,45)}A\U0001F600"));
    QCOMPARE(split->second.text, QStringLiteral("B"));
    QCOMPARE(split->first.startMs, qint64(1000));
    QCOMPARE(split->first.endMs, qint64(1500));
    QCOMPARE(split->second.startMs, qint64(1500));
    QCOMPARE(split->second.endMs, qint64(2000));
    QCOMPARE(split->second.actor, original.actor);
    QVERIFY(split->first.id == original.id);
    QVERIFY(split->second.id != original.id);
    QVERIFY(!yoake::ass::EventEdits::splitAtCursor(original, 0, 1000).has_value());
    QVERIFY(!yoake::ass::EventEdits::splitAtCursor(original, 0, 2000).has_value());
}

void AssDocumentTest::readsAegisubLinkedMediaMetadata()
{
    const auto document = yoake::ass::Document::parse(QByteArrayLiteral(
        "[Aegisub Project Garbage]\n"
        "Audio URI: audio/commentary.flac\n"
        "Video File: ../video/episode.mkv\n"
        "Aegisub Video Position: 1421\n"
        "Active Line: 27\n"
        "Scroll Position: 20\n\n"
        "[Events]\n"
        "Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n"));
    const auto &properties = document.projectProperties();
    QCOMPARE(properties.videoFile, QStringLiteral("../video/episode.mkv"));
    QCOMPARE(properties.audioFile, QStringLiteral("audio/commentary.flac"));
    QCOMPARE(properties.videoPosition, 1421);
    QCOMPARE(properties.activeRow, 27);
    QCOMPARE(properties.scrollPosition, 20);
    QVERIFY(document.serialize().contains("Video File: ../video/episode.mkv"));
}

void AssDocumentTest::mapsVariableFrameRateTimesWithoutFpsArithmetic()
{
    yoake::media::FrameTimeMap map;
    map.reset({0, 40, 81, 121, 201, 241}, 301);
    QVERIFY(map.variableFrameRate());
    QCOMPARE(map.frameAtTime(0), 0);
    QCOMPARE(map.frameAtTime(80), 1);
    QCOMPARE(map.frameAtTime(81), 2);
    QCOMPARE(map.frameAtTime(200), 3);
    QCOMPARE(map.frameAtTime(201), 4);
    QCOMPARE(map.frameStartMs(4), 201);
    QCOMPARE(map.frameEndMs(4), 241);
    QCOMPARE(map.frameEndMs(5), 301);
}

void AssDocumentTest::findsSpeechBandToneWithFft()
{
    constexpr int sampleRate = 48000;
    constexpr int fftSize = 4096;
    constexpr double toneHz = 1000.0;
    QVector<float> samples(fftSize);
    for (int sample = 0; sample < fftSize; ++sample) {
        samples[sample] = static_cast<float>(std::sin(
            2.0 * std::numbers::pi * toneHz * sample / sampleRate));
    }
    const QVector<float> spectrum = yoake::media::SpectrumAnalyzer::powerSpectrum(samples);
    QVERIFY(!spectrum.isEmpty());
    const qsizetype peakBin = std::distance(spectrum.cbegin(),
        std::max_element(spectrum.cbegin(), spectrum.cend()));
    const double peakHz = static_cast<double>(peakBin) * sampleRate / fftSize;
    QVERIFY(std::abs(peakHz - toneHz) < 20.0);
}

void AssDocumentTest::selectsStableSpectrumZoomLevels()
{
    const int detailed = yoake::media::SpectrumAnalyzer::chooseLevel(2.0, 48000);
    const int timing = yoake::media::SpectrumAnalyzer::chooseLevel(12.0, 48000);
    const int overview = yoake::media::SpectrumAnalyzer::chooseLevel(1000.0, 48000);
    QCOMPARE(detailed, 0);
    QVERIFY(timing > detailed);
    QVERIFY(overview > timing);
    QVERIFY(yoake::media::SpectrumAnalyzer::hopForLevel(overview)
        > yoake::media::SpectrumAnalyzer::hopForLevel(timing));
}

QTEST_GUILESS_MAIN(AssDocumentTest)
#include "ass_document_test.moc"
