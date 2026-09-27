#pragma once

#include "ass/ass_event.h"
#include "ass/ass_document.h"
#include "ui/visual_snap_service.h"
#include "ui/visual_tool.h"

#include <QtCore/QPointer>
#include <QtCore/QRectF>
#include <QtCore/QSizeF>
#include <QtCore/QStringList>
#include <QtCore/QSet>

#include <utility>

namespace yoake::app { class DocumentContext; }

namespace yoake::ui {

// Shared document/viewport services for built-in tools. Editing policy and
// interaction state belong to the concrete tool, while this base keeps the
// common coordinate, overlay, snapping, and DocumentContext transaction code.
class VisualToolBase : public VisualTool {
public:
    VisualToolBase(app::DocumentContext *document, VideoViewport *viewport);
    ~VisualToolBase() override = default;

    [[nodiscard]] QVariantList renderOverlay() const final;
    [[nodiscard]] QString coordinateLabel() const override { return m_coordinateLabel; }
    void activeLineChanged() override;
    void deactivate() override;
    void commitOperation() override;
    void cancelOperation() override;

protected:
    virtual void buildOverlay(QVariantList &features) const = 0;
    void setCoordinateLabel(QString label) const { m_coordinateLabel = std::move(label); }
    [[nodiscard]] app::DocumentContext *document() const { return m_document.data(); }
    [[nodiscard]] VideoViewport *viewport() const { return m_viewport.data(); }
    [[nodiscard]] QString activeText() const;
    [[nodiscard]] ass::Event activeEvent() const;
    [[nodiscard]] QPointF toScript(QPointF screen) const;
    [[nodiscard]] QPointF toScreen(QPointF script) const;
    [[nodiscard]] QPointF currentPointer() const { return m_currentPointer; }
    void setCurrentPointer(QPointF point) { m_currentPointer = point; }
    [[nodiscard]] QPointF effectivePosition(const ass::Event &event) const;
    [[nodiscard]] QSizeF estimateSubtitleBounds(const QString &text, const ass::Document::Style &style,
                                                qreal scaleX = 100.0, qreal scaleY = 100.0) const;
    [[nodiscard]] QPointF snap(QPointF point, int modifiers) const;

    void addPoint(QVariantList &features, QString id, QPointF scriptPoint,
                  QString label = {}, QString role = {}, bool selected = false,
                  QString nodeType = {}) const;
    void addScreenPoint(QVariantList &features, QString id, QPointF screenPoint,
                        QString label = {}, QString role = {}, bool selected = false,
                        QString nodeType = {}) const;
    void addLine(QVariantList &features, QString id, QPointF start, QPointF end,
                 QString role = {}) const;
    void addScreenLine(QVariantList &features, QString id, QPointF start, QPointF end,
                       QString role = {}) const;
    void addRect(QVariantList &features, QString id, QRectF rect, QString role = {}) const;
    [[nodiscard]] QVariantMap feature(const QVariantList &features, const QString &id) const;
    [[nodiscard]] QString hitPoint(const QVariantList &features, QPointF screenPoint,
                                   qreal tolerance = 13.0) const;
    [[nodiscard]] qreal hitLine(const QVariantList &features, QPointF screenPoint,
                                QString *featureId = nullptr, qreal tolerance = 9.0) const;
    [[nodiscard]] QVariantList addSnapGuides(QVariantList features) const;
    void clearSnapGuides() const { m_guides.clear(); }

    bool beginTextEdit();
    bool beginTextEditGroup(const QStringList &eventIds);
    void previewTextEdit(const QString &text);
    void previewTextEditGroup(const QVariantMap &textsByEventId);
    void editTextOnce(const QString &text);
    [[nodiscard]] bool hasTextEdit() const { return m_editStarted; }

    mutable QVariantList m_guides;

private:
    QPointer<app::DocumentContext> m_document;
    QPointer<VideoViewport> m_viewport;
    VisualSnapService m_snapService;
    mutable QString m_coordinateLabel;
    QPointF m_currentPointer{-1000, -1000};
    bool m_editStarted = false;
    QString m_editActiveId;
};

} // namespace yoake::ui
