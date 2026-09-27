#include "ass/ass_document.h"
#include "ass/visual_tags.h"
#include "ui/video_viewport.h"
#include "ui/visual_snap_service.h"

#include <QtTest/QTest>

#include <cmath>

class VisualToolsTest final : public QObject {
    Q_OBJECT

private slots:
    void viewportRoundTripsAllSpaces();
    void anchoredZoomKeepsContentUnderCursor();
    void pinchZoomTracksItsMovingCentroid();
    void resizePreservesViewedContentCenter();
    void panBoundsAndResetStayFinite();
    void snappingUsesScreenToleranceAndCanBeBypassed();
    void positionEditPreservesUnrelatedTags();
    void escapedBracesDoNotExposeLiteralTags();
    void moveEditPreservesExplicitTimes();
    void clipRoundTripsRectInverseAndVectorScale();
    void drawingPathEditingPreservesSurroundingText();
    void scriptAndStyleGeometryAreReadWithoutNormalizingSource();
};

namespace {
bool closePoint(QPointF a, QPointF b, qreal tolerance = 1e-5)
{
    return std::abs(a.x() - b.x()) <= tolerance && std::abs(a.y() - b.y()) <= tolerance;
}
}

void VisualToolsTest::viewportRoundTripsAllSpaces()
{
    yoake::ui::VideoViewport viewport;
    viewport.setViewportSize(QSizeF(1000, 700));
    viewport.setVideoSize(QSizeF(1920, 1080));
    viewport.setScriptSize(QSizeF(1280, 720));

    const QPointF video(347.25, 905.5);
    QVERIFY(closePoint(viewport.screenToVideo(viewport.videoToScreen(video)), video));
    const QPointF script(900.125, 210.5);
    QVERIFY(closePoint(viewport.screenToScript(viewport.scriptToScreen(script)), script));
    const QPointF delta(16.5, -28.25);
    QVERIFY(closePoint(viewport.screenDeltaToScriptDelta(viewport.scriptDeltaToScreenDelta(delta)), delta));
    QVERIFY(std::abs(viewport.displayedVideoRect().left() - 0.0) < 1e-6);
    QVERIFY(viewport.displayedVideoRect().height() < viewport.viewportSize().height());
}

void VisualToolsTest::anchoredZoomKeepsContentUnderCursor()
{
    yoake::ui::VideoViewport viewport;
    viewport.setViewportSize(QSizeF(900, 600));
    viewport.setVideoSize(QSizeF(1920, 1080));
    const QPointF anchor(137.5, 420.25);
    const QPointF content = viewport.screenToVideo(anchor);
    viewport.zoomAt(anchor, 2.5);
    QVERIFY(closePoint(viewport.videoToScreen(content), anchor));
    viewport.panBy(QPointF(80, -35));
    const QPointF secondAnchor(700, 90);
    const QPointF secondContent = viewport.screenToVideo(secondAnchor);
    viewport.zoomAt(secondAnchor, 1.4);
    QVERIFY(closePoint(viewport.videoToScreen(secondContent), secondAnchor));
    viewport.zoomTo(secondAnchor, 200.0);
    QVERIFY(std::abs(viewport.contentZoom() - 10.0) < 1e-8);
    viewport.zoomTo(secondAnchor, 0.01);
    QVERIFY(std::abs(viewport.contentZoom() - 0.125) < 1e-8);
}

void VisualToolsTest::pinchZoomTracksItsMovingCentroid()
{
    yoake::ui::VideoViewport viewport;
    viewport.setViewportSize(QSizeF(900, 600));
    viewport.setVideoSize(QSizeF(1920, 1080));
    const QPointF initialCentroid(340, 260);
    const QPointF content = viewport.screenToVideo(initialCentroid);
    viewport.beginAnchoredZoom(initialCentroid);
    viewport.updateAnchoredZoom(QPointF(370, 280), 1.8);
    QVERIFY(closePoint(viewport.videoToScreen(content), QPointF(370, 280)));
    viewport.endAnchoredZoom();
}

void VisualToolsTest::resizePreservesViewedContentCenter()
{
    yoake::ui::VideoViewport viewport;
    viewport.setViewportSize(QSizeF(800, 600));
    viewport.setVideoSize(QSizeF(1920, 1080));
    viewport.zoomAt(QPointF(250, 260), 3.0);
    viewport.panBy(QPointF(74, -63));
    const QPointF viewed = viewport.screenToVideo(QPointF(400, 300));
    viewport.setViewportSize(QSizeF(1200, 820));
    QVERIFY(closePoint(viewport.screenToVideo(QPointF(600, 410)), viewed));
}

