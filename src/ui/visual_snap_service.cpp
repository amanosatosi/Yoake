#include "ui/visual_snap_service.h"

#include "ui/video_viewport.h"

#include <cmath>

namespace yoake::ui {

VisualSnapService::VisualSnapService(const VideoViewport *viewport, qreal tolerance)
    : m_viewport(viewport), m_tolerance(tolerance)
{
}

VisualSnapService::Result VisualSnapService::snap(const QPointF &sourcePoint, bool bypass) const
{
    Result result{sourcePoint, {}};
    if (bypass || !m_viewport || m_viewport->scriptSize().isEmpty())
        return result;

    const QSizeF size = m_viewport->scriptSize();
    const QPointF center(size.width() / 2.0, size.height() / 2.0);
    const auto consider = [this, &result](qreal &coordinate, qreal target, bool horizontal,
                                          const QPointF &otherCoordinate) {
        const QPointF source = horizontal ? QPointF(coordinate, otherCoordinate.y())
                                          : QPointF(otherCoordinate.x(), coordinate);
        const QPointF destination = horizontal ? QPointF(target, otherCoordinate.y())
                                               : QPointF(otherCoordinate.x(), target);
        const QPointF sourceScreen = m_viewport->scriptToScreen(source);
        const QPointF targetScreen = m_viewport->scriptToScreen(destination);
        const qreal delta = horizontal ? sourceScreen.x() - targetScreen.x()
                                       : sourceScreen.y() - targetScreen.y();
        if (std::abs(delta) <= m_tolerance) {
            coordinate = target;
            result.guides.push_back(QVariantMap{{QStringLiteral("axis"), horizontal ? QStringLiteral("x") : QStringLiteral("y")},
                {QStringLiteral("value"), target}});
        }
    };
    consider(result.point.rx(), center.x(), true, result.point);
    consider(result.point.ry(), center.y(), false, result.point);
    consider(result.point.rx(), 0.0, true, result.point);
    consider(result.point.rx(), size.width(), true, result.point);
    consider(result.point.ry(), 0.0, false, result.point);
    consider(result.point.ry(), size.height(), false, result.point);
    return result;
}

} // namespace yoake::ui
