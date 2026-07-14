#include "app/document_manager.h"
#include "app/document_context.h"
#include "app/theme_manager.h"
#include "ass/ass_text_boundaries.h"
#include "media/media_session.h"
#include "media/waveform_model.h"
#include "models/subtitle_model.h"
#include "renderer/mangetsu_library.h"
#include "timing/karaoke_session.h"
#include "ui/ass_highlighter.h"
#include "ui/subtitle_overlay_item.h"
#include "ui/video_frame_item.h"

#include <ffms.h>

#include <QtCore/QDebug>
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

#if defined(Q_OS_WIN)
    // Qt Multimedia is retained only for its PCM sink. Yoake never asks its
    // FFmpeg plugin to decode or seek media.
    qputenv("QT_MEDIA_BACKEND", "windows");
#endif
    bool smokeTest = false;
    for (int index = 1; index < argc; ++index)
        smokeTest = smokeTest || QString::fromLocal8Bit(argv[index]) == QStringLiteral("--smoke-test");
    QGuiApplication application(argc, argv);
    if (smokeTest) {
        if (FFMS_GetVersion() <= 0) {
            qCritical() << "FFMS2 runtime did not initialize";
            return EXIT_FAILURE;
        }
        QString rendererError;
        if (!yoake::renderer::MangetsuLibrary::instance().api(&rendererError)) {
            qCritical().noquote() << rendererError;
            return EXIT_FAILURE;
        }
        qInfo() << "Yoake native runtime smoke test passed; FFMS2 version" << FFMS_GetVersion();
        return EXIT_SUCCESS;
    }
    QQuickStyle::setStyle(QStringLiteral("Basic"));

    qmlRegisterType<yoake::ui::AssHighlighter>("Yoake", 1, 0, "AssHighlighter");
    qmlRegisterType<yoake::ui::SubtitleOverlayItem>("Yoake", 1, 0, "SubtitleOverlay");
    qmlRegisterType<yoake::ui::VideoFrameItem>("Yoake", 1, 0, "VideoFrame");
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
