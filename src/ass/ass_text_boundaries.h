#pragma once

#include <QtCore/QObject>
#include <QtCore/QVariantMap>

namespace yoake::ass {

class TextBoundaries final : public QObject {
    Q_OBJECT

public:
    explicit TextBoundaries(QObject *parent = nullptr) : QObject(parent) { }

    Q_INVOKABLE QVariantMap selectionRange(const QString &text, int position) const;

    static QPair<int, int> rangeAt(QStringView text, int position);
};

} // namespace yoake::ass
