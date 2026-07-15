#pragma once

#include <QtCore/QMetaObject>
#include <QtCore/QPointer>
#include <QtCore/QTimer>
#include <QtGui/QColor>
#include <QtGui/QImage>
#include <QtQuick/QQuickItem>

namespace yoake::media { class MediaSession; }

namespace yoake::ui {

class SpectrumItem final : public QQuickItem {
    Q_OBJECT
    Q_PROPERTY(yoake::media::MediaSession *session READ session WRITE setSession NOTIFY sessionChanged)
    Q_PROPERTY(qint64 startMs READ startMs WRITE setStartMs NOTIFY startMsChanged)
    Q_PROPERTY(qint64 endMs READ endMs WRITE setEndMs NOTIFY endMsChanged)
    Q_PROPERTY(QColor lowColor READ lowColor WRITE setLowColor NOTIFY colorsChanged)
    Q_PROPERTY(QColor midColor READ midColor WRITE setMidColor NOTIFY colorsChanged)
    Q_PROPERTY(QColor highColor READ highColor WRITE setHighColor NOTIFY colorsChanged)
    Q_PROPERTY(bool active READ active WRITE setActive NOTIFY activeChanged)

public:
    explicit SpectrumItem(QQuickItem *parent = nullptr);

    [[nodiscard]] media::MediaSession *session() const { return m_session.data(); }
    [[nodiscard]] qint64 startMs() const { return m_startMs; }
    [[nodiscard]] qint64 endMs() const { return m_endMs; }
    [[nodiscard]] QColor lowColor() const { return m_lowColor; }
    [[nodiscard]] QColor midColor() const { return m_midColor; }
    [[nodiscard]] QColor highColor() const { return m_highColor; }
    [[nodiscard]] bool active() const { return m_active; }

    void setSession(media::MediaSession *session);
    void setStartMs(qint64 value);
    void setEndMs(qint64 value);
    void setLowColor(const QColor &value);
    void setMidColor(const QColor &value);
    void setHighColor(const QColor &value);
    void setActive(bool value);

signals:
    void sessionChanged();
    void startMsChanged();
    void endMsChanged();
    void colorsChanged();
    void activeChanged();

protected:
    QSGNode *updatePaintNode(QSGNode *oldNode, UpdatePaintNodeData *) override;
    void geometryChange(const QRectF &newGeometry, const QRectF &oldGeometry) override;

private slots:
    void requestNow();
    void refreshImage();

private:
    void scheduleRequest();

    QPointer<media::MediaSession> m_session;
    QMetaObject::Connection m_imageConnection;
    QMetaObject::Connection m_metadataConnection;
    QMetaObject::Connection m_destroyedConnection;
    QTimer m_requestTimer;
    QImage m_image;
    qint64 m_startMs = 0;
    qint64 m_endMs = 1;
    QColor m_lowColor{QStringLiteral("#0b1020")};
    QColor m_midColor{QStringLiteral("#166f87")};
    QColor m_highColor{QStringLiteral("#fff08a")};
    bool m_active = false;
    bool m_textureDirty = true;
};

} // namespace yoake::ui