void VisualToolsTest::panBoundsAndResetStayFinite()
{
    yoake::ui::VideoViewport viewport;
    viewport.setViewportSize(QSizeF(800, 600));
    viewport.setVideoSize(QSizeF(1920, 1080));
    viewport.zoomAt(QPointF(400, 300), 4.0);
    viewport.panBy(QPointF(1e9, -1e9));
    QVERIFY(std::abs(viewport.panOffset().x()) < 5000);
    QVERIFY(std::abs(viewport.panOffset().y()) < 5000);
    viewport.resetView();
    QCOMPARE(viewport.contentZoom(), 1.0);
    QCOMPARE(viewport.panOffset(), QPointF{});
    QVERIFY(std::abs(viewport.displayedVideoRect().width() - 800.0) < 1e-6);
    QVERIFY(std::abs(viewport.displayedVideoRect().height() - 450.0) < 1e-6);
    viewport.zoomTo(QPointF(400, 300), 0.125);
    viewport.panBy(QPointF(1e9, 1e9));
    QVERIFY(viewport.displayedVideoRect().intersects(QRectF(QPointF(0, 0), viewport.viewportSize())));
}

void VisualToolsTest::snappingUsesScreenToleranceAndCanBeBypassed()
{
    yoake::ui::VideoViewport viewport;
    viewport.setViewportSize(QSizeF(1000, 600));
    viewport.setVideoSize(QSizeF(1920, 1080));
    yoake::ui::VisualSnapService snap(&viewport, 8.0);
    const auto nearCenter = snap.snap(QPointF(970, 550));
    QCOMPARE(nearCenter.point, QPointF(960, 540));
    QCOMPARE(nearCenter.guides.size(), 2);
    viewport.zoomAt(QPointF(500, 300), 2.0);
    const auto zoomed = snap.snap(QPointF(970, 550));
    QCOMPARE(zoomed.point, QPointF(970, 550));
    QCOMPARE(zoomed.guides.size(), 0);
    const auto bypassed = snap.snap(QPointF(970, 550), true);
    QCOMPARE(bypassed.point, QPointF(970, 550));
}

void VisualToolsTest::positionEditPreservesUnrelatedTags()
{
    const QString original = QStringLiteral("{\\blur1\\bord3\\pos(300,500)\\1c&HFFFFFF&\\shad0}Hello");
    const QString updated = yoake::ass::VisualTags::setPoint(original, u"pos", QPointF(640.25, 360.5));
    QVERIFY(updated.startsWith(QStringLiteral("{\\blur1\\bord3\\pos(640.25,360.5)\\1c&HFFFFFF&\\shad0}")));
    QVERIFY(updated.endsWith(QStringLiteral("Hello")));
    QCOMPARE(yoake::ass::VisualTags::point(updated, u"pos").value(), QPointF(640.25, 360.5));
    const QString inserted = yoake::ass::VisualTags::setPoint(QStringLiteral("Plain text"), u"pos", QPointF(10, 20));
    QCOMPARE(inserted, QStringLiteral("{\\pos(10,20)}Plain text"));
    const QString insertedBeforeLaterBlock = yoake::ass::VisualTags::setPoint(
        QStringLiteral("Plain {\\i1}text"), u"pos", QPointF(10, 20));
    QCOMPARE(insertedBeforeLaterBlock, QStringLiteral("{\\pos(10,20)}Plain {\\i1}text"));
    const QString escaped = QStringLiteral("\\{not an override} {\\pos(1,2)}x");
    QCOMPARE(yoake::ass::VisualTags::point(escaped, u"pos").value(), QPointF(1, 2));
}

void VisualToolsTest::escapedBracesDoNotExposeLiteralTags()
{
    const QString original = QStringLiteral("\\{literal \\pos(1,2)\\}");
    QVERIFY(!yoake::ass::VisualTags::point(original, u"pos").has_value());
    const QString updated = yoake::ass::VisualTags::setPoint(original, u"pos", QPointF(9, 10));
    QCOMPARE(updated, QStringLiteral("{\\pos(9,10)}\\{literal \\pos(1,2)\\}"));
}

void VisualToolsTest::moveEditPreservesExplicitTimes()
{
    const QString original = QStringLiteral("{\\move(100,600,900,300,250,1250)\\blur1}MOVE");
    auto move = yoake::ass::VisualTags::move(original);
    QVERIFY(move.has_value());
    move->end = QPointF(1200.5, 80.25);
    const QString updated = yoake::ass::VisualTags::setMove(original, *move);
    QVERIFY(updated.contains(QStringLiteral("\\move(100,600,1200.5,80.25,250,1250)")));
    QVERIFY(updated.contains(QStringLiteral("\\blur1")));
}

