#include "renderer/mangetsu_library.h"

#include <QtCore/QCoreApplication>
#include <QtCore/QDir>
#include <QtCore/QFileInfo>
#include <QtCore/QMutexLocker>

namespace yoake::renderer {

MangetsuLibrary &MangetsuLibrary::instance()
{
    static MangetsuLibrary library;
    return library;
}

const abi::Api *MangetsuLibrary::api(QString *error)
{
    QMutexLocker lock(&m_mutex);
    if (!m_attempted) {
        m_attempted = true;
        load();
    }
    if (!m_error.isEmpty()) {
        if (error)
            *error = m_error;
        return nullptr;
    }
    if (error)
        error->clear();
    return &m_api;
}

QString MangetsuLibrary::loadedPath() const
{
    QMutexLocker lock(&m_mutex);
    return m_loadedPath;
}

template<typename Function>
bool MangetsuLibrary::resolve(const char *name, Function *target)
{
    const QFunctionPointer symbol = m_library.resolve(name);
    if (!symbol) {
        m_error = QStringLiteral("Mangetsu is missing required symbol %1: %2")
            .arg(QString::fromLatin1(name), m_library.errorString());
        return false;
    }
    *target = reinterpret_cast<Function>(symbol);
    return true;
}

bool MangetsuLibrary::load()
{
#if defined(Q_OS_WIN)
    const QString fileName = QStringLiteral("mangetsu.dll");
#elif defined(Q_OS_MACOS)
    const QString fileName = QStringLiteral("libmangetsu.dylib");
#else
    const QString fileName = QStringLiteral("libmangetsu.so");
#endif
    QString path = qEnvironmentVariable("YOAKE_MANGETSU_LIBRARY");
    if (!path.isEmpty() && !QFileInfo(path).isAbsolute()) {
        m_error = QStringLiteral("YOAKE_MANGETSU_LIBRARY must be an absolute path");
        return false;
    }
    if (path.isEmpty())
        path = QDir(QCoreApplication::applicationDirPath()).absoluteFilePath(fileName);

    m_library.setFileName(path);
    m_library.setLoadHints(QLibrary::ResolveAllSymbolsHint);
    if (!m_library.load()) {
        m_error = QStringLiteral("Mangetsu is unavailable at %1: %2")
            .arg(QDir::toNativeSeparators(path), m_library.errorString());
        return false;
    }
    m_loadedPath = QFileInfo(path).absoluteFilePath();

    if (!resolve("ass_library_init", &m_api.libraryInit)
        || !resolve("ass_library_done", &m_api.libraryDone)
        || !resolve("ass_set_message_cb", &m_api.setMessageCallback)
        || !resolve("ass_renderer_init", &m_api.rendererInit)
        || !resolve("ass_renderer_done", &m_api.rendererDone)
        || !resolve("ass_set_font_scale", &m_api.setFontScale)
        || !resolve("ass_set_fonts", &m_api.setFonts)
        || !resolve("ass_read_memory", &m_api.readMemory)
        || !resolve("ass_free_track", &m_api.freeTrack)
        || !resolve("ass_set_frame_size", &m_api.setFrameSize)
        || !resolve("ass_set_storage_size", &m_api.setStorageSize)
        || !resolve("ass_render_frame_auto", &m_api.renderFrameAuto)
        || !resolve("ass_free_images_rgba", &m_api.freeImagesRgba)) {
        m_library.unload();
        return false;
    }
    return true;
}

} // namespace yoake::renderer
