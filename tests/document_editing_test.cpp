#include "app/document_context.h"
#include "ass/ass_document.h"
#include "ass/visual_tags.h"
#include "ass/vector_path.h"
#include "models/subtitle_model.h"
#include "ui/video_viewport.h"
#include "ui/visual_tool.h"
#include "ui/visual_tool_manager.h"

#include <QtCore/QTemporaryDir>
#include <QtTest/QSignalSpy>
#include <QtTest/QTest>
#include <QtGui/QClipboard>
#include <QtGui/QGuiApplication>
#include <QtCore/QPointF>
#include <QtCore/QSizeF>
#include <QtCore/QtMath>
#include <cmath>

#include <memory>
#include <tuple>

using yoake::app::DocumentContext;
using yoake::ass::Document;
using yoake::ass::Event;
using yoake::models::SubtitleModel;

namespace {

const QByteArray fixture = R"ASS([Script Info]
ScriptType: v4.00+
X-Preserved: keep this metadata

[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
Style: Fancy,Arial,64,&H00FFFFFF,&H000000FF,&H00000000,&H80000000,0,0,0,0,100,100,0,0,1,3,1,2,60,60,40,1
Style: Default,Arial,48,&H00FFFFFF,&H000000FF,&H00000000,&H80000000,0,0,0,0,100,100,0,0,1,3,1,2,60,60,40,1

[Before Unknown]
Payload: leave untouched

[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
Dialogue: 2,0:00:01.00,0:00:02.00,Fancy,Alice,0010,0020,0030,fx,{\pgrd(0,0,10,10,45)}Aello
Dialogue: 1,0:00:03.00,0:00:04.00,Default,Bob,0001,0002,0003,,second
Dialogue: 3,0:00:05.00,0:00:06.00,Fancy,Bob,0004,0005,0006,,third
Dialogue: 0,0:00:07.00,0:00:08.00,Default,,0000,0000,0000,,Hello again

[Aegisub Extradata]
Data: 1,key,still here

[After Unknown]
Payload: keep after event edits
)ASS";

std::unique_ptr<DocumentContext> makeContext()
{
    QString error;
    Document document = Document::parse(fixture, &error);
    Q_ASSERT(error.isEmpty());
    return std::make_unique<DocumentContext>(std::move(document));
}

QVector<Event> eventsOf(const DocumentContext &context)
{
    QString error;
    const Document document = Document::parse(context.rendererSnapshot(), &error);
    Q_ASSERT(error.isEmpty());
    return document.events();
}

QVector<Event> eventsOf(const std::unique_ptr<DocumentContext> &context)
{
    return eventsOf(*context);
}

QString idAt(const SubtitleModel *model, int row)
{
    return model->data(model->index(row), SubtitleModel::IdRole).toString();
}

void selectRange(SubtitleModel *model, int first, int last)
{
    model->selectRow(first);
    model->selectRow(last, false, true);
}

struct ToolProbe {
    int factoryCalls = 0;
    int activations = 0;
    int pointerDowns = 0;
    int deactivations = 0;
};

class RecordingVisualTool final : public yoake::ui::VisualTool {
public:
    explicit RecordingVisualTool(std::shared_ptr<ToolProbe> probe) : m_probe(std::move(probe)) {}
    QVariantMap descriptor() const override { return {{QStringLiteral("id"), QStringLiteral("probe")}}; }
    QVariantList renderOverlay() const override
    {
        return {QVariantMap{{QStringLiteral("id"), QStringLiteral("probe-overlay")},
            {QStringLiteral("kind"), QStringLiteral("label")}, {QStringLiteral("label"), QStringLiteral("probe")}}};
    }
    void activate() override { ++m_probe->activations; }
    void deactivate() override { ++m_probe->deactivations; }
    void pointerDown(const QPointF &, int) override { ++m_probe->pointerDowns; }

private:
    std::shared_ptr<ToolProbe> m_probe;
};

} // namespace

class DocumentEditingTest final : public QObject {
    Q_OBJECT

private slots:
    void selectionSupportsRangesTogglesAndNavigation();
    void cpsIgnoresOverridesAndHandlesZeroDuration();
    void cpsCountsAssEscapesAccordingToVisibleText();
    void homeAndEndCanExtendSelection();
    void documentLocalStyleActorAndEffectSuggestionsRemainConservative();
    void insertBeforeAndAfterAreUndoableAndUseFreshIds();
    void duplicatePreservesFieldsAndCreatesNewIdentity();
    void deleteSingleManyAndAllRestoreSelection();
    void rowClipboardPasteRetainsFieldsAndCreatesNewIds();
    void joinUsesDocumentOrderAndOneSurvivor();
    void splitAtCursorPreservesRawAssTextAndUndo();
    void moveKeepsMultiSelectionStableAtEdges();
    void timingOperationsPreserveValidDurations();
    void findWrapsAndReplaceAllIsOneUndo();
    void replaceCurrentAndNextUseLiteralMatches();
    void editsKeepUnknownAssRecordsAndMangetsuText();
    void textEditsMergePerLineAndSelectionChangesBreakTheMerge();
    void undoRedoAreHardTextMergeBoundaries();
    void savedStateIsRestoredByUndoAndRedo();
    void visualDragCommitsOnceAndEscapeRestoresOriginal();
    void visualPositionUsesSharedTransformAfterNavigation();
    void visualMoveEndpointUsesSharedTransformAndKeepsTimes();
    void visualRotationToolsEditTheirTags();
    void visualScaleUsesScreenHandleAfterZoomAndPan();
    void visualScaleRectangleAcceptsReverseDrags();
    void viewportNavigationDoesNotChangeDocumentBytes();
    void visualRectClipResizeUsesSharedTransform();
    void visualVectorClipNodeUsesSharedTransform();
    void visualBezierControlsAndEndpointDragIndependently();
    void vectorNodesSupportControlToggleGroupDragAndSafeDelete();
    void visualVectorSegmentsConvertAndInsertOnCurves();
    void visualShiftMovesDefaultPositionAndSelectedOrigin();
    void visualShiftMovesSelectedEventsAsOneUndoableTransaction();
    void customVisualToolFactoryOwnsDispatchOverlayAndLifecycle();
    void alignmentKeepsPositionedTextVisuallyStationary();
    void unsupportedVectorSyntaxCannotBeOverwrittenByVisualTools();
    void visualClipCreationUsesSharedTransform();
    void visualDrawingNodeEditUsesLineAnchorAndUndo();
};

void DocumentEditingTest::selectionSupportsRangesTogglesAndNavigation()
{
    auto context = makeContext();
    SubtitleModel *model = context->lines();
    model->selectRow(2, true);
    QCOMPARE(model->selectedCount(), 2);
    QCOMPARE(model->activeRow(), 2);
    model->selectRow(1, false, true);
    QCOMPARE(model->selectedRows(), QVector<int>({1, 2}));
    model->selectAll();
    QCOMPARE(model->selectedCount(), 4);
    model->setActiveRow(2);
    model->clearToActive();
    QCOMPARE(model->selectedRows(), QVector<int>({2}));
    model->selectPrevious();
    QCOMPARE(model->activeRow(), 1);
    model->moveActive(2, true);
    QCOMPARE(model->activeRow(), 3);
    QCOMPARE(model->selectedRows(), QVector<int>({1, 2, 3}));
}

void DocumentEditingTest::cpsIgnoresOverridesAndHandlesZeroDuration()
{
    auto context = makeContext();
    QCOMPARE(context->lines()->data(context->lines()->index(0), SubtitleModel::CpsRole).toDouble(), 5.0);
    QCOMPARE(context->lines()->data(context->lines()->index(3), SubtitleModel::CpsRole).toDouble(), 11.0);

    QString error;
    const Document zeroDuration = Document::parse(QByteArrayLiteral(
        "[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n"
        "Dialogue: 0,0:00:01.00,0:00:01.00,Default,,0000,0000,0000,,{\\i1}x\n"), &error);
    QVERIFY2(error.isEmpty(), qPrintable(error));
    DocumentContext zeroContext(zeroDuration);
    QCOMPARE(zeroContext.lines()->data(zeroContext.lines()->index(0), SubtitleModel::CpsRole).toDouble(), 0.0);
}

void DocumentEditingTest::cpsCountsAssEscapesAccordingToVisibleText()
{
    QByteArray script = QByteArrayLiteral(
        "[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n"
        "Dialogue: 0,0:00:00.00,0:00:01.00,Default,,0000,0000,0000,,ab\\Ncd\n"
        "Dialogue: 0,0:00:00.00,0:00:01.00,Default,,0000,0000,0000,,ab\\ncd\n"
        "Dialogue: 0,0:00:00.00,0:00:01.00,Default,,0000,0000,0000,,ab\\hcd\n"
        "Dialogue: 0,0:00:00.00,0:00:01.00,Default,,0000,0000,0000,,{\\i1}abcd\n"
        "Dialogue: 0,0:00:00.00,0:00:01.00,Default,,0000,0000,0000,,A");
    script += QString::fromUcs4(U"\U0001F600").toUtf8();
    script += QByteArrayLiteral("B\n");
    QString error;
    Document document = Document::parse(script, &error);
    QVERIFY2(error.isEmpty(), qPrintable(error));
    DocumentContext context(std::move(document));
    QCOMPARE(context.lines()->data(context.lines()->index(0), SubtitleModel::CpsRole).toDouble(), 4.0);
    QCOMPARE(context.lines()->data(context.lines()->index(1), SubtitleModel::CpsRole).toDouble(), 4.0);
    QCOMPARE(context.lines()->data(context.lines()->index(2), SubtitleModel::CpsRole).toDouble(), 5.0);
    QCOMPARE(context.lines()->data(context.lines()->index(3), SubtitleModel::CpsRole).toDouble(), 4.0);
    QCOMPARE(context.lines()->data(context.lines()->index(4), SubtitleModel::CpsRole).toDouble(), 3.0);
}

void DocumentEditingTest::homeAndEndCanExtendSelection()
{
    auto toFirst = makeContext();
    toFirst->lines()->setActiveRow(2);
    toFirst->lines()->selectFirst(true);
    QCOMPARE(toFirst->lines()->selectedRows(), QVector<int>({0, 1, 2}));
    QCOMPARE(toFirst->lines()->activeRow(), 0);

    auto toLast = makeContext();
    toLast->lines()->setActiveRow(2);
    toLast->lines()->selectLast(true);
    QCOMPARE(toLast->lines()->selectedRows(), QVector<int>({2, 3}));
    QCOMPARE(toLast->lines()->activeRow(), 3);

    toLast->lines()->selectFirst();
    QCOMPARE(toLast->lines()->selectedRows(), QVector<int>({0}));
    toLast->lines()->selectLast();
    QCOMPARE(toLast->lines()->selectedRows(), QVector<int>({3}));
}

void DocumentEditingTest::documentLocalStyleActorAndEffectSuggestionsRemainConservative()
{
    auto context = makeContext();
    QCOMPARE(context->styleNames(), QStringList({QStringLiteral("Fancy"), QStringLiteral("Default")}));
    QCOMPARE(context->actorSuggestions(), QStringList({QStringLiteral("Alice"), QStringLiteral("Bob")}));
    QCOMPARE(context->effectSuggestions(), QStringList({QStringLiteral("fx")}));
    context->setActiveStyle(QStringLiteral("Manually entered style"));
    QCOMPARE(context->activeStyle(), QStringLiteral("Manually entered style"));
    QVERIFY(context->styleNames().contains(QStringLiteral("Fancy")));
    QVERIFY(context->rendererSnapshot().contains("Manually entered style"));
    context->setActiveActor(QStringLiteral("Carol"));
    QVERIFY(context->actorSuggestions().contains(QStringLiteral("Carol")));
    context->undo();
    QVERIFY(!context->actorSuggestions().contains(QStringLiteral("Carol")));
    context->redo();
    QVERIFY(context->actorSuggestions().contains(QStringLiteral("Carol")));
    context->undo();
    context->setActiveEffect(QStringLiteral("new effect"));
    QVERIFY(context->effectSuggestions().contains(QStringLiteral("new effect")));
    context->undo();
    QVERIFY(!context->effectSuggestions().contains(QStringLiteral("new effect")));
    context->redo();
    QVERIFY(context->effectSuggestions().contains(QStringLiteral("new effect")));
    QVERIFY(!context->actorSuggestions().contains(QStringLiteral("Carol")));
}

void DocumentEditingTest::insertBeforeAndAfterAreUndoableAndUseFreshIds()
{
    auto context = makeContext();
    const QString oldId = idAt(context->lines(), 1);
    context->lines()->setActiveRow(1);
    context->insertBeforeActive();
    QCOMPARE(context->lines()->rowCount(), 5);
    QCOMPARE(context->lines()->activeRow(), 1);
    const QString beforeId = idAt(context->lines(), 1);
    QVERIFY(beforeId != oldId);
    auto events = eventsOf(context);
    QCOMPARE(events[1].text, QString());
    QCOMPARE(events[1].style, QStringLiteral("Default"));
    QCOMPARE(events[1].startMs, qint64(2000));
    QCOMPARE(events[1].endMs, qint64(3000));
    context->undo();
    QCOMPARE(context->lines()->rowCount(), 4);
    QCOMPARE(idAt(context->lines(), 1), oldId);
    context->redo();
    QCOMPARE(idAt(context->lines(), 1), beforeId);

    context->lines()->setActiveRow(3);
    context->insertAfterActive();
    QCOMPARE(context->lines()->activeRow(), 4);
    events = eventsOf(context);
    QCOMPARE(events[4].startMs, qint64(6000));
    QCOMPARE(events[4].endMs, qint64(7000));
}

void DocumentEditingTest::duplicatePreservesFieldsAndCreatesNewIdentity()
{
    auto context = makeContext();
    context->lines()->setActiveRow(0);
    context->duplicateSelected();
    QCOMPARE(context->lines()->rowCount(), 5);
    const QVector<Event> events = eventsOf(context);
    QCOMPARE(events[0].text, events[1].text);
    QCOMPARE(events[0].style, events[1].style);
    QCOMPARE(events[0].actor, events[1].actor);
    QCOMPARE(events[0].effect, events[1].effect);
    QCOMPARE(events[0].marginLeft, events[1].marginLeft);
    QVERIFY(idAt(context->lines(), 0) != idAt(context->lines(), 1));
    QVERIFY(context->lines()->data(context->lines()->index(1), SubtitleModel::SelectedRole).toBool());
    context->undo();
    QCOMPARE(context->lines()->rowCount(), 4);
    context->redo();
    QCOMPARE(context->lines()->rowCount(), 5);
}

void DocumentEditingTest::deleteSingleManyAndAllRestoreSelection()
{
    auto context = makeContext();
    context->lines()->setActiveRow(1);
    context->deleteSelected();
    QCOMPARE(context->lines()->rowCount(), 3);
    QCOMPARE(context->lines()->activeRow(), 1);
    context->undo();
    QCOMPARE(context->lines()->rowCount(), 4);
    QCOMPARE(context->lines()->activeRow(), 1);
    QCOMPARE(context->lines()->selectedRows(), QVector<int>({1}));

    context->lines()->setActiveRow(0);
    context->deleteSelected();
    QCOMPARE(context->lines()->activeRow(), 0);
    context->undo();
    context->lines()->setActiveRow(3);
    context->deleteSelected();
    QCOMPARE(context->lines()->rowCount(), 3);
    QCOMPARE(context->lines()->activeRow(), 2);
    context->undo();

    selectRange(context->lines(), 1, 2);
    context->deleteSelected();
    QCOMPARE(context->lines()->rowCount(), 2);
    context->undo();
    QCOMPARE(context->lines()->rowCount(), 4);
    QCOMPARE(context->lines()->selectedRows(), QVector<int>({1, 2}));
    QCOMPARE(context->lines()->activeRow(), 2);

    context->lines()->setActiveRow(0);
    context->lines()->selectRow(3, true);
    context->deleteSelected();
    QCOMPARE(context->lines()->rowCount(), 2);
    context->undo();
    QCOMPARE(context->lines()->selectedRows(), QVector<int>({0, 3}));

    context->lines()->selectAll();
    context->deleteSelected();
    QCOMPARE(context->lines()->rowCount(), 0);
    QCOMPARE(context->lines()->activeRow(), -1);
    const QByteArray emptyDocumentBytes = context->rendererSnapshot();
    QVERIFY(emptyDocumentBytes.contains("[Aegisub Extradata]"));
    QString emptyDocumentError;
    const Document emptyDocument = Document::parse(emptyDocumentBytes, &emptyDocumentError);
    QVERIFY2(emptyDocumentError.isEmpty(), qPrintable(emptyDocumentError));
    QVERIFY(emptyDocument.events().isEmpty());
    context->undo();
    QCOMPARE(context->lines()->rowCount(), 4);
    QCOMPARE(context->lines()->selectedRows(), QVector<int>({0, 1, 2, 3}));
}

void DocumentEditingTest::rowClipboardPasteRetainsFieldsAndCreatesNewIds()
{
    auto context = makeContext();
    context->lines()->setActiveRow(0);
    const QString originalId = idAt(context->lines(), 0);
    const QString secondCopiedId = idAt(context->lines(), 2);
    const QVector<Event> originalEvents = eventsOf(context);
    context->lines()->selectRow(2, true);
    context->copySelected();
    QVERIFY(context->canPasteRows());
    QVERIFY(QGuiApplication::clipboard()->text().contains(QStringLiteral("Aello")));
    context->lines()->setActiveRow(1);
    context->pasteRows();
    QCOMPARE(context->lines()->rowCount(), 6);
    QCOMPARE(context->lines()->activeRow(), 3);
    QCOMPARE(context->lines()->selectedRows(), QVector<int>({2, 3}));
    const QVector<Event> pastedEvents = eventsOf(context);
    const Event firstPasted = pastedEvents[2];
    const Event secondPasted = pastedEvents[3];
    QVERIFY(idAt(context->lines(), 2) != originalId);
    QVERIFY(idAt(context->lines(), 3) != secondCopiedId);
    QCOMPARE(firstPasted.text, originalEvents[0].text);
    QCOMPARE(firstPasted.style, originalEvents[0].style);
    QCOMPARE(firstPasted.actor, originalEvents[0].actor);
    QCOMPARE(firstPasted.effect, originalEvents[0].effect);
    QCOMPARE(firstPasted.layer, originalEvents[0].layer);
    QCOMPARE(firstPasted.marginRight, originalEvents[0].marginRight);
    QCOMPARE(secondPasted.text, originalEvents[2].text);
    context->undo();
    QCOMPARE(context->lines()->rowCount(), 4);
    QCOMPARE(idAt(context->lines(), 0), originalId);

    selectRange(context->lines(), 0, 1);
    context->cutSelected();
    QCOMPARE(context->lines()->rowCount(), 2);
    context->undo();
    QCOMPARE(context->lines()->rowCount(), 4);
    QCOMPARE(context->lines()->selectedRows(), QVector<int>({0, 1}));
}

void DocumentEditingTest::joinUsesDocumentOrderAndOneSurvivor()
{
    auto context = makeContext();
    const QString survivor = idAt(context->lines(), 0);
    selectRange(context->lines(), 0, 2);
    context->joinSelected();
    QCOMPARE(context->lines()->rowCount(), 2);
    QCOMPARE(idAt(context->lines(), 0), survivor);
    const QVector<Event> events = eventsOf(context);
    QCOMPARE(events[0].text, QStringLiteral("{\\pgrd(0,0,10,10,45)}Aello\\Nsecond\\Nthird"));
    QCOMPARE(events[0].startMs, qint64(1000));
    QCOMPARE(events[0].endMs, qint64(6000));
    QCOMPARE(events[0].style, QStringLiteral("Fancy"));
    QCOMPARE(context->lines()->selectedRows(), QVector<int>({0}));
    context->undo();
    QCOMPARE(context->lines()->rowCount(), 4);
    QCOMPARE(context->lines()->selectedRows(), QVector<int>({0, 1, 2}));
}

void DocumentEditingTest::splitAtCursorPreservesRawAssTextAndUndo()
{
    auto context = makeContext();
    const QString originalId = idAt(context->lines(), 0);
    const QString originalText = eventsOf(context).front().text;
    const int split = originalText.indexOf(QStringLiteral("Aello")) + 2;
    context->splitActiveAtCursor(split);
    QCOMPARE(context->lines()->rowCount(), 5);
    const QVector<Event> events = eventsOf(context);
    QCOMPARE(idAt(context->lines(), 0), originalId);
    QCOMPARE(events[0].text + events[1].text, originalText);
    QVERIFY(events[0].text.contains(QStringLiteral("{\\pgrd")));
    QVERIFY(idAt(context->lines(), 1) != originalId);
    QCOMPARE(events[0].startMs, events[1].startMs);
    QCOMPARE(events[0].endMs, events[1].endMs);
    QCOMPARE(context->lines()->activeRow(), 1);
    QCOMPARE(context->lines()->selectedCount(), 2);
    context->undo();
    QCOMPARE(context->lines()->rowCount(), 4);
    QCOMPARE(eventsOf(context).front().text, originalText);
    context->splitActiveAtCursorAtPosition(3);
    QCOMPARE(context->lines()->rowCount(), 4);
}

void DocumentEditingTest::moveKeepsMultiSelectionStableAtEdges()
{
    auto context = makeContext();
    const QString a = idAt(context->lines(), 0);
    const QString b = idAt(context->lines(), 1);
    const QString c = idAt(context->lines(), 2);
    const QString d = idAt(context->lines(), 3);
    context->lines()->setActiveRow(1);
    context->lines()->selectRow(3, true);
    context->moveSelectedUp();
    QCOMPARE(idAt(context->lines(), 0), b);
    QCOMPARE(idAt(context->lines(), 1), a);
    QCOMPARE(idAt(context->lines(), 2), d);
    QCOMPARE(idAt(context->lines(), 3), c);
    QCOMPARE(context->lines()->selectedCount(), 2);
    context->undo();
    QCOMPARE(idAt(context->lines(), 0), a);
    QCOMPARE(idAt(context->lines(), 1), b);
    QCOMPARE(context->lines()->selectedRows(), QVector<int>({1, 3}));
    context->moveSelectedDown();
    QCOMPARE(idAt(context->lines(), 2), b);
    QCOMPARE(idAt(context->lines(), 3), d);
    context->lines()->setActiveRow(0);
    context->moveSelectedUp();
    QCOMPARE(idAt(context->lines(), 0), a);
    context->lines()->setActiveRow(3);
    context->moveSelectedDown();
    QCOMPARE(idAt(context->lines(), 3), d);
}

void DocumentEditingTest::timingOperationsPreserveValidDurations()
{
    auto context = makeContext();
    context->lines()->selectRow(0);
    context->lines()->selectRow(1, true);
    context->shiftSelectedTiming(-1500);
    QVector<Event> events = eventsOf(context);
    QCOMPARE(events[0].startMs, qint64(0));
    QCOMPARE(events[0].endMs, qint64(1000));
    QCOMPARE(events[1].startMs, qint64(1500));
    QCOMPARE(events[1].endMs, qint64(2500));
    context->setSelectedTiming(750, 900);
    events = eventsOf(context);
    for (int row : {0, 1}) {
        QCOMPARE(events[row].startMs, qint64(750));
        QCOMPARE(events[row].endMs, qint64(900));
        QVERIFY(events[row].endMs >= events[row].startMs);
    }
    context->undo();
    QCOMPARE(eventsOf(context)[0].startMs, qint64(0));
}

void DocumentEditingTest::findWrapsAndReplaceAllIsOneUndo()
{
    auto context = makeContext();
    QVERIFY(context->findText(QStringLiteral("hello"), false, false));
    QCOMPARE(context->lines()->activeRow(), 3);
    QVERIFY(context->findText(QStringLiteral("Aello"), true, false));
    QCOMPARE(context->lines()->activeRow(), 0);
    QVERIFY(!context->findText(QStringLiteral("aello"), true, false));
    context->lines()->setActiveRow(3);
    QVERIFY(context->findText(QStringLiteral("Aello"), true, false));
    QCOMPARE(context->lines()->activeRow(), 0); // wrapped forward

    const int replacements = context->replaceAll(QStringLiteral("ell"), QStringLiteral("XX"), false);
    QCOMPARE(replacements, 2);
    QCOMPARE(eventsOf(context)[0].text, QStringLiteral("{\\pgrd(0,0,10,10,45)}AXXo"));
    QCOMPARE(eventsOf(context)[3].text, QStringLiteral("HXXo again"));
    context->undo();
    QCOMPARE(eventsOf(context)[0].text, QStringLiteral("{\\pgrd(0,0,10,10,45)}Aello"));
    QCOMPARE(eventsOf(context)[3].text, QStringLiteral("Hello again"));
}

void DocumentEditingTest::replaceCurrentAndNextUseLiteralMatches()
{
    auto context = makeContext();
    QVERIFY(context->findText(QStringLiteral("Aello"), true, false));
    QVERIFY(context->replaceCurrent(QStringLiteral("Aello"), QStringLiteral("Yello"), true));
    QCOMPARE(eventsOf(context)[0].text, QStringLiteral("{\\pgrd(0,0,10,10,45)}Yello"));
    QVERIFY(context->replaceNext(QStringLiteral("second"), QStringLiteral("changed"), true));
    QCOMPARE(eventsOf(context)[1].text, QStringLiteral("changed"));
    context->undo();
    QCOMPARE(eventsOf(context)[1].text, QStringLiteral("second"));
    context->undo();
    QCOMPARE(eventsOf(context)[0].text, QStringLiteral("{\\pgrd(0,0,10,10,45)}Aello"));
}

void DocumentEditingTest::editsKeepUnknownAssRecordsAndMangetsuText()
{
    auto context = makeContext();
    context->lines()->selectAll();
    context->joinSelected();
    const QByteArray serialized = context->rendererSnapshot();
    QVERIFY(serialized.contains("X-Preserved: keep this metadata"));
    QVERIFY(serialized.contains("[Before Unknown]"));
    QVERIFY(serialized.contains("Payload: leave untouched"));
    QVERIFY(serialized.contains("[Aegisub Extradata]"));
    QVERIFY(serialized.contains("Data: 1,key,still here"));
    QVERIFY(serialized.contains("[After Unknown]"));
    QVERIFY(serialized.contains("Payload: keep after event edits"));
    QVERIFY(serialized.contains("\\pgrd(0,0,10,10,45)"));
}

void DocumentEditingTest::textEditsMergePerLineAndSelectionChangesBreakTheMerge()
{
    auto context = makeContext();
    const QString original = context->activeText();
    context->setActiveText(QStringLiteral("typed"));
    context->setActiveText(QStringLiteral("typed more"));
    context->undo();
    QCOMPARE(context->activeText(), original);

    context->setActiveText(QStringLiteral("undo returns to edited line"));
    context->lines()->setActiveRow(1);
    context->undo();
    QCOMPARE(context->lines()->activeRow(), 0);
    QCOMPARE(context->activeText(), original);

    context->setActiveText(QStringLiteral("first edit"));
    context->lines()->setActiveRow(1);
    context->lines()->setActiveRow(0);
    context->setActiveText(QStringLiteral("second edit"));
    context->undo();
    QCOMPARE(context->activeText(), QStringLiteral("first edit"));
    context->undo();
    QCOMPARE(context->activeText(), original);
}

void DocumentEditingTest::undoRedoAreHardTextMergeBoundaries()
{
    auto context = makeContext();
    const QString original = context->activeText();
    context->setActiveText(QStringLiteral("a"));
    context->setActiveText(QStringLiteral("ab"));
    context->setActiveText(QStringLiteral("abc"));

    context->shiftSelectedTiming(100);
    QCOMPARE(eventsOf(context)[0].startMs, qint64(1100));
    context->undo();
    QCOMPARE(eventsOf(context)[0].startMs, qint64(1000));
    QCOMPARE(context->activeText(), QStringLiteral("abc"));

    context->setActiveText(QStringLiteral("abcd"));
    context->setActiveText(QStringLiteral("abcde"));
    context->undo();
    QCOMPARE(context->activeText(), QStringLiteral("abc"));
    context->undo();
    QCOMPARE(context->activeText(), original);

    auto redoContext = makeContext();
    const QString redoOriginal = redoContext->activeText();
    redoContext->setActiveText(QStringLiteral("a"));
    redoContext->setActiveText(QStringLiteral("ab"));
    redoContext->undo();
    QCOMPARE(redoContext->activeText(), redoOriginal);
    redoContext->redo();
    QCOMPARE(redoContext->activeText(), QStringLiteral("ab"));
    redoContext->setActiveText(QStringLiteral("abc"));
    redoContext->undo();
    QCOMPARE(redoContext->activeText(), QStringLiteral("ab"));
    redoContext->undo();
    QCOMPARE(redoContext->activeText(), redoOriginal);
}

void DocumentEditingTest::savedStateIsRestoredByUndoAndRedo()
{
    auto context = makeContext();
    QTemporaryDir directory;
    QVERIFY(directory.isValid());
    QSignalSpy saved(context.get(), &DocumentContext::saveFinished);
    context->save(QUrl::fromLocalFile(directory.filePath(QStringLiteral("saved.ass"))));
    QTRY_COMPARE_WITH_TIMEOUT(saved.count(), 1, 5000);
    QVERIFY(!context->modified());
    const QString originalText = context->activeText();
    context->setActiveText(originalText + QStringLiteral(" changed"));
    QVERIFY(context->modified());
    context->undo();
    QVERIFY(!context->modified());
    context->redo();
    QVERIFY(context->modified());
}

void DocumentEditingTest::visualDragCommitsOnceAndEscapeRestoresOriginal()
{
    auto context = makeContext();
    const QString original = context->activeText();
    QVERIFY(context->beginVisualTextEdit(context->lines()->activeId()));
    for (int i = 0; i < 100; ++i) {
        const QString preview = yoake::ass::VisualTags::setPoint(original, u"pos", QPointF(i * 4.0, i * 3.0));
        context->previewVisualTextEdit(preview);
    }
    context->previewVisualTextEdit(yoake::ass::VisualTags::setPoint(original, u"pos", QPointF(500, 400)));
    QCOMPARE(context->activeText(), QStringLiteral("{\\pgrd(0,0,10,10,45)\\pos(500,400)}Aello"));
    context->commitVisualTextEdit();
    QCOMPARE(context->undoText(), QStringLiteral("Visual subtitle edit"));
    context->undo();
    QCOMPARE(context->activeText(), original);
    context->redo();
    QVERIFY(context->activeText().contains(QStringLiteral("\\pos(500,400)")));

    const int undoBeforeCancel = context->undoText().size();
    QVERIFY(context->beginVisualTextEdit(context->lines()->activeId()));
    context->previewVisualTextEdit(QStringLiteral("changed during drag"));
    context->cancelVisualTextEdit();
    QVERIFY(context->activeText().contains(QStringLiteral("\\pos(500,400)")));
    QCOMPARE(context->undoText().size(), undoBeforeCancel);
}

void DocumentEditingTest::visualPositionUsesSharedTransformAfterNavigation()
{
    auto context = makeContext();
    const QString original = context->activeText();
    auto *viewport = context->videoViewport();
    viewport->setViewportSize(QSizeF(960, 540));
    viewport->setVideoSize(QSizeF(1920, 1080));
    const QPointF center(480, 270);
    viewport->zoomAt(center, 2.5);
    viewport->panBy(QPointF(72, -400));
    const QString afterNavigation = context->activeText();
    QCOMPARE(afterNavigation, original);
    const QPointF anchorScript(960, 1050); // alignment 2, event vertical margin 30
    const QPointF handle = viewport->scriptToScreen(anchorScript);
    context->visualTools()->pointerDown(handle.x(), handle.y(), Qt::LeftButton, 0);
    context->visualTools()->pointerMove(handle.x() + 30, handle.y() - 15, Qt::LeftButton, 0);
    context->visualTools()->pointerUp(handle.x() + 30, handle.y() - 15, Qt::LeftButton, 0);
    QCOMPARE(yoake::ass::VisualTags::point(context->activeText(), u"pos").value(), QPointF(984, 1038));
    QVERIFY(context->activeText().contains(QStringLiteral("\\pgrd(0,0,10,10,45)")));
    context->undo();
    QCOMPARE(context->activeText(), original);
}

void DocumentEditingTest::visualMoveEndpointUsesSharedTransformAndKeepsTimes()
{
    auto context = makeContext();
    context->setActiveText(QStringLiteral("{\\move(100,600,900,300,250,1250)}MOVE"));
    auto *viewport = context->videoViewport();
    viewport->setViewportSize(QSizeF(960, 540));
    viewport->setVideoSize(QSizeF(1920, 1080));
    viewport->zoomAt(QPointF(480, 270), 2.5);
    viewport->panBy(QPointF(72, 200));
    context->visualTools()->setActiveToolId(QStringLiteral("move"));

    const QPointF endScript(900, 300);
    const QPointF handle = viewport->scriptToScreen(endScript);
    QVERIFY(viewport->displayedVideoRect().contains(handle));
    context->visualTools()->pointerDown(handle.x(), handle.y(), Qt::LeftButton, 0);
    context->visualTools()->pointerMove(handle.x() + 30, handle.y() - 15, Qt::LeftButton, 0);
    context->visualTools()->pointerUp(handle.x() + 30, handle.y() - 15, Qt::LeftButton, 0);

    const auto move = yoake::ass::VisualTags::move(context->activeText());
    QVERIFY(move.has_value());
    QCOMPARE(move->end, QPointF(924, 288));
    QCOMPARE(move->startMs.value(), 250.0);
    QCOMPARE(move->endMs.value(), 1250.0);
    context->undo();
    QCOMPARE(context->activeText(), QStringLiteral("{\\move(100,600,900,300,250,1250)}MOVE"));
}

void DocumentEditingTest::visualRotationToolsEditTheirTags()
{
    auto context = makeContext();
    context->setActiveText(QStringLiteral("{\\pos(960,540)\\org(960,540)\\frz20\\frx10\\fry-5}ROTATE"));
    auto *viewport = context->videoViewport();
    viewport->setViewportSize(QSizeF(960, 540));
    viewport->setVideoSize(QSizeF(1920, 1080));
    viewport->zoomAt(QPointF(480, 270), 2.0);
    viewport->panBy(QPointF(40, 25));
    context->visualTools()->setActiveToolId(QStringLiteral("rotate-z"));

    const QPointF origin = viewport->scriptToScreen(QPointF(960, 540));
    const QPointF start = origin + QPointF(std::cos(qDegreesToRadians(20.0)) * 46.0,
                                           std::sin(qDegreesToRadians(20.0)) * 46.0);
    const QPointF end = origin + QPointF(std::cos(qDegreesToRadians(50.0)) * 46.0,
                                         std::sin(qDegreesToRadians(50.0)) * 46.0);
    context->visualTools()->pointerDown(start.x(), start.y(), Qt::LeftButton, 0);
    context->visualTools()->pointerMove(end.x(), end.y(), Qt::LeftButton, 0);
    context->visualTools()->pointerUp(end.x(), end.y(), Qt::LeftButton, 0);
    QVERIFY(std::abs(yoake::ass::VisualTags::number(context->activeText(), u"frz").value() - 50.0) < 0.01);
    context->undo();

    context->visualTools()->setActiveToolId(QStringLiteral("rotate-xy"));
    const QPointF center = viewport->scriptToScreen(QPointF(960, 540));
    const QPointF xHandle(center.x(), center.y() - 54);
    context->visualTools()->pointerDown(xHandle.x(), xHandle.y(), Qt::LeftButton, 0);
    context->visualTools()->pointerMove(xHandle.x(), xHandle.y() - 25, Qt::LeftButton, 0);
    context->visualTools()->pointerUp(xHandle.x(), xHandle.y() - 25, Qt::LeftButton, 0);
    QVERIFY(std::abs(yoake::ass::VisualTags::number(context->activeText(), u"frx").value() - 20.0) < 0.01);
    QCOMPARE(yoake::ass::VisualTags::number(context->activeText(), u"fry").value(), -5.0);
}

void DocumentEditingTest::visualScaleUsesScreenHandleAfterZoomAndPan()
{
    auto context = makeContext();
    context->setActiveText(QStringLiteral("Scale"));
    auto *viewport = context->videoViewport();
    viewport->setViewportSize(QSizeF(960, 540));
    viewport->setVideoSize(QSizeF(1920, 1080));
    viewport->zoomAt(QPointF(480, 270), 2.5);
    viewport->panBy(QPointF(72, -400));
    context->visualTools()->setActiveToolId(QStringLiteral("scale"));

    QPointF handle;
    bool found = false;
    for (const QVariant &entry : context->visualTools()->overlayFeatures()) {
        const QVariantMap feature = entry.toMap();
        if (feature.value(QStringLiteral("id")).toString() == QStringLiteral("scale-x")) {
            handle = QPointF(feature.value(QStringLiteral("x")).toReal(), feature.value(QStringLiteral("y")).toReal());
            found = true;
            break;
        }
    }
    QVERIFY(found);
    QVERIFY(viewport->displayedVideoRect().contains(handle));
    context->visualTools()->pointerDown(handle.x(), handle.y(), Qt::LeftButton, 0);
    context->visualTools()->pointerMove(handle.x() + 30, handle.y(), Qt::LeftButton, 0);
    context->visualTools()->pointerUp(handle.x() + 30, handle.y(), Qt::LeftButton, 0);
    QVERIFY(yoake::ass::VisualTags::number(context->activeText(), u"fscx").value() > 100.0);
    context->undo();
    QCOMPARE(context->activeText(), QStringLiteral("Scale"));
}

void DocumentEditingTest::visualRectClipResizeUsesSharedTransform()
{
    auto context = makeContext();
    context->setActiveText(QStringLiteral("{\\clip(100,100,500,300)}Clip"));
    auto *viewport = context->videoViewport();
    viewport->setViewportSize(QSizeF(960, 540));
    viewport->setVideoSize(QSizeF(1920, 1080));
    viewport->zoomAt(QPointF(480, 270), 2.5);
    viewport->panBy(QPointF(500, 350));
    context->visualTools()->setActiveToolId(QStringLiteral("clip"));

    const QPointF handle = viewport->scriptToScreen(QPointF(500, 300));
    context->visualTools()->pointerDown(handle.x(), handle.y(), Qt::LeftButton, 0);
    context->visualTools()->pointerMove(handle.x() + 30, handle.y() - 15, Qt::LeftButton, 0);
    context->visualTools()->pointerUp(handle.x() + 30, handle.y() - 15, Qt::LeftButton, 0);
    const QRectF resized = yoake::ass::VisualTags::clip(context->activeText())->bounds;
    QCOMPARE(resized.left(), 100.0);
    QCOMPARE(resized.top(), 100.0);
    QCOMPARE(resized.right(), 524.0);
    QCOMPARE(resized.bottom(), 288.0);
    context->undo();
    QCOMPARE(context->activeText(), QStringLiteral("{\\clip(100,100,500,300)}Clip"));
}

void DocumentEditingTest::visualScaleRectangleAcceptsReverseDrags()
{
    const QVector<QPair<QPointF, QPointF>> directions{
        {QPointF(200, 200), QPointF(600, 400)},
        {QPointF(600, 400), QPointF(200, 200)},
        {QPointF(600, 200), QPointF(200, 400)},
        {QPointF(200, 400), QPointF(600, 200)},
    };
    QVector<QPointF> scales;
    for (const auto &[start, end] : directions) {
        auto context = makeContext();
        context->setActiveText(QStringLiteral("Fit"));
        auto *viewport = context->videoViewport();
        viewport->setViewportSize(QSizeF(960, 540));
        viewport->setVideoSize(QSizeF(1920, 1080));
        context->visualTools()->setActiveToolId(QStringLiteral("scale"));
        context->visualTools()->setOption(QStringLiteral("fit-rect"));
        const QPointF a = viewport->scriptToScreen(start);
        const QPointF b = viewport->scriptToScreen(end);
        context->visualTools()->pointerDown(a.x(), a.y(), Qt::LeftButton, 0);
        context->visualTools()->pointerUp(b.x(), b.y(), Qt::LeftButton, 0);
        scales.push_back(QPointF(
            yoake::ass::VisualTags::number(context->activeText(), u"fscx").value(),
            yoake::ass::VisualTags::number(context->activeText(), u"fscy").value()));
    }
    for (const QPointF scale : scales) {
        QVERIFY(std::abs(scale.x() - scales.front().x()) < 0.01);
        QVERIFY(std::abs(scale.y() - scales.front().y()) < 0.01);
    }

    auto tiny = makeContext();
    tiny->setActiveText(QStringLiteral("Fit"));
    auto *viewport = tiny->videoViewport();
    viewport->setViewportSize(QSizeF(960, 540));
    viewport->setVideoSize(QSizeF(1920, 1080));
    tiny->visualTools()->setActiveToolId(QStringLiteral("scale"));
    tiny->visualTools()->setOption(QStringLiteral("fit-rect"));
    const QPointF point = viewport->scriptToScreen(QPointF(200, 200));
    tiny->visualTools()->pointerDown(point.x(), point.y(), Qt::LeftButton, 0);
    tiny->visualTools()->pointerUp(point.x() + 0.1, point.y() + 0.1, Qt::LeftButton, 0);
    QCOMPARE(tiny->activeText(), QStringLiteral("Fit"));
    QCOMPARE(yoake::ass::VisualTags::number(tiny->activeText(), u"fscx").has_value(), false);
}

void DocumentEditingTest::viewportNavigationDoesNotChangeDocumentBytes()
{
    auto context = makeContext();
    const QByteArray before = context->rendererSnapshot();
    auto *viewport = context->videoViewport();
    viewport->setViewportSize(QSizeF(960, 540));
    viewport->setVideoSize(QSizeF(1920, 1080));
    viewport->setScriptSize(QSizeF(1280, 720));
    viewport->zoomAt(QPointF(210, 180), 2.5);
    viewport->panBy(QPointF(175, -96));
    viewport->beginAnchoredZoom(QPointF(360, 250));
    viewport->updateAnchoredZoom(QPointF(390, 275), 1.4);
    viewport->endAnchoredZoom();
    viewport->setViewportSize(QSizeF(1080, 720));
    viewport->resetView();
    QCOMPARE(context->rendererSnapshot(), before);
    QCOMPARE(context->canUndo(), false);
}

void DocumentEditingTest::visualVectorClipNodeUsesSharedTransform()
{
    auto context = makeContext();
    context->setActiveText(QStringLiteral("{\\clip(m 100 100 l 500 100 500 300)}Vector"));
    auto *viewport = context->videoViewport();
    viewport->setViewportSize(QSizeF(960, 540));
    viewport->setVideoSize(QSizeF(1920, 1080));
    viewport->zoomAt(QPointF(480, 270), 2.5);
    viewport->panBy(QPointF(500, 350));
    context->visualTools()->setActiveToolId(QStringLiteral("vector-clip"));

    const QPointF handle = viewport->scriptToScreen(QPointF(500, 300));
    context->visualTools()->pointerDown(handle.x(), handle.y(), Qt::LeftButton, 0);
    context->visualTools()->pointerMove(handle.x() + 30, handle.y() - 15, Qt::LeftButton, 0);
    context->visualTools()->pointerUp(handle.x() + 30, handle.y() - 15, Qt::LeftButton, 0);
    QVERIFY(yoake::ass::VisualTags::clip(context->activeText())->path.contains(QStringLiteral("524 288")));
    context->undo();
    QCOMPARE(context->activeText(), QStringLiteral("{\\clip(m 100 100 l 500 100 500 300)}Vector"));
}

void DocumentEditingTest::visualBezierControlsAndEndpointDragIndependently()
{
    auto context = makeContext();
    const QString original = QStringLiteral("{\\clip(m 800 400 b 900 400 1000 500 1100 500)}Curve");
    context->setActiveText(original);
    auto *viewport = context->videoViewport();
    viewport->setViewportSize(QSizeF(960, 540));
    viewport->setVideoSize(QSizeF(1920, 1080));
    viewport->zoomAt(QPointF(480, 270), 2.5);
    viewport->panBy(QPointF(20, -10));
    context->visualTools()->setActiveToolId(QStringLiteral("vector-clip"));

    const auto featurePoint = [context](const QString &id) {
        for (const QVariant &entry : context->visualTools()->overlayFeatures()) {
            const QVariantMap feature = entry.toMap();
            if (feature.value(QStringLiteral("id")).toString() == id)
                return QPointF(feature.value(QStringLiteral("x")).toReal(), feature.value(QStringLiteral("y")).toReal());
        }
        return QPointF(-10000, -10000);
    };
    for (const auto &[nodeIndex, featureId, screenDelta] : QVector<std::tuple<int, QString, QPointF>>{
             {1, QStringLiteral("vector-1"), QPointF(20, -10)},
             {3, QStringLiteral("vector-3"), QPointF(-15, 10)}}) {
        const auto before = yoake::ass::VectorPath::parse(yoake::ass::VisualTags::clip(context->activeText())->path);
        const QPointF oldSelected = before->node(nodeIndex);
        const QPointF oldControlA = before->node(1);
        const QPointF oldControlB = before->node(2);
        const QPointF oldEndpoint = before->node(3);
        const QPointF handle = featurePoint(featureId);
        QVERIFY(handle.x() >= 0 && handle.y() >= 0);
        context->visualTools()->pointerDown(handle.x(), handle.y(), Qt::LeftButton, 0);
        context->visualTools()->pointerMove(handle.x() + screenDelta.x(), handle.y() + screenDelta.y(), Qt::LeftButton, 0);
        context->visualTools()->pointerUp(handle.x() + screenDelta.x(), handle.y() + screenDelta.y(), Qt::LeftButton, 0);
        const auto after = yoake::ass::VectorPath::parse(yoake::ass::VisualTags::clip(context->activeText())->path);
        QVERIFY(after.has_value());
        QVERIFY(after->node(nodeIndex) != oldSelected);
        if (nodeIndex != 1) QCOMPARE(after->node(1), oldControlA);
        if (nodeIndex != 2) QCOMPARE(after->node(2), oldControlB);
        if (nodeIndex != 3) QCOMPARE(after->node(3), oldEndpoint);
        context->undo();
        QCOMPARE(context->activeText(), original);
    }
}

void DocumentEditingTest::visualVectorSegmentsConvertAndInsertOnCurves()
{
    auto context = makeContext();
    context->setActiveText(QStringLiteral("{\\clip(m 800 400 l 1000 400 b 1020 400 1080 520 1100 540)}V"));
    auto *viewport = context->videoViewport();
    viewport->setViewportSize(QSizeF(960, 540));
    viewport->setVideoSize(QSizeF(1920, 1080));
    context->visualTools()->setActiveToolId(QStringLiteral("vector-clip"));

    context->visualTools()->pointerMove(450, 200, 0, 0); // Middle of the first line.
    context->visualTools()->setOption(QStringLiteral("line-to-bezier"));
    auto path = yoake::ass::VectorPath::parse(yoake::ass::VisualTags::clip(context->activeText())->path);
    QVERIFY(path && path->commands().at(1).kind == yoake::ass::VectorPath::Kind::Cubic);
    context->visualTools()->setOption(QStringLiteral("bezier-to-line"));
    path = yoake::ass::VectorPath::parse(yoake::ass::VisualTags::clip(context->activeText())->path);
    QVERIFY(path && path->commands().at(1).kind == yoake::ass::VectorPath::Kind::Line);

    context->visualTools()->setOption(QStringLiteral("insert"));
    const QPointF curveScript(1050, 462.5);
    const QPointF curveScreen = viewport->scriptToScreen(curveScript);
    context->visualTools()->pointerDown(curveScreen.x(), curveScreen.y(), Qt::LeftButton, 0);
    context->visualTools()->pointerUp(curveScreen.x(), curveScreen.y(), Qt::LeftButton, 0);
    path = yoake::ass::VectorPath::parse(yoake::ass::VisualTags::clip(context->activeText())->path);
    QVERIFY(path.has_value());
    QCOMPARE(path->commands().size(), 4);
    QVERIFY(path->commands().at(2).kind == yoake::ass::VectorPath::Kind::Cubic);
    QVERIFY(path->commands().at(3).kind == yoake::ass::VectorPath::Kind::Cubic);
}

void DocumentEditingTest::vectorNodesSupportControlToggleGroupDragAndSafeDelete()
{
    auto context = makeContext();
    const QString original = QStringLiteral("{\\clip(m 800 400 l 1000 400 l 1200 500)}Path");
    context->setActiveText(original);
    auto *viewport = context->videoViewport();
    viewport->setViewportSize(QSizeF(960, 540));
    viewport->setVideoSize(QSizeF(1920, 1080));
    context->visualTools()->setActiveToolId(QStringLiteral("vector-clip"));
    const auto featurePoint = [context](const QString &id) {
        for (const QVariant &entry : context->visualTools()->overlayFeatures()) {
            const QVariantMap feature = entry.toMap();
            if (feature.value(QStringLiteral("id")).toString() == id)
                return QPointF(feature.value(QStringLiteral("x")).toReal(), feature.value(QStringLiteral("y")).toReal());
        }
        return QPointF(-10000, -10000);
    };
    const QPointF first = featurePoint(QStringLiteral("vector-1"));
    const QPointF second = featurePoint(QStringLiteral("vector-2"));
    QVERIFY(first.x() >= 0 && second.x() >= 0);
    context->visualTools()->pointerDown(first.x(), first.y(), Qt::LeftButton, 0);
    context->visualTools()->pointerUp(first.x(), first.y(), Qt::LeftButton, 0);
    context->visualTools()->pointerDown(second.x(), second.y(), Qt::LeftButton, Qt::ControlModifier);
    context->visualTools()->pointerUp(second.x(), second.y(), Qt::LeftButton, Qt::ControlModifier);
    context->visualTools()->pointerDown(first.x(), first.y(), Qt::LeftButton, 0);
    context->visualTools()->pointerMove(first.x() + 10, first.y() + 10, Qt::LeftButton, 0);
    context->visualTools()->pointerUp(first.x() + 10, first.y() + 10, Qt::LeftButton, 0);
    auto path = yoake::ass::VectorPath::parse(yoake::ass::VisualTags::clip(context->activeText())->path);
    QVERIFY(path);
    QCOMPARE(path->node(0), QPointF(800, 400));
    QCOMPARE(path->node(1), QPointF(1020, 420));
    QCOMPARE(path->node(2), QPointF(1220, 520));
    context->visualTools()->keyDown(Qt::Key_Delete, 0);
    path = yoake::ass::VectorPath::parse(yoake::ass::VisualTags::clip(context->activeText())->path);
    QVERIFY(path);
    QCOMPARE(path->commands().size(), 1);
    context->undo();
    context->undo();
    QCOMPARE(context->activeText(), original);
}

void DocumentEditingTest::visualShiftMovesDefaultPositionAndSelectedOrigin()
{
    auto context = makeContext();
    context->setActiveText(QStringLiteral("{\\org(960,540)}SIGN"));
    auto *viewport = context->videoViewport();
    viewport->setViewportSize(QSizeF(960, 540));
    viewport->setVideoSize(QSizeF(1920, 1080));
    viewport->zoomAt(QPointF(480, 270), 2.5);
    viewport->panBy(QPointF(72, -400));
    context->visualTools()->setActiveToolId(QStringLiteral("shift"));

    const QPointF effectiveAnchor(960, 1050); // Fancy style's bottom-center anchor.
    const QPointF handle = viewport->scriptToScreen(effectiveAnchor);
    QVERIFY(viewport->displayedVideoRect().contains(handle));
    context->visualTools()->pointerDown(handle.x(), handle.y(), Qt::LeftButton, 0);
    context->visualTools()->pointerMove(handle.x() + 30, handle.y() - 15, Qt::LeftButton, 0);
    context->visualTools()->pointerUp(handle.x() + 30, handle.y() - 15, Qt::LeftButton, 0);

    QCOMPARE(yoake::ass::VisualTags::point(context->activeText(), u"pos").value(), QPointF(984, 1038));
    QCOMPARE(yoake::ass::VisualTags::point(context->activeText(), u"org").value(), QPointF(984, 528));
    context->undo();
    QCOMPARE(context->activeText(), QStringLiteral("{\\org(960,540)}SIGN"));
}

void DocumentEditingTest::visualShiftMovesSelectedEventsAsOneUndoableTransaction()
{
    auto context = makeContext();
    const QString first = QStringLiteral("{\\pos(200,200)\\move(10,20,30,40)\\org(5,6)\\clip(0,0,100,100)\\p1}m 0 0 l 10 10{\\p0}A");
    const QString second = QStringLiteral("{\\pos(400,300)\\move(110,120,130,140,0,1000)\\org(15,16)\\iclip(2,m 0 0 l 50 0 50 50)}B");
    context->lines()->setActiveRow(0);
    context->setActiveText(first);
    const QString firstId = idAt(context->lines(), 0);
    const QString secondId = idAt(context->lines(), 1);
    context->editEvent(QUuid(secondId), SubtitleModel::TextRole, second);
    context->lines()->selectRow(0);
    context->lines()->selectRow(1, false, true);
    const QString beforeFirst = context->lines()->eventAt(0)->text;
    const QString beforeSecond = context->lines()->eventAt(1)->text;

    auto *viewport = context->videoViewport();
    viewport->setViewportSize(QSizeF(960, 540));
    viewport->setVideoSize(QSizeF(1920, 1080));
    viewport->setScriptSize(QSizeF(1920, 1080));
    context->visualTools()->setActiveToolId(QStringLiteral("shift"));
    const QPointF start = viewport->scriptToScreen(QPointF(400, 300));
    const QPointF end = start + QPointF(15, -10); // 30,-20 script pixels at this fit scale.
    context->visualTools()->pointerDown(start.x(), start.y(), Qt::LeftButton, 0);
    for (int step = 1; step <= 20; ++step) {
        const QPointF point = start + (end - start) * (step / 20.0);
        context->visualTools()->pointerMove(point.x(), point.y(), Qt::LeftButton, 0);
    }
    context->visualTools()->pointerUp(end.x(), end.y(), Qt::LeftButton, 0);

    const QString afterFirst = context->lines()->eventAt(0)->text;
    const QString afterSecond = context->lines()->eventAt(1)->text;
    QCOMPARE(yoake::ass::VisualTags::point(afterFirst, u"pos").value(), QPointF(230, 180));
    QCOMPARE(yoake::ass::VisualTags::move(afterFirst)->start, QPointF(40, 0));
    QCOMPARE(yoake::ass::VisualTags::move(afterFirst)->end, QPointF(60, 20));
    QCOMPARE(yoake::ass::VisualTags::point(afterSecond, u"pos").value(), QPointF(430, 280));
    QCOMPARE(yoake::ass::VisualTags::move(afterSecond)->start, QPointF(140, 100));
    QCOMPARE(yoake::ass::VisualTags::move(afterSecond)->end, QPointF(160, 120));
    QCOMPARE(yoake::ass::VisualTags::move(afterSecond)->startMs.value(), 0.0);
    const auto shiftedClip = yoake::ass::VisualTags::clip(afterSecond);
    QVERIFY(shiftedClip && shiftedClip->pathEditable);
    QCOMPARE(yoake::ass::VectorPath::parse(shiftedClip->path, shiftedClip->drawingScale)->node(0), QPointF(15, -10));
    QCOMPARE(idAt(context->lines(), 0), firstId);
    QCOMPARE(idAt(context->lines(), 1), secondId);
    QCOMPARE(context->undoText(), QStringLiteral("Visual geometry shift"));

    context->undo();
    QCOMPARE(context->lines()->eventAt(0)->text, beforeFirst);
    QCOMPARE(context->lines()->eventAt(1)->text, beforeSecond);
    context->redo();
    QCOMPARE(context->lines()->eventAt(0)->text, afterFirst);
    QCOMPARE(context->lines()->eventAt(1)->text, afterSecond);

    const QString cancelUndoText = context->undoText();
    const QPointF cancelStart = viewport->scriptToScreen(QPointF(430, 280));
    const QPointF cancelEnd = cancelStart + QPointF(20, 15);
    context->visualTools()->pointerDown(cancelStart.x(), cancelStart.y(), Qt::LeftButton, 0);
    context->visualTools()->pointerMove(cancelEnd.x(), cancelEnd.y(), Qt::LeftButton, 0);
    context->visualTools()->cancelOperation();
    QCOMPARE(context->lines()->eventAt(0)->text, afterFirst);
    QCOMPARE(context->lines()->eventAt(1)->text, afterSecond);
    QCOMPARE(context->undoText(), cancelUndoText);
}

void DocumentEditingTest::customVisualToolFactoryOwnsDispatchOverlayAndLifecycle()
{
    auto context = makeContext();
    context->videoViewport()->setVideoSize(QSizeF(1920, 1080));
    context->videoViewport()->setViewportSize(QSizeF(960, 540));
    auto probe = std::make_shared<ToolProbe>();
    const QVariantMap descriptor{
        {QStringLiteral("id"), QStringLiteral("probe")},
        {QStringLiteral("name"), QStringLiteral("Probe")},
        {QStringLiteral("icon"), QStringLiteral("P")},
        {QStringLiteral("tooltip"), QStringLiteral("Tool factory test")},
        {QStringLiteral("commandId"), QStringLiteral("test.probe")},
        {QStringLiteral("factory"), QStringLiteral("ProbeTool")},
    };
    QVERIFY(context->visualTools()->registerToolFactory(descriptor,
        [probe](yoake::app::DocumentContext *, yoake::ui::VideoViewport *) {
            ++probe->factoryCalls;
            return std::make_shared<RecordingVisualTool>(probe);
        }));
    context->visualTools()->setActiveToolId(QStringLiteral("probe"));
    QCOMPARE(probe->factoryCalls, 1);
    QCOMPARE(probe->activations, 1);
    context->visualTools()->pointerDown(100, 100, Qt::LeftButton, 0);
    QCOMPARE(probe->pointerDowns, 1);
    QCOMPARE(context->visualTools()->overlayFeatures().front().toMap().value(QStringLiteral("id")).toString(),
        QStringLiteral("probe-overlay"));
    context->visualTools()->setActiveToolId(QStringLiteral("position"));
    QCOMPARE(probe->deactivations, 1);
}

void DocumentEditingTest::alignmentKeepsPositionedTextVisuallyStationary()
{
    auto unchanged = makeContext();
    const QString unchangedText = unchanged->activeText();
    unchanged->visualTools()->setActiveToolId(QStringLiteral("position"));
    unchanged->visualTools()->setOption(QStringLiteral("align-2"));
    QCOMPARE(unchanged->activeText(), unchangedText);
    QVERIFY(!unchanged->canUndo());

    const QVector<QPair<int, int>> transitions{{1, 9}, {2, 8}, {4, 6}};
    for (const auto &[from, to] : transitions) {
        auto context = makeContext();
        const QString original = QStringLiteral("{\\blur1\\an%1\\pos(500,500)\\org(470,470)}Text").arg(from);
        context->setActiveText(original);
        context->visualTools()->setActiveToolId(QStringLiteral("position"));
        const QPointF oldPosition = yoake::ass::VisualTags::point(context->activeText(), u"pos").value();
        const QPointF oldOrigin = yoake::ass::VisualTags::point(context->activeText(), u"org").value();
        context->visualTools()->setOption(QStringLiteral("align-%1").arg(to));
        const QPointF newPosition = yoake::ass::VisualTags::point(context->activeText(), u"pos").value();
        const QPointF newOrigin = yoake::ass::VisualTags::point(context->activeText(), u"org").value();
        const QPointF delta = newPosition - oldPosition;
        QCOMPARE(newOrigin - oldOrigin, delta);
        QVERIFY(context->activeText().contains(QStringLiteral("\\blur1")));
        QCOMPARE(yoake::ass::VisualTags::number(context->activeText(), u"an").value(), qreal(to));
        if (from == 1) { QVERIFY(delta.x() > 0); QVERIFY(delta.y() < 0); }
        if (from == 2) { QCOMPARE(delta.x(), 0.0); QVERIFY(delta.y() < 0); }
        if (from == 4) { QVERIFY(delta.x() > 0); QCOMPARE(delta.y(), 0.0); }
        context->undo();
        QCOMPARE(context->activeText(), original);
    }

    auto context = makeContext();
    const QString moving = QStringLiteral("{\\blur1\\an1\\move(100,500,100,600,200,1200)\\org(90,480)}Text");
    context->setActiveText(moving);
    context->visualTools()->setActiveToolId(QStringLiteral("position"));
    context->visualTools()->setOption(QStringLiteral("align-9"));
    const auto adjustedMove = yoake::ass::VisualTags::move(context->activeText());
    QVERIFY(adjustedMove.has_value());
    QCOMPARE(adjustedMove->startMs.value(), 200.0);
    QCOMPARE(adjustedMove->endMs.value(), 1200.0);
    QVERIFY(!yoake::ass::VisualTags::point(context->activeText(), u"pos"));
    QVERIFY(context->activeText().contains(QStringLiteral("\\blur1")));
}

void DocumentEditingTest::unsupportedVectorSyntaxCannotBeOverwrittenByVisualTools()
{
    auto context = makeContext();
    const QString original = QStringLiteral("{\\clip( 2, m 0 0 s 10 10 20 20 )\\blur2}Path");
    context->setActiveText(original);
    auto *viewport = context->videoViewport();
    viewport->setViewportSize(QSizeF(960, 540));
    viewport->setVideoSize(QSizeF(1920, 1080));
    context->visualTools()->setActiveToolId(QStringLiteral("vector-clip"));
    context->visualTools()->pointerDown(300, 200, Qt::LeftButton, 0);
    context->visualTools()->pointerMove(340, 220, Qt::LeftButton, 0);
    context->visualTools()->pointerUp(340, 220, Qt::LeftButton, 0);
    QCOMPARE(context->activeText(), original);

    context->visualTools()->setActiveToolId(QStringLiteral("clip"));
    context->visualTools()->pointerDown(300, 200, Qt::LeftButton, 0);
    context->visualTools()->pointerMove(360, 250, Qt::LeftButton, 0);
    context->visualTools()->pointerUp(360, 250, Qt::LeftButton, 0);
    QCOMPARE(context->activeText(), original);
    context->visualTools()->setOption(QStringLiteral("invert"));
    QVERIFY(context->activeText().contains(QStringLiteral("\\iclip( 2, m 0 0 s 10 10 20 20 )")));
    QVERIFY(context->activeText().contains(QStringLiteral("\\blur2")));
}

void DocumentEditingTest::visualClipCreationUsesSharedTransform()
{
    auto context = makeContext();
    context->setActiveText(QStringLiteral("Clip"));
    auto *viewport = context->videoViewport();
    viewport->setViewportSize(QSizeF(960, 540));
    viewport->setVideoSize(QSizeF(1920, 1080));
    viewport->zoomAt(QPointF(480, 270), 2.5);
    viewport->panBy(QPointF(500, 350));
    context->visualTools()->setActiveToolId(QStringLiteral("clip"));

    const QPointF start = viewport->scriptToScreen(QPointF(200, 200));
    const QPointF end = start + QPointF(30, 20);
    QVERIFY(viewport->displayedVideoRect().contains(start));
    context->visualTools()->pointerDown(start.x(), start.y(), Qt::LeftButton, 0);
    context->visualTools()->pointerMove(end.x(), end.y(), Qt::LeftButton, 0);
    context->visualTools()->pointerUp(end.x(), end.y(), Qt::LeftButton, 0);

    const auto clip = yoake::ass::VisualTags::clip(context->activeText());
    QVERIFY(clip.has_value() && clip->rectangle);
    QCOMPARE(clip->bounds, QRectF(200, 200, 24, 16));
    context->undo();
    QCOMPARE(context->activeText(), QStringLiteral("Clip"));
}

void DocumentEditingTest::visualDrawingNodeEditUsesLineAnchorAndUndo()
{
    auto context = makeContext();
    context->setActiveText(QStringLiteral("{\\p1}m 0 0 l 100 0{\\p0}"));
    const QString original = context->activeText();
    auto *viewport = context->videoViewport();
    viewport->setViewportSize(QSizeF(960, 540));
    viewport->setVideoSize(QSizeF(1920, 1080));
    context->visualTools()->setActiveToolId(QStringLiteral("drawing"));
    const QPointF scriptPoint(1060, 1050); // fallback line anchor + drawing node
    const QPointF handle = viewport->scriptToScreen(scriptPoint);
    context->visualTools()->pointerDown(handle.x(), handle.y(), Qt::LeftButton, 0);
    context->visualTools()->pointerMove(handle.x() + 10, handle.y(), Qt::LeftButton, 0);
    context->visualTools()->pointerUp(handle.x() + 10, handle.y(), Qt::LeftButton, 0);
    const auto drawing = yoake::ass::VisualTags::drawing(context->activeText());
    QVERIFY(drawing.has_value());
    QVERIFY(drawing->path.contains(QStringLiteral("l 120 0")));
    context->undo();
    QCOMPARE(context->activeText(), original);
}

QTEST_MAIN(DocumentEditingTest)
#include "document_editing_test.moc"
