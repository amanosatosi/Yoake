#pragma once

#include <QtCore/QObject>
#include <QtCore/QStringList>
#include <QtCore/QVariantMap>
#include <QtGui/QColor>

namespace yoake::app {

class ThemeManager final : public QObject {
    Q_OBJECT
    Q_PROPERTY(QString currentTheme READ currentTheme WRITE setCurrentTheme NOTIFY currentThemeChanged)
    Q_PROPERTY(QStringList availableThemes READ availableThemes CONSTANT)
    Q_PROPERTY(QVariantMap palette READ palette NOTIFY paletteChanged)

public:
    explicit ThemeManager(QObject *parent = nullptr);

    [[nodiscard]] QString currentTheme() const { return m_currentTheme; }
    [[nodiscard]] QStringList availableThemes() const;
    [[nodiscard]] QVariantMap palette() const;
    [[nodiscard]] QColor color(QStringView semanticName) const;

    void setCurrentTheme(const QString &themeId);

signals:
    void currentThemeChanged();
    void paletteChanged();

private:
    bool loadTheme(const QString &themeId);

    QString m_currentTheme;
    QHash<QString, QColor> m_colors;
};

} // namespace yoake::app
