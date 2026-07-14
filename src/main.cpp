#include "app/document_manager.h"
#include "app/document_context.h"
#include "app/theme_manager.h"
#include "ass/ass_text_boundaries.h"
#include "media/media_session.h"
#include "media/waveform_model.h"
#include "models/subtitle_model.h"
#include "timing/karaoke_session.h"
#include "ui/ass_highlighter.h"
#include "ui/subtitle_overlay_item.h"

#include <QtGui/QGuiApplication>
#include <QtQml/QQmlApplicationEngine>
#include <QtQml/QQmlContext>
#include <QtQuickControls2/QQuickStyle>

int main(int argc, char *argv[])
{
    QCoreApplication::setOrganizationName(QStringLiteral("Yoake Project"));
    QCoreApplication::setOrganizationDomain(QStringLiteral("yoake.app"));
    QCoreApplication::setApplicationName(QStringLiteral("Yoake"));
    QCoreApplication::setApplicationVersion(QStringLiteral("0.1.0"));

    QGuiApplication application(argc, argv);
    QQuickStyle::setStyle(QStringLiteral("Basic"));

    qmlRegisterType<yoake::ui::AssHighlighter>("Yoake", 1, 0, "AssHighlighter");
    qmlRegisterType<yoake::ui::SubtitleOverlayItem>("Yoake", 1, 0, "SubtitleOverlay");
    qmlRegisterUncreatableType<yoake::app::DocumentContext>("Yoake", 1, 0,
        "DocumentContext", QStringLiteral("Document contexts are owned by DocumentManager"));
    qmlRegisterUncreatableType<yoake::models::SubtitleModel>("Yoake", 1, 0,
        "SubtitleModel", QStringLiteral("Subtitle models are owned by DocumentContext"));
    qmlRegisterUncreatableType<yoake::media::MediaSession>("Yoake", 1, 0,
        "MediaSession", QStringLiteral("Media sessions are owned by DocumentContext"));
    qmlRegisterUncreatableType<yoake::media::WaveformModel>("Yoake", 1, 0,
        "WaveformModel", QStringLiteral("Waveform models are owned by MediaSession"));
    qmlRegisterUncreatableType<yoake::timing::KaraokeSession>("Yoake", 1, 0,
        "KaraokeSession", QStringLiteral("Karaoke sessions are owned by DocumentContext"));

    yoake::app::DocumentManager documents;
    yoake::app::ThemeManager theme;
    yoake::ass::TextBoundaries textBoundaries;

    QQmlApplicationEngine engine;
    engine.rootContext()->setContextProperty(QStringLiteral("Documents"), &documents);
    engine.rootContext()->setContextProperty(QStringLiteral("Theme"), &theme);
    engine.rootContext()->setContextProperty(QStringLiteral("AssBoundaries"), &textBoundaries);
    QObject::connect(&engine, &QQmlApplicationEngine::objectCreationFailed,
        &application, [] { QCoreApplication::exit(EXIT_FAILURE); }, Qt::QueuedConnection);
    engine.loadFromModule(QStringLiteral("Yoake"), QStringLiteral("Main"));
    return application.exec();
}
