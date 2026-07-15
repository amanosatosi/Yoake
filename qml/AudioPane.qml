import QtQuick
import QtQuick.Controls
import QtQuick.Layouts
import Yoake

Rectangle {
    id: root
    required property var context
    color: Theme.palette.waveformBackground
    border.color: Theme.palette.border
    property real dragStartMs: -1
    property real dragCurrentMs: -1
    property int dragBoundaryIndex: -1
    property real viewStartMs: 0
    property real viewEndMs: Math.max(1, context.media.durationMs)
    property bool fittedInitialLine: false
    property int visualizationMode: 0 // 0 = waveform, 1 = spectrum

    function repaint() {
        waveformCanvas.requestPaint()
        timingOverlay.requestPaint()
    }

    function setView(startMs, endMs) {
        const duration = Math.max(1, context.media.durationMs)
        const span = Math.min(duration, Math.max(80, endMs - startMs))
        viewStartMs = Math.max(0, Math.min(duration - span, startMs))
        viewEndMs = viewStartMs + span
        repaint()
    }

    function fitMedia() {
        setView(0, Math.max(1, context.media.durationMs))
    }

    function fitActiveLine() {
        const padding = Math.max(1000, context.activeEndMs - context.activeStartMs)
        setView(context.activeStartMs - padding, context.activeEndMs + padding)
    }

    function ensureActiveLineVisible() {
        if (context.media.durationMs <= 0)
            return
        if (context.activeStartMs < viewStartMs || context.activeEndMs > viewEndMs)
            fitActiveLine()
        else
            repaint()
    }

    function timeAt(x) {
        return Math.max(viewStartMs,
            Math.min(viewEndMs, viewStartMs
                + x / Math.max(1, timingSurface.width) * (viewEndMs - viewStartMs)))
    }

    function xAt(timeMs) {
        return (timeMs - viewStartMs) / Math.max(1, viewEndMs - viewStartMs) * timingSurface.width
    }

    function frequencyY(frequency) {
        const maximum = Math.min(20000, Math.max(100, context.media.audioSampleRate / 2))
        const minimum = Math.min(45, maximum * 0.25)
        const normalized = Math.max(0, Math.min(1, (frequency - minimum) / Math.max(1, maximum - minimum)))
        const position = Math.pow(normalized, 1 / 2.4)
        return (1 - position) * timingSurface.height
    }

    ColumnLayout {
        anchors.fill: parent
        spacing: 0

        RowLayout {
            Layout.fillWidth: true
            Layout.leftMargin: 4
            Layout.rightMargin: 4

            ToolButton {
                text: root.context.media.playing ? qsTr("Pause") : qsTr("Play line")
                enabled: root.context.media.hasAudio
                onClicked: root.context.media.playing
                           ? root.context.media.pause()
                           : root.context.media.playRange(root.context.activeStartMs, root.context.activeEndMs)
                ToolTip.visible: hovered
                ToolTip.text: qsTr("Play the active timing range")
            }
            ToolButton {
                text: qsTr("Stop")
                enabled: root.context.media.playing
                onClicked: root.context.media.stop()
            }
            ToolSeparator { }
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
                text: "S"
                enabled: root.context.karaoke.active && root.context.media.hasAudio
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
                text: "↺"
                enabled: root.context.karaoke.active
                onClicked: root.context.karaoke.beginOriginal()
                palette.buttonText: Theme.palette.error
                ToolTip.visible: hovered
                ToolTip.text: qsTr("Reset K-Timing")
            }
            ToolSeparator { }
            ComboBox {
                id: displayMode
                model: [qsTr("Waveform"), qsTr("Spectrum")]
                currentIndex: root.visualizationMode
                onActivated: {
                    root.visualizationMode = currentIndex
                    root.repaint()
                }
                ToolTip.visible: hovered
                ToolTip.text: qsTr("Audio visualization mode")
            }
            ToolButton {
                text: qsTr("Line")
                enabled: root.context.media.durationMs > 0
                onClicked: root.fitActiveLine()
                ToolTip.visible: hovered
                ToolTip.text: qsTr("Zoom the audio view to the active subtitle line")
            }
            ToolButton {
                text: qsTr("Fit")
                enabled: root.context.media.durationMs > 0
                onClicked: root.fitMedia()
                ToolTip.visible: hovered
                ToolTip.text: qsTr("Show the entire indexed audio source")
            }
            ListView {
                Layout.fillWidth: true
                Layout.preferredHeight: 32
                orientation: ListView.Horizontal
                spacing: 3
                clip: true
                model: root.context.karaoke.active ? root.context.karaoke : null
                delegate: Button {
                    required property int index
                    required property string syllableLabel
                    required property bool syllableSelected
                    height: 30
                    text: syllableLabel.length ? syllableLabel : "·"
                    highlighted: syllableSelected
                    onClicked: root.context.karaoke.selectedIndex = index
                }
            }
        }

        Item {
            id: timingSurface
            Layout.fillWidth: true
            Layout.fillHeight: true
            clip: true

            Rectangle {
                anchors.fill: parent
                color: Theme.palette.waveformBackground
            }

            Canvas {
                id: waveformCanvas
                anchors.fill: parent
                visible: root.visualizationMode === 0

                onPaint: {
                    const painter = getContext("2d")
                    painter.reset()
                    painter.clearRect(0, 0, width, height)
                    const peaks = root.context.media.waveform.samplesForRange(
                        root.viewStartMs, root.viewEndMs, Math.max(1, Math.round(width)))
                    if (peaks.length === 0)
                        return
                    painter.strokeStyle = Theme.palette.waveform
                    painter.lineWidth = 1
                    painter.beginPath()
                    for (let index = 0; index < peaks.length; ++index) {
                        const peak = peaks[index]
                        const x = index / Math.max(1, peaks.length - 1) * width
                        painter.moveTo(x, (1 - peak.y) * height / 2)
                        painter.lineTo(x, (1 - peak.x) * height / 2)
                    }
                    painter.stroke()
                }
            }

            SpectrumView {
                id: spectrumView
                anchors.fill: parent
                visible: root.visualizationMode === 1
                active: visible
                session: root.context.media
                startMs: Math.round(root.viewStartMs)
                endMs: Math.round(root.viewEndMs)
                lowColor: Theme.palette.spectrumLow
                midColor: Theme.palette.spectrumMid
                highColor: Theme.palette.spectrumHigh
            }

            Canvas {
                id: timingOverlay
                anchors.fill: parent

                onPaint: {
                    const painter = getContext("2d")
                    painter.reset()
                    painter.clearRect(0, 0, width, height)

                    const startX = root.xAt(root.context.activeStartMs)
                    const endX = root.xAt(root.context.activeEndMs)
                    painter.fillStyle = Theme.palette.timingRegion
                    painter.fillRect(Math.max(0, startX), 0,
                        Math.max(0, Math.min(width, endX) - Math.max(0, startX)), height)

                    if (root.dragStartMs >= 0) {
                        const x1 = root.xAt(Math.min(root.dragStartMs, root.dragCurrentMs))
                        const x2 = root.xAt(Math.max(root.dragStartMs, root.dragCurrentMs))
                        painter.fillStyle = Theme.palette.timingSelected
                        painter.fillRect(x1, 0, Math.max(1, x2 - x1), height)
                    }

                    const karaoke = root.context.karaoke
                    if (karaoke.active && karaoke.selectedIndex >= 0) {
                        const selectedStart = root.xAt(karaoke.slotStartMs(karaoke.selectedIndex))
                        const selectedEnd = root.xAt(karaoke.slotEndMs(karaoke.selectedIndex))
                        painter.fillStyle = Theme.palette.timingSelected
                        painter.fillRect(selectedStart, 0, Math.max(1, selectedEnd - selectedStart), height)
                    }

                    painter.strokeStyle = Theme.palette.border
                    painter.fillStyle = Theme.palette.textMuted
                    painter.lineWidth = 1
                    painter.font = "10px sans-serif"
                    for (let tick = 0; tick <= 8; ++tick) {
                        const tickX = tick * width / 8
                        const tickMs = root.viewStartMs + tick / 8 * (root.viewEndMs - root.viewStartMs)
                        painter.beginPath()
                        painter.moveTo(tickX, 0)
                        painter.lineTo(tickX, height)
                        painter.stroke()
                        painter.fillText((tickMs / 1000).toFixed(2), tickX + 2, 11)
                    }

                    if (root.visualizationMode === 1) {
                        const guides = [200, 1000, 4000, 12000]
                        const maximum = root.context.media.audioSampleRate / 2
                        for (let guide = 0; guide < guides.length; ++guide) {
                            if (guides[guide] >= maximum)
                                continue
                            const guideY = root.frequencyY(guides[guide])
                            painter.beginPath()
                            painter.moveTo(0, guideY)
                            painter.lineTo(width, guideY)
                            painter.stroke()
                            const label = guides[guide] >= 1000
                                ? (guides[guide] / 1000) + " kHz" : guides[guide] + " Hz"
                            painter.fillText(label, Math.max(2, width - 42), Math.max(11, guideY - 2))
                        }
                    }

                    if (karaoke.active) {
                        painter.strokeStyle = Theme.palette.timingBoundary
                        painter.lineWidth = 2
                        for (let boundary = 0; boundary + 1 < karaoke.count; ++boundary) {
                            const boundaryX = root.xAt(karaoke.boundaryMs(boundary))
                            if (boundaryX < 0 || boundaryX > width)
                                continue
                            painter.beginPath()
                            painter.moveTo(boundaryX, 0)
                            painter.lineTo(boundaryX, height)
                            painter.stroke()
                        }
                    }

                    const playX = root.xAt(root.context.media.positionMs)
                    if (playX >= 0 && playX <= width) {
                        painter.strokeStyle = Theme.palette.timingBoundary
                        painter.lineWidth = 2
                        painter.beginPath()
                        painter.moveTo(playX, 0)
                        painter.lineTo(playX, height)
                        painter.stroke()
                    }
                }
            }

            MouseArea {
                anchors.fill: parent
                onPressed: mouse => {
                    if (root.context.karaoke.active) {
                        const time = root.timeAt(mouse.x)
                        const tolerance = Math.max(10,
                            (root.viewEndMs - root.viewStartMs) * 8 / Math.max(1, timingSurface.width))
                        root.dragBoundaryIndex = root.context.karaoke.nearestBoundary(time, tolerance)
                        if (root.dragBoundaryIndex < 0) {
                            root.context.karaoke.selectAtTime(time)
                            root.context.media.seek(time)
                        }
                        root.repaint()
                        return
                    }
                    root.dragStartMs = root.timeAt(mouse.x)
                    root.dragCurrentMs = root.dragStartMs
                    root.repaint()
                }
                onPositionChanged: mouse => {
                    if (pressed && root.context.karaoke.active && root.dragBoundaryIndex >= 0) {
                        root.context.karaoke.moveBoundary(root.dragBoundaryIndex, root.timeAt(mouse.x))
                        root.repaint()
                        return
                    }
                    if (pressed) {
                        root.dragCurrentMs = root.timeAt(mouse.x)
                        root.repaint()
                    }
                }
                onReleased: mouse => {
                    if (root.context.karaoke.active) {
                        if (root.dragBoundaryIndex >= 0)
                            root.context.karaoke.moveBoundary(root.dragBoundaryIndex, root.timeAt(mouse.x))
                        root.dragBoundaryIndex = -1
                        root.repaint()
                        return
                    }
                    root.dragCurrentMs = root.timeAt(mouse.x)
                    if (Math.abs(root.dragCurrentMs - root.dragStartMs) > 30) {
                        root.context.setSelectedTiming(Math.min(root.dragStartMs, root.dragCurrentMs),
                            Math.max(root.dragStartMs, root.dragCurrentMs))
                    } else {
                        root.context.media.seek(root.dragCurrentMs)
                    }
                    root.dragStartMs = -1
                    root.dragCurrentMs = -1
                    root.repaint()
                }
                onWheel: wheel => {
                    if (root.context.media.durationMs <= 0)
                        return
                    const amount = wheel.angleDelta.y !== 0 ? wheel.angleDelta.y : wheel.angleDelta.x
                    const span = root.viewEndMs - root.viewStartMs
                    if (wheel.modifiers & Qt.ControlModifier) {
                        const anchor = root.timeAt(wheel.x)
                        const ratio = (anchor - root.viewStartMs) / Math.max(1, span)
                        const factor = amount > 0 ? 0.8 : 1.25
                        const nextSpan = span * factor
                        root.setView(anchor - ratio * nextSpan, anchor + (1 - ratio) * nextSpan)
                    } else {
                        const delta = -amount / 120 * span * 0.12
                        root.setView(root.viewStartMs + delta, root.viewEndMs + delta)
                    }
                    wheel.accepted = true
                }
            }
        }

        RowLayout {
            Layout.fillWidth: true
            Layout.leftMargin: 6
            Layout.rightMargin: 6
            Label {
                text: root.visualizationMode === 1
                      ? root.context.media.spectrumBusy
                        ? qsTr("Generating cached spectrum tiles…")
                        : root.context.media.spectrumErrorString.length > 0
                          ? root.context.media.spectrumErrorString
                          : !root.context.media.hasAudio
                            ? qsTr("No FFMS2 audio track")
                            : root.context.media.audioReady
                              ? qsTr("FFMS2 spectrum ready")
                              : qsTr("Opening FFMS2 audio track…")
                      : root.context.media.waveform.busy
                        ? qsTr("Generating indexed waveform…")
                        : root.context.media.waveform.errorString.length > 0
                          ? root.context.media.waveform.errorString
                          : !root.context.media.hasAudio
                            ? qsTr("No FFMS2 audio track")
                            : root.context.media.audioReady
                              ? qsTr("FFMS2 audio ready")
                              : qsTr("Opening FFMS2 audio track…")
                color: (root.visualizationMode === 1
                        ? root.context.media.spectrumErrorString.length > 0
                        : root.context.media.waveform.errorString.length > 0)
                       ? Theme.palette.warning : Theme.palette.textMuted
            }
            Item { Layout.fillWidth: true }
            Label {
                text: root.context.karaoke.active
                      ? qsTr("Drag syllable boundaries; S restarts the selected range")
                      : qsTr("%1 view %2–%3 ms")
                        .arg(root.visualizationMode === 1 ? qsTr("Spectrum") : qsTr("Waveform"))
                        .arg(Math.round(root.viewStartMs)).arg(Math.round(root.viewEndMs))
                color: Theme.palette.textMuted
            }
        }
    }

    Connections {
        target: root.context.media.waveform
        function onCountChanged() { root.repaint() }
        function onBusyChanged() { root.repaint() }
        function onCompleteChanged() { root.repaint() }
    }
    Connections {
        target: root.context.media
        function onPositionChanged() { root.repaint() }
        function onDurationChanged() {
            if (root.context.media.durationMs <= 0) {
                root.fittedInitialLine = false
                root.fitMedia()
            } else if (!root.fittedInitialLine || root.viewEndMs > root.context.media.durationMs) {
                root.fittedInitialLine = true
                root.fitActiveLine()
            }
            root.repaint()
        }
    }
    Connections {
        target: root.context
        function onActiveLineChanged() { root.ensureActiveLineVisible() }
    }
    Connections {
        target: root.context.karaoke
        function onBoundariesChanged() { root.repaint() }
        function onSelectedIndexChanged() { root.repaint() }
        function onActiveChanged() { root.repaint() }
    }
    Connections {
        target: Theme
        function onPaletteChanged() { root.repaint() }
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
