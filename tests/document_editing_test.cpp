#include "app/document_context.h"
#include "ass/ass_document.h"
#include "models/subtitle_model.h"

#include <QtCore/QTemporaryDir>
#include <QtTest/QSignalSpy>
#include <QtTest/QTest>
#include <QtGui/QClipboard>
#include <QtGui/QGuiApplication>

#include <memory>

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

QTEST_MAIN(DocumentEditingTest)
#include "document_editing_test.moc"
