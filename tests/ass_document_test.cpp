#include "ass/ass_document.h"
#include "ass/ass_text_boundaries.h"
#include "media/frame_time_map.h"

#include <QtTest/QTest>

class AssDocumentTest final : public QObject {
    Q_OBJECT

private slots:
    void parsesAndPreservesUnknownSections();
    void editsEventsWithoutDiscardingMangetsuData();
    void handlesAssTimes();
    void selectsFontNameValues();
    void rejectsUnsafeEventFormats();
    void keepsValidEventlessDocumentsEventless();
    void mapsVariableFrameRateTimesWithoutFpsArithmetic();
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

QTEST_GUILESS_MAIN(AssDocumentTest)
#include "ass_document_test.moc"
