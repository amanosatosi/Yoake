#pragma once

#include "renderer/mangetsu_abi.h"

#include <QtCore/QLibrary>
#include <QtCore/QMutex>
#include <QtCore/QString>

namespace yoake::renderer {

class MangetsuLibrary final {
public:
    static MangetsuLibrary &instance();

    [[nodiscard]] const abi::Api *api(QString *error = nullptr);
    [[nodiscard]] QString loadedPath() const;

private:
    MangetsuLibrary() = default;
    bool load();

    template<typename Function>
    bool resolve(const char *name, Function *target);

    mutable QMutex m_mutex;
    QLibrary m_library;
    abi::Api m_api;
    QString m_error;
    QString m_loadedPath;
    bool m_attempted = false;
};

} // namespace yoake::renderer
