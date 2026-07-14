#include "ui/ass_highlighter.h"

#include "app/theme_manager.h"

#include <QtCore/QRegularExpression>
#include <QtGui/QTextDocument>

namespace yoake::ui {
namespace {

enum class TagKind { Standard, Transform, Karaoke, Mangetsu };

TagKind tagKind(QStringView tag)
{
    QString normalized = tag.toString().toLower();
    while (!normalized.isEmpty() && normalized.front().isDigit())
        normalized.removeFirst();
    if (normalized == QStringLiteral("t"))
        return TagKind::Transform;
    if (normalized == QStringLiteral("k") || normalized == QStringLiteral("kf")
        || normalized == QStringLiteral("ko") || normalized == QStringLiteral("kt"))
        return TagKind::Karaoke;

    static const QStringList mangetsuTags = {
        QStringLiteral("vc"), QStringLiteral("va"), QStringLiteral("grd"),
        QStringLiteral("gra"), QStringLiteral("bgrd"), QStringLiteral("bga"),
        QStringLiteral("pgrd"), QStringLiteral("img"), QStringLiteral("bs"),
        QStringLiteral("bsx"), QStringLiteral("bsy"), QStringLiteral("bc"),
        QStringLiteral("ba"), QStringLiteral("bvc"), QStringLiteral("bva")
    };
    if (mangetsuTags.contains(normalized))
        return TagKind::Mangetsu;
    return TagKind::Standard;
}

} // namespace

AssHighlighter::AssHighlighter(QObject *parent) : QSyntaxHighlighter(parent) { }

void AssHighlighter::setTextDocument(QQuickTextDocument *document)
{
    if (m_quickDocument == document)
        return;
    m_quickDocument = document;
    QSyntaxHighlighter::setDocument(document ? document->textDocument() : nullptr);
    emit textDocumentChanged();
}

void AssHighlighter::setTheme(app::ThemeManager *theme)
{
    if (m_theme == theme)
        return;
    disconnect(m_themeConnection);
    m_theme = theme;
    if (m_theme) {
        m_themeConnection = connect(m_theme, &app::ThemeManager::paletteChanged,
            this, [this] { rehighlight(); });
    }
    emit themeChanged();
    rehighlight();
}

QTextCharFormat AssHighlighter::format(QStringView semanticColor, bool bold) const
{
    QTextCharFormat result;
    if (m_theme)
        result.setForeground(m_theme->color(semanticColor));
    if (bold)
        result.setFontWeight(QFont::DemiBold);
    return result;
}

void AssHighlighter::highlightBlock(const QString &text)
{
    const auto lineBreakFormat = format(QStringLiteral("syntaxLineBreak"), true);
    static const QRegularExpression lineBreak(QStringLiteral(R"(\\[Nnh])"));
    auto breaks = lineBreak.globalMatch(text);
    while (breaks.hasNext()) {
        const auto match = breaks.next();
        setFormat(match.capturedStart(), match.capturedLength(), lineBreakFormat);
    }

    int cursor = 0;
    bool drawingMode = false;
    while (cursor < text.size()) {
        const int open = text.indexOf(u'{', cursor);
        if (open < 0) {
            if (drawingMode)
                highlightDrawing(text, cursor, text.size());
            break;
        }
        if (drawingMode)
            highlightDrawing(text, cursor, open);
        const int close = text.indexOf(u'}', open + 1);
        if (close < 0) {
            setFormat(open, text.size() - open, format(QStringLiteral("syntaxError")));
            break;
        }
        setFormat(open, 1, format(QStringLiteral("syntaxBrace"), true));
        setFormat(close, 1, format(QStringLiteral("syntaxBrace"), true));
        highlightOverride(text, open + 1, close);

        static const QRegularExpression drawingTag(QStringLiteral(R"(\\p([+-]?\d+))"),
            QRegularExpression::CaseInsensitiveOption);
        auto drawingTags = drawingTag.globalMatch(text, open + 1);
        while (drawingTags.hasNext()) {
            const auto match = drawingTags.next();
            if (match.capturedStart() >= close)
                break;
            drawingMode = match.captured(1).toInt() > 0;
        }
        cursor = close + 1;
    }
}

void AssHighlighter::highlightOverride(const QString &text, int start, int end)
{
    int cursor = start;
    while (cursor < end) {
        if (text[cursor] == u'{') {
            setFormat(cursor, 1, format(QStringLiteral("syntaxError"), true));
            ++cursor;
            continue;
        }
        if (text[cursor] != u'\\') {
            ++cursor;
            continue;
        }
        setFormat(cursor, 1, format(QStringLiteral("syntaxTag"), true));
        const int nameStart = ++cursor;
        if (cursor < end && text[cursor].isDigit())
            ++cursor;
        while (cursor < end && (text[cursor].isLetter() || text[cursor] == u'_'))
            ++cursor;
        const QStringView name = QStringView(text).mid(nameStart, cursor - nameStart);
        QString semantic = QStringLiteral("syntaxTag");
        switch (tagKind(name)) {
        case TagKind::Transform: semantic = QStringLiteral("syntaxTransform"); break;
        case TagKind::Karaoke: semantic = QStringLiteral("syntaxKaraoke"); break;
        case TagKind::Mangetsu: semantic = QStringLiteral("syntaxExtension"); break;
        case TagKind::Standard: break;
        }
        setFormat(nameStart, cursor - nameStart, format(semantic, true));

        const int parameterStart = cursor;
        while (cursor < end && text[cursor] != u'\\')
            ++cursor;
        if (cursor > parameterStart)
            setFormat(parameterStart, cursor - parameterStart, format(QStringLiteral("syntaxParameter")));

        static const QRegularExpression number(QStringLiteral(R"((?<![A-Za-z])[+-]?(?:\d+(?:\.\d*)?|\.\d+)%?)"));
        auto numbers = number.globalMatch(text, parameterStart);
        while (numbers.hasNext()) {
            const auto match = numbers.next();
            if (match.capturedStart() >= cursor)
                break;
            setFormat(match.capturedStart(), match.capturedLength(), format(QStringLiteral("syntaxNumber")));
        }
        static const QRegularExpression color(
            QStringLiteral(R"(&H([0-9A-Fa-f]{8}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{2})&?)"));
        auto colors = color.globalMatch(text, parameterStart, QRegularExpression::NormalMatch,
            QRegularExpression::DontCheckSubjectStringMatchOption);
        while (colors.hasNext()) {
            const auto match = colors.next();
            if (match.capturedStart() >= cursor)
                break;
            setFormat(match.capturedStart(), match.capturedLength(),
                format(match.capturedLength(1) == 2
                    ? QStringLiteral("syntaxAlpha") : QStringLiteral("syntaxColor")));
        }
    }
}

void AssHighlighter::highlightDrawing(const QString &text, int start, int end)
{
    static const QRegularExpression drawing(QStringLiteral(R"((?<!\w)[mnlbspc]|[+-]?(?:\d+(?:\.\d*)?|\.\d+))"),
        QRegularExpression::CaseInsensitiveOption);
    auto matches = drawing.globalMatch(text, start);
    while (matches.hasNext()) {
        const auto match = matches.next();
        if (match.capturedStart() >= end)
            break;
        setFormat(match.capturedStart(), match.capturedLength(), format(QStringLiteral("syntaxDrawing")));
    }
}

} // namespace yoake::ui
