import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

Rectangle {
    id: root
    required property var context
    color: Theme.palette.waveformBackground
    border.color: Theme.palette.border
    property real dragStartMs: -1
    property real dragCurrentMs: -1
    property int dragBoundaryIndex: -1

    function timeAt(x) {
        return Math.max(0, Math.min(context.media.durationMs, x / Math.max(1, waveform.width) * context.media.durationMs))
    }

    ColumnLayout {
        anchors.fill: parent
        spacing: 0
        RowLayout {
            Layout.fillWidth: true
            Layout.leftMargin: 4
            Layout.rightMargin: 4
            ToolButton {
                text: qsTr("K-Timing")
                checkable: true
                checked: root.context.karaoke.active
                onToggled: {
                    if (checked !== root.context.karaoke.active) {
                        if (checked)
                            root.context.karaoke.beginOriginal()
                        else
                            root.context.karaoke.cancel()
                    }
                }
            }
            ComboBox {
                enabled: root.context.karaoke.active
                model: ["\\k", "\\K", "\\kf", "\\ko"]
                currentIndex: Math.max(0, model.indexOf(root.context.karaoke.tagType))
                onActivated: root.context.karaoke.tagType = currentText
                ToolTip.visible: hovered
                ToolTip.text: qsTr("Karaoke tag type")
            }
            ToolButton {
                text: "▶"
                enabled: root.context.karaoke.active
                onClicked: root.context.karaoke.playSelected()
                ToolTip.visible: hovered
                ToolTip.text: qsTr("Audition selected syllable (S)")
            }
            ToolButton {
                text: "✓"
                enabled: root.context.karaoke.active && root.context.karaoke.dirty
                onClicked: root.context.karaoke.commit()
                palette.buttonText: Theme.palette.success
                ToolTip.visible: hovered
                ToolTip.text: qsTr("Commit K-Timing")
            }
            ToolButton {
                text: "↶"
                enabled: root.context.karaoke.active
                onClicked: root.context.karaoke.beginOriginal()
                palette.buttonText: Theme.palette.error
                ToolTip.visible: hovered
                ToolTip.text: qsTr("Reset K-Timing")
            }
            ListView {
                Layout.fillWidth: true
                Layout.preferredHeight: 32
                orientation: ListView.Horizontal
                spacing: 3
                clip: true
                model: root.context.karaoke.active ? root.context.karaoke : null
                delegate: Button {
                    id: syllableButton
                    required property int index
                    required property string syllableLabel
                    required property bool syllableSelected
                    height: 30
                    text: syllableLabel.length ? syllableLabel : "∅"
                    highlighted: syllableSelected
                    onClicked: root.context.karaoke.selectedIndex = index
                }
            }
        }
        Canvas {
            id: waveform
            Layout.fillWidth: true
            Layout.fillHeight: true
            onPaint: {
                const painter = getContext("2d")
                painter.reset()
                painter.fillStyle = Theme.palette.waveformBackground
                painter.fillRect(0, 0, width, height)
                const duration = Math.max(1, root.context.media.durationMs)

                const startX = root.context.activeStartMs / duration * width
                const endX = root.context.activeEndMs / duration * width
                painter.fillStyle = Theme.palette.timingRegion
                painter.fillRect(startX, 0, Math.max(1, endX - startX), height)

                if (root.dragStartMs >= 0) {
                    const x1 = Math.min(root.dragStartMs, root.dragCurrentMs) / duration * width
                    const x2 = Math.max(root.dragStartMs, root.dragCurrentMs) / duration * width
                    painter.fillStyle = Theme.palette.timingSelected
                    painter.fillRect(x1, 0, Math.max(1, x2 - x1), height)
                }

                const karaoke = root.context.karaoke
                if (karaoke.active && karaoke.selectedIndex >= 0) {
                    const selectedStart = karaoke.slotStartMs(karaoke.selectedIndex) / duration * width
                    const selectedEnd = karaoke.slotEndMs(karaoke.selectedIndex) / duration * width
                    painter.fillStyle = Theme.palette.timingSelected
                    painter.fillRect(selectedStart, 0, Math.max(1, selectedEnd - selectedStart), height)
                }

                const model = root.context.media.waveform
                const count = model.count
                if (count > 0) {
                    painter.strokeStyle = Theme.palette.waveform
                    painter.lineWidth = 1
                    painter.beginPath()
                    const step = Math.max(1, Math.ceil(count / Math.max(1, width)))
                    for (let index = 0; index < count; index += step) {
                        const peak = model.sample(index)
                        const x = index / Math.max(1, count - 1) * width
                        painter.moveTo(x, (1 - peak.y) * height / 2)
                        painter.lineTo(x, (1 - peak.x) * height / 2)
                    }
                    painter.stroke()
                }
                if (karaoke.active) {
                    painter.strokeStyle = Theme.palette.timingBoundary
                    painter.lineWidth = 2
                    for (let boundary = 0; boundary + 1 < karaoke.count; ++boundary) {
                        const boundaryX = karaoke.boundaryMs(boundary) / duration * width
                        painter.beginPath()
                        painter.moveTo(boundaryX, 0)
                        painter.lineTo(boundaryX, height)
                        painter.stroke()
                    }
                }
                const playX = root.context.media.positionMs / duration * width
                painter.strokeStyle = Theme.palette.timingBoundary
                painter.lineWidth = 2
                painter.beginPath(); painter.moveTo(playX, 0); painter.lineTo(playX, height); painter.stroke()
            }
            MouseArea {
                anchors.fill: parent
                onPressed: mouse => {
                    if (root.context.karaoke.active) {
                        const time = root.timeAt(mouse.x)
                        const tolerance = Math.max(10, root.context.media.durationMs * 8 / Math.max(1, waveform.width))
                        root.dragBoundaryIndex = root.context.karaoke.nearestBoundary(time, tolerance)
                        if (root.dragBoundaryIndex < 0) {
                            root.context.karaoke.selectAtTime(time)
                            root.context.media.seek(time)
                        }
                        waveform.requestPaint()
                        return
                    }
                    root.dragStartMs = root.timeAt(mouse.x)
                    root.dragCurrentMs = root.dragStartMs
                    waveform.requestPaint()
                }
                onPositionChanged: mouse => {
                    if (pressed && root.context.karaoke.active && root.dragBoundaryIndex >= 0) {
                        root.context.karaoke.moveBoundary(root.dragBoundaryIndex, root.timeAt(mouse.x))
                        waveform.requestPaint()
                        return
                    }
                    if (pressed) {
                        root.dragCurrentMs = root.timeAt(mouse.x)
                        waveform.requestPaint()
                    }
                }
                onReleased: mouse => {
                    if (root.context.karaoke.active) {
                        if (root.dragBoundaryIndex >= 0)
                            root.context.karaoke.moveBoundary(root.dragBoundaryIndex, root.timeAt(mouse.x))
                        root.dragBoundaryIndex = -1
                        waveform.requestPaint()
                        return
                    }
                    root.dragCurrentMs = root.timeAt(mouse.x)
                    if (Math.abs(root.dragCurrentMs - root.dragStartMs) > 30)
                        root.context.setSelectedTiming(Math.min(root.dragStartMs, root.dragCurrentMs), Math.max(root.dragStartMs, root.dragCurrentMs))
                    else
                        root.context.media.seek(root.dragCurrentMs)
                    root.dragStartMs = -1
                    root.dragCurrentMs = -1
                    waveform.requestPaint()
                }
            }
            Connections {
                target: root.context.media.waveform
                function onCountChanged() { waveform.requestPaint() }
                function onBusyChanged() { waveform.requestPaint() }
            }
            Connections {
                target: root.context.media
                function onPositionChanged() { waveform.requestPaint() }
                function onDurationChanged() { waveform.requestPaint() }
            }
            Connections {
                target: root.context
                function onActiveLineChanged() { waveform.requestPaint() }
            }
            Connections {
                target: root.context.karaoke
                function onBoundariesChanged() { waveform.requestPaint() }
                function onSelectedIndexChanged() { waveform.requestPaint() }
                function onActiveChanged() { waveform.requestPaint() }
            }
            Connections {
                target: Theme
                function onPaletteChanged() { waveform.requestPaint() }
            }
        }
        RowLayout {
            Layout.fillWidth: true
            Layout.leftMargin: 6
            Layout.rightMargin: 6
            Label {
                text: root.context.media.waveform.busy ? qsTr("Generating waveform…")
                      : root.context.media.waveform.errorString
                color: root.context.media.waveform.errorString ? Theme.palette.warning : Theme.palette.textMuted
            }
            Item { Layout.fillWidth: true }
            Label {
                text: root.context.karaoke.active
                      ? qsTr("Drag syllable boundaries; S auditions")
                      : qsTr("Drag to time the active line")
                color: Theme.palette.textMuted
            }
        }
    }

    Shortcut {
        sequence: "S"
        enabled: root.visible && root.context.karaoke.active
        onActivated: root.context.karaoke.playSelected()
    }
    Shortcut {
        sequence: "G"
        enabled: root.visible && root.context.karaoke.active && root.context.karaoke.dirty
        onActivated: root.context.karaoke.commit()
    }
}
