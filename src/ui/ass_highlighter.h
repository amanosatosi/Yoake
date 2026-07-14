#pragma once

#include "app/theme_manager.h"

#include <QtCore/QPointer>
#include <QtGui/QSyntaxHighlighter>
#include <QtQuick/QQuickTextDocument>

namespace yoake::ui {

class AssHighlighter final : public QSyntaxHighlighter {
    Q_OBJECT
    Q_PROPERTY(QQuickTextDocument *textDocument READ textDocument WRITE setTextDocument NOTIFY textDocumentChanged)
    Q_PROPERTY(yoake::app::ThemeManager *theme READ theme WRITE setTheme NOTIFY themeChanged)

public:
    explicit AssHighlighter(QObject *parent = nullptr);

    [[nodiscard]] QQuickTextDocument *textDocument() const { return m_quickDocument; }
    [[nodiscard]] app::ThemeManager *theme() const { return m_theme; }
    void setTextDocument(QQuickTextDocument *document);
    void setTheme(app::ThemeManager *theme);

signals:
    void textDocumentChanged();
    void themeChanged();

protected:
    void highlightBlock(const QString &text) override;

private:
    QTextCharFormat format(QStringView semanticColor, bool bold = false) const;
    void highlightOverride(const QString &text, int start, int end);
    void highlightDrawing(const QString &text, int start, int end);

    QPointer<QQuickTextDocument> m_quickDocument;
    QPointer<app::ThemeManager> m_theme;
    QMetaObject::Connection m_themeConnection;
};

} // namespace yoake::ui
