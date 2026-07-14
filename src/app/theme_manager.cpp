#include "app/theme_manager.h"

#include <QtCore/QFile>
#include <QtCore/QJsonDocument>
#include <QtCore/QJsonObject>
#include <QtCore/QSettings>

namespace yoake::app {
namespace {

const QHash<QString, QString> themeResources = {
    {QStringLiteral("yoake-dawn"), QStringLiteral(":/yoake/themes/yoake-dawn.json")},
    {QStringLiteral("yoake-dusk"), QStringLiteral(":/yoake/themes/yoake-dusk.json")},
    {QStringLiteral("catppuccin-mocha"), QStringLiteral(":/yoake/themes/catppuccin-mocha.json")}
};

const QHash<QString, QColor> fallbackPalette = {
    {QStringLiteral("background"), QColor(QStringLiteral("#f5f2ec"))},
    {QStringLiteral("panel"), QColor(QStringLiteral("#fffdf9"))},
    {QStringLiteral("surface"), QColor(QStringLiteral("#ebe7df"))},
    {QStringLiteral("surfaceRaised"), QColor(QStringLiteral("#ffffff"))},
    {QStringLiteral("text"), QColor(QStringLiteral("#25221f"))},
    {QStringLiteral("textMuted"), QColor(QStringLiteral("#756f68"))},
    {QStringLiteral("textDisabled"), QColor(QStringLiteral("#aaa39a"))},
    {QStringLiteral("border"), QColor(QStringLiteral("#d2ccc2"))},
    {QStringLiteral("separator"), QColor(QStringLiteral("#e1dcd4"))},
    {QStringLiteral("accent"), QColor(QStringLiteral("#d65c45"))},
    {QStringLiteral("selection"), QColor(QStringLiteral("#f4c9ba"))},
    {QStringLiteral("hover"), QColor(QStringLiteral("#eee3da"))},
    {QStringLiteral("pressed"), QColor(QStringLiteral("#dfd2c7"))},
    {QStringLiteral("active"), QColor(QStringLiteral("#fff1eb"))},
    {QStringLiteral("inactive"), QColor(QStringLiteral("#e8e3db"))},
    {QStringLiteral("tab"), QColor(QStringLiteral("#e9e4dc"))},
    {QStringLiteral("tabActive"), QColor(QStringLiteral("#fffdf9"))},
    {QStringLiteral("gridAlternate"), QColor(QStringLiteral("#faf7f2"))},
    {QStringLiteral("gridComment"), QColor(QStringLiteral("#8b776f"))},
    {QStringLiteral("waveform"), QColor(QStringLiteral("#4c8b8a"))},
    {QStringLiteral("waveformBackground"), QColor(QStringLiteral("#17202a"))},
    {QStringLiteral("timingRegion"), QColor(QStringLiteral("#3b8f6b80"))},
    {QStringLiteral("timingSelected"), QColor(QStringLiteral("#e9a23b99"))},
    {QStringLiteral("timingBoundary"), QColor(QStringLiteral("#ffd166"))},
    {QStringLiteral("videoOverlay"), QColor(QStringLiteral("#111111a0"))},
    {QStringLiteral("error"), QColor(QStringLiteral("#c43d4b"))},
    {QStringLiteral("warning"), QColor(QStringLiteral("#c47a2c"))},
    {QStringLiteral("success"), QColor(QStringLiteral("#398562"))},
    {QStringLiteral("syntaxBrace"), QColor(QStringLiteral("#9c4dcc"))},
    {QStringLiteral("syntaxTag"), QColor(QStringLiteral("#b34b35"))},
    {QStringLiteral("syntaxParameter"), QColor(QStringLiteral("#246b8f"))},
    {QStringLiteral("syntaxNumber"), QColor(QStringLiteral("#7b5aae"))},
    {QStringLiteral("syntaxColor"), QColor(QStringLiteral("#d14d72"))},
    {QStringLiteral("syntaxAlpha"), QColor(QStringLiteral("#b45f93"))},
    {QStringLiteral("syntaxTransform"), QColor(QStringLiteral("#a15c14"))},
    {QStringLiteral("syntaxKaraoke"), QColor(QStringLiteral("#16825f"))},
    {QStringLiteral("syntaxDrawing"), QColor(QStringLiteral("#287d8e"))},
    {QStringLiteral("syntaxLineBreak"), QColor(QStringLiteral("#c25779"))},
    {QStringLiteral("syntaxExtension"), QColor(QStringLiteral("#705cc5"))},
    {QStringLiteral("syntaxError"), QColor(QStringLiteral("#d1223e"))}
};

} // namespace

ThemeManager::ThemeManager(QObject *parent) : QObject(parent)
{
    QSettings settings;
    const QString saved = settings.value(QStringLiteral("appearance/theme"),
        QStringLiteral("yoake-dusk")).toString();
    if (!loadTheme(saved))
        loadTheme(QStringLiteral("yoake-dusk"));
}

QStringList ThemeManager::availableThemes() const
{
    return {QStringLiteral("yoake-dawn"), QStringLiteral("yoake-dusk"),
        QStringLiteral("catppuccin-mocha")};
}

QVariantMap ThemeManager::palette() const
{
    QVariantMap result;
    for (auto it = m_colors.cbegin(); it != m_colors.cend(); ++it)
        result.insert(it.key(), it.value());
    return result;
}

QColor ThemeManager::color(QStringView semanticName) const
{
    return m_colors.value(semanticName.toString(), fallbackPalette.value(semanticName.toString(), Qt::magenta));
}

void ThemeManager::setCurrentTheme(const QString &themeId)
{
    if (themeId == m_currentTheme || !loadTheme(themeId))
        return;
    QSettings().setValue(QStringLiteral("appearance/theme"), themeId);
    emit currentThemeChanged();
    emit paletteChanged();
}

bool ThemeManager::loadTheme(const QString &themeId)
{
    const QString path = themeResources.value(themeId);
    if (path.isEmpty())
        return false;
    QFile file(path);
    if (!file.open(QIODevice::ReadOnly))
        return false;
    const QJsonDocument document = QJsonDocument::fromJson(file.readAll());
    if (!document.isObject())
        return false;

    QHash<QString, QColor> colors = fallbackPalette;
    const QJsonObject object = document.object().value(QStringLiteral("colors")).toObject();
    for (auto it = object.begin(); it != object.end(); ++it) {
        const QColor color(it.value().toString());
        if (color.isValid())
            colors.insert(it.key(), color);
    }
    m_colors = std::move(colors);
    m_currentTheme = themeId;
    return true;
}

} // namespace yoake::app
