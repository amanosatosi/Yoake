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
#include "ui/spectrum_item.h"
#include "ui/video_frame_item.h"

#include <ffms.h>

#include <QtCore/QDateTime>
#include <QtCore/QDebug>
#include <QtCore/QDir>
#include <QtCore/QFile>
#include <QtCore/QStandardPaths>
#include <QtCore/QTextStream>
#include <QtCore/QTimer>
#include <QtGui/QGuiApplication>
#include <QtGui/QWindow>
#include <QtQml/QQmlApplicationEngine>
#include <QtQml/QQmlContext>
#include <QtQml/QQmlError>
#include <QtQuickControls2/QQuickStyle>

#if defined(Q_OS_WIN)
#include <windows.h>
#endif

namespace {

QString startupLogPath()
{
    const QString overridePath = qEnvironmentVariable("YOAKE_STARTUP_LOG");
    if (!overridePath.isEmpty())
        return overridePath;
    const QString directory = QStandardPaths::writableLocation(QStandardPaths::AppLocalDataLocation);
    QDir().mkpath(directory);
    return QDir(directory).filePath(QStringLiteral("yoake-startup.log"));
}

void writeStartupLog(const QString &summary, const QStringList &details = {})
{
    QFile file(startupLogPath());
    if (!file.open(QIODevice::WriteOnly | QIODevice::Truncate | QIODevice::Text))
        return;
    QTextStream stream(&file);
    stream << "Yoake startup diagnostic\n"
           << "Timestamp: " << QDateTime::currentDateTimeUtc().toString(Qt::ISODate) << " UTC\n"
           << "Executable: " << QCoreApplication::applicationFilePath() << '\n'
           << "Working directory: " << QDir::currentPath() << '\n'
           << "Qt: " << qVersion() << "\n\n"
           << summary << '\n';
    for (const QString &detail : details)
        stream << detail << '\n';
}

void reportStartupFailure(const QString &summary, const QStringList &details, bool showDialog)
{
    writeStartupLog(summary, details);
    QString message = summary;
    if (!details.isEmpty())
        message += QStringLiteral("\n\n") + details.join(QLatin1Char('\n'));
    message += QStringLiteral("\n\nDiagnostic log:\n") + startupLogPath();
    qCritical().noquote() << message;
#if defined(Q_OS_WIN)
    if (showDialog) {
        const QString title = QStringLiteral("Yoake could not start");
        MessageBoxW(nullptr, reinterpret_cast<LPCWSTR>(message.utf16()),
            reinterpret_cast<LPCWSTR>(title.utf16()), MB_OK | MB_ICONERROR);
    }
#else
    Q_UNUSED(showDialog);
#endif
}

} // namespace

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
    bool uiSmokeTest = false;
    for (int index = 1; index < argc; ++index) {
        const QString argument = QString::fromLocal8Bit(argv[index]);
        smokeTest = smokeTest || argument == QStringLiteral("--smoke-test");
        uiSmokeTest = uiSmokeTest || argument == QStringLiteral("--ui-smoke-test");
    }
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
    qmlRegisterType<yoake::ui::SpectrumItem>("Yoake", 1, 0, "SpectrumView");
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
    QStringList qmlWarnings;
    QObject::connect(&engine, &QQmlEngine::warnings, &engine,
        [&qmlWarnings](const QList<QQmlError> &warnings) {
            for (const QQmlError &warning : warnings)
                qmlWarnings.push_back(warning.toString());
        });
    engine.loadFromModule(QStringLiteral("Yoake"), QStringLiteral("Main"));

    if (engine.rootObjects().isEmpty()) {
        reportStartupFailure(QStringLiteral("The main QML window could not be created."),
            qmlWarnings, !uiSmokeTest);
        return EXIT_FAILURE;
    }

    if (uiSmokeTest) {
        QTimer::singleShot(250, &application, [&application, &engine, &qmlWarnings] {
            QWindow *window = qobject_cast<QWindow *>(engine.rootObjects().constFirst());
            if (!window || !window->isVisible()) {
                reportStartupFailure(QStringLiteral("The QML root object did not produce a visible window."),
                    qmlWarnings, false);
                application.exit(EXIT_FAILURE);
                return;
            }
            writeStartupLog(QStringLiteral("The packaged QML window was created successfully."), qmlWarnings);
            window->setVisible(false);
            application.exit(EXIT_SUCCESS);
        });
    } else {
        writeStartupLog(QStringLiteral("The QML window was created successfully."), qmlWarnings);
    }
    return application.exec();
}