void VisualToolsTest::clipRoundTripsRectInverseAndVectorScale()
{
    const QString rect = QStringLiteral("{\\blur1\\iclip(100,200,500,600)}x");
    auto parsedRect = yoake::ass::VisualTags::clip(rect);
    QVERIFY(parsedRect && parsedRect->rectangle && parsedRect->inverse);
    parsedRect->bounds.translate(5, -10);
    const QString rectOut = yoake::ass::VisualTags::setClip(rect, *parsedRect);
    QVERIFY(rectOut.contains(QStringLiteral("\\iclip(105,190,505,590)")));
    parsedRect->bounds = QRectF(QPointF(500, 600), QPointF(100, 200));
    const QString normalizedOut = yoake::ass::VisualTags::setClip(rect, *parsedRect);
    QVERIFY(normalizedOut.contains(QStringLiteral("\\iclip(100,200,500,600)")));

    const QString vector = QStringLiteral("{\\clip(2,m 100 100 l 500 100 500 300)}x");
    const auto parsedVector = yoake::ass::VisualTags::clip(vector);
    QVERIFY(parsedVector && !parsedVector->rectangle);
    QCOMPARE(parsedVector->drawingScale, 2);
    QCOMPARE(parsedVector->path, QStringLiteral("m 100 100 l 500 100 500 300"));
    const QString vectorOut = yoake::ass::VisualTags::setClip(vector, *parsedVector);
    QVERIFY(vectorOut.contains(QStringLiteral("\\clip(2,m 100 100 l 500 100 500 300)")));
}

void VisualToolsTest::drawingPathEditingPreservesSurroundingText()
{
    const QString original = QStringLiteral("{\\i1}label {\\p2}m 0 0 l 100 0 100 80{\\p0} tail");
    const auto drawing = yoake::ass::VisualTags::drawing(original);
    QVERIFY(drawing.has_value());
    QCOMPARE(drawing->drawingScale, 2);
    QCOMPARE(drawing->path, QStringLiteral("m 0 0 l 100 0 100 80"));
    const QString edited = yoake::ass::VisualTags::setDrawingPath(original, u"m 2 3 l 105 3 105 85", 2);
    QVERIFY(edited.startsWith(QStringLiteral("{\\i1}label {\\p2}")));
    QVERIFY(edited.endsWith(QStringLiteral("{\\p0} tail")));
    QCOMPARE(yoake::ass::VisualTags::drawing(edited)->path, QStringLiteral("m 2 3 l 105 3 105 85"));
    const QString inserted = yoake::ass::VisualTags::setDrawingPath(QStringLiteral("Text"), u"m 0 0 l 1 1");
    QCOMPARE(inserted, QStringLiteral("Text{\\p1}m 0 0 l 1 1{\\p0}"));
}

void VisualToolsTest::scriptAndStyleGeometryAreReadWithoutNormalizingSource()
{
    const QByteArray source = QByteArrayLiteral(
        "[Script Info]\nScriptType: v4.00+\nPlayResX: 1280\nPlayResY: 720\n"
        "[V4+ Styles]\nFormat: Name, Fontname, Fontsize, ScaleX, ScaleY, Alignment, MarginL, MarginR, MarginV\n"
        "Style: Sign, Noto Sans, 62, 110, 95, 7, 45, 55, 30\n"
        "[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n"
        "Dialogue: 0,0:00:00.00,0:00:02.00,Sign,,0000,0000,0000,,Hi\n");
    QString error;
    const auto doc = yoake::ass::Document::parse(source, &error);
    QVERIFY2(error.isEmpty(), qPrintable(error));
    QCOMPARE(doc.projectProperties().playResX, 1280);
    QCOMPARE(doc.projectProperties().playResY, 720);
    const auto style = doc.style(u"Sign");
    QCOMPARE(style.fontName, QStringLiteral("Noto Sans"));
    QCOMPARE(style.fontSize, 62.0);
    QCOMPARE(style.alignment, 7);
    QCOMPARE(style.marginLeft, 45);
    QVERIFY(doc.serialize().contains("Style: Sign, Noto Sans, 62, 110, 95, 7, 45, 55, 30"));
}

QTEST_APPLESS_MAIN(VisualToolsTest)

#include "visual_tools_test.moc"
