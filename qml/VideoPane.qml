import QtQuick
import QtQuick.Controls
import QtQuick.Layouts
import Yoake

Rectangle {
    id: root
    required property var context
    color: Theme.palette.panel
    border.color: Theme.palette.border

    function trackIndex(model, track) {
        for (let index = 0; index < model.length; ++index) {
            if (model[index].track === track)
                return index
        }
        return -1
    }

    ColumnLayout {
        anchors.fill: parent
        spacing: 0

        Item {
            id: viewportSurface
            Layout.fillWidth: true
            Layout.fillHeight: true
            clip: true
            focus: true

            VideoFrame {
                id: video
                anchors.fill: parent
                session: root.context.media
                viewport: root.context.videoViewport
            }

            SubtitleOverlay {
                id: overlay
                readonly property rect fitRect: root.context.videoViewport.fittedVideoRect
                x: fitRect.x + root.context.videoViewport.panOffset.x
                y: fitRect.y + root.context.videoViewport.panOffset.y
                width: fitRect.width
                height: fitRect.height
                scale: root.context.videoViewport.contentZoom
                transformOrigin: Item.Center
                document: root.context
                // Render at the same fitted source-frame rectangle as the video.
                timeMs: root.context.media.displayedFrameStartMs
                z: 1
            }

            Item {
                id: toolOverlay
                anchors.fill: parent
                z: 2
                visible: root.context.media.hasVideo
                enabled: false

                Repeater {
                    model: root.context.visualTools.overlayFeatures
                    delegate: Item {
                        required property var modelData
                        readonly property string kind: modelData.kind || ""
                        x: modelData.x || 0
                        y: modelData.y || 0
                        width: kind === "rect" ? modelData.width : 0
                        height: kind === "rect" ? modelData.height : 0
                        z: 3

                        Rectangle {
                            visible: kind === "point"
                            width: 13
                            height: 13
                            x: -width / 2
                            y: -height / 2
                            radius: 3
                            color: modelData.selected ? Theme.palette.accent : Theme.palette.surfaceRaised
                            border.width: 2
                            border.color: Theme.palette.text
                        }
                        Rectangle {
                            visible: kind === "rect"
                            anchors.fill: parent
                            color: "#00000000"
                            border.width: 2
                            border.color: Theme.palette.accent
                        }
                        Rectangle {
                            visible: kind === "ring"
                            width: (modelData.radius || 44) * 2
                            height: width
                            x: -width / 2
                            y: -height / 2
                            radius: width / 2
                            color: "#00000000"
                            border.width: 1
                            border.color: Theme.palette.accent
                        }
                        Rectangle {
                            visible: kind === "line" || kind === "screen-line"
                            width: Math.max(1, Math.hypot((modelData.x2 || 0) - (modelData.x || 0),
                                                          (modelData.y2 || 0) - (modelData.y || 0)))
                            height: kind === "line" ? 2 : 1
                            transformOrigin: Item.Left
                            rotation: Math.atan2((modelData.y2 || 0) - (modelData.y || 0),
                                                 (modelData.x2 || 0) - (modelData.x || 0)) * 180 / Math.PI
                            color: kind === "line" ? Theme.palette.accent
                                                   : modelData.snap ? Theme.palette.accent : Theme.palette.textMuted
                            opacity: modelData.guide ? 0.45 : 1.0
                        }
                        Rectangle {
                            visible: kind === "crosshair"
                            x: -toolOverlay.width
                            y: 0
                            width: toolOverlay.width * 2
                            height: 1
                            color: modelData.color || Theme.palette.text
                        }
                        Rectangle {
                            visible: kind === "crosshair"
                            x: 0
                            y: -toolOverlay.height
                            width: 1
                            height: toolOverlay.height * 2
                            color: modelData.color || Theme.palette.text
                        }
                        Label {
                            visible: (kind === "point" || kind === "label") && (modelData.label || "").length > 0
                            x: kind === "label" ? 0 : 10
                            y: kind === "label" ? 0 : -height / 2
                            text: modelData.label || ""
                            color: Theme.palette.text
                            font.pixelSize: 12
                            padding: 2
                            background: Rectangle {
                                color: Theme.palette.videoOverlay
                                radius: 3
                            }
                        }
                    }
                }
            }

            Label {
                anchors.centerIn: parent
                visible: !root.context.media.hasVideo
                text: root.context.media.indexing
                      ? qsTr("FFMS2 indexing… %1%").arg(Math.round(root.context.media.indexingProgress * 100))
                      : qsTr("Open media to begin exact-frame preview")
                color: Theme.palette.textMuted
            }
            BusyIndicator {
                anchors.horizontalCenter: parent.horizontalCenter
                anchors.top: parent.top
                anchors.topMargin: 8
                running: root.context.media.indexing || root.context.media.framePending
                visible: running
                z: 5
            }
            Rectangle {
                anchors.left: parent.left
                anchors.right: parent.right
                anchors.bottom: parent.bottom
                height: rendererError.implicitHeight + 12
                visible: overlay.errorString.length > 0 || root.context.media.errorString.length > 0
                color: Theme.palette.videoOverlay
                z: 5
                Label {
                    id: rendererError
                    anchors.fill: parent
                    anchors.margins: 6
                    text: overlay.errorString.length > 0 ? overlay.errorString : root.context.media.errorString
                    color: Theme.palette.warning
                    elide: Text.ElideRight
                }
            }

            MouseArea {
                id: canvasInput
                anchors.fill: parent
                acceptedButtons: Qt.LeftButton | Qt.MiddleButton
                hoverEnabled: true
                preventStealing: true
                cursorShape: middlePanning ? Qt.ClosedHandCursor
                                           : root.context.visualTools.activeToolId === "crosshair"
                                             ? Qt.CrossCursor : Qt.ArrowCursor
                property bool middlePanning: false
                property bool leftEditing: false

                onPressed: function(mouse) {
                    forceActiveFocus()
                    if (mouse.button === Qt.MiddleButton) {
                        middlePanning = true
                        root.context.videoViewport.beginPan(Qt.point(mouse.x, mouse.y))
                    } else if (mouse.button === Qt.LeftButton) {
                        leftEditing = true
                        root.context.visualTools.pointerDown(mouse.x, mouse.y, mouse.button, mouse.modifiers)
                    }
                    mouse.accepted = true
                }
                onPositionChanged: function(mouse) {
                    if (middlePanning)
                        root.context.videoViewport.updatePan(Qt.point(mouse.x, mouse.y))
                    else if (leftEditing)
                        root.context.visualTools.pointerMove(mouse.x, mouse.y, mouse.buttons, mouse.modifiers)
                    else
                        root.context.visualTools.pointerMove(mouse.x, mouse.y, mouse.buttons, mouse.modifiers)
                }
                onReleased: function(mouse) {
                    if (mouse.button === Qt.MiddleButton) {
                        middlePanning = false
                        root.context.videoViewport.endPan()
                    } else if (mouse.button === Qt.LeftButton) {
                        leftEditing = false
                        root.context.visualTools.pointerUp(mouse.x, mouse.y, mouse.button, mouse.modifiers)
                    }
                }
                onCanceled: {
                    middlePanning = false
                    leftEditing = false
                    root.context.videoViewport.endPan()
                    root.context.visualTools.cancelOperation()
                }
                onExited: root.context.visualTools.pointerLeave()
                onWheel: function(wheel) {
                    const deltaX = wheel.pixelDelta.x !== 0 ? wheel.pixelDelta.x : wheel.angleDelta.x * 0.45
                    const deltaY = wheel.pixelDelta.y !== 0 ? wheel.pixelDelta.y : wheel.angleDelta.y * 0.45
                    if ((wheel.modifiers & Qt.ControlModifier) !== 0) {
                        const zoomDelta = wheel.pixelDelta.y !== 0 ? wheel.pixelDelta.y : wheel.angleDelta.y
                        root.context.videoViewport.zoomAt(Qt.point(wheel.x, wheel.y), Math.exp(zoomDelta * 0.0015))
                        wheel.accepted = true
                    } else if ((wheel.modifiers & Qt.ShiftModifier) !== 0) {
                        if (wheel.pixelDelta.x !== 0 || wheel.angleDelta.x !== 0)
                            root.context.videoViewport.panBy(Qt.point(deltaX, 0))
                        else
                            root.context.videoViewport.panBy(Qt.point(0, deltaY))
                        wheel.accepted = true
                    } else if (wheel.pixelDelta.x !== 0 || wheel.angleDelta.x !== 0) {
                        root.context.videoViewport.panBy(Qt.point(deltaX, 0))
                        wheel.accepted = true
                    } else {
                        wheel.accepted = root.context.visualTools.wheel(wheel.x, wheel.y,
                            wheel.pixelDelta.x !== 0 ? wheel.pixelDelta.x : wheel.angleDelta.x,
                            wheel.pixelDelta.y !== 0 ? wheel.pixelDelta.y : wheel.angleDelta.y,
                            wheel.modifiers)
                    }
                }
                Keys.onPressed: function(event) {
                    root.context.visualTools.keyDown(event.key, event.modifiers)
                    if (event.key === Qt.Key_Escape || event.key === Qt.Key_Delete
                        || event.key === Qt.Key_Left || event.key === Qt.Key_Right
                        || event.key === Qt.Key_Up || event.key === Qt.Key_Down)
                        event.accepted = true
                    if ((event.modifiers & Qt.ControlModifier) !== 0
                        && (event.modifiers & Qt.AltModifier) !== 0
                        && ((event.key >= Qt.Key_1 && event.key <= Qt.Key_9) || event.key === Qt.Key_0))
                        event.accepted = true
                }
            }

            PinchHandler {
                id: pinchInput
                target: null
                property real startZoom: 1.0
                onActiveChanged: {
                    if (active) {
                        startZoom = root.context.videoViewport.contentZoom
                        root.context.videoViewport.beginAnchoredZoom(centroid.position)
                    } else {
                        root.context.videoViewport.endAnchoredZoom()
                    }
                }
                onScaleChanged: root.context.videoViewport.updateAnchoredZoom(centroid.position, startZoom * scale)
            }
        }

        RowLayout {
            Layout.fillWidth: true
            Layout.preferredHeight: 38
            Layout.leftMargin: 4
            Layout.rightMargin: 4
            spacing: 2

            Flickable {
                Layout.fillWidth: true
                Layout.fillHeight: true
                clip: true
                contentWidth: toolRow.implicitWidth
                contentHeight: height
                boundsBehavior: Flickable.StopAtBounds
                interactive: contentWidth > width

                Row {
                    id: toolRow
                    height: parent.height
                    spacing: 2

                    Repeater {
                        model: root.context.visualTools.toolDescriptors
                        delegate: ToolButton {
                            required property var modelData
                            width: 34
                            height: 34
                            padding: 0
                            focusPolicy: Qt.NoFocus
                            text: modelData.icon
                            font.pixelSize: 19
                            Accessible.name: modelData.name
                            Accessible.description: modelData.tooltip
                            checked: root.context.visualTools.activeToolId === modelData.id
                            onClicked: root.context.visualTools.setActiveToolId(modelData.id)
                            background: Rectangle {
                                radius: 4
                                color: parent.checked ? Theme.palette.active
                                                     : parent.hovered ? Theme.palette.hover : "transparent"
                                border.color: parent.checked ? Theme.palette.accent : "transparent"
                            }
                            ToolTip.visible: hovered
                            ToolTip.text: modelData.name + "\n" + modelData.tooltip
                        }
                    }

                    ToolSeparator { }

                    Repeater {
                        model: root.context.visualTools.contextOptions
                        delegate: ToolButton {
                            required property var modelData
                            implicitWidth: modelData.id.indexOf("align-") === 0 ? 28 : implicitContentWidth + 16
                            height: 32
                            padding: 5
                            focusPolicy: Qt.NoFocus
                            text: modelData.label
                            checkable: modelData.id.indexOf("shift-") === 0
                            checked: checkable && modelData.value
                            onClicked: root.context.visualTools.setOption(modelData.id,
                                modelData.id.indexOf("shift-") === 0 ? !modelData.value : modelData.value)
                            background: Rectangle {
                                radius: 4
                                color: parent.hovered ? Theme.palette.hover : "transparent"
                            }
                            ToolTip.visible: hovered
                            ToolTip.text: modelData.id.indexOf("align-") === 0
                                          ? qsTr("Set ASS alignment %1").arg(modelData.id.slice(6)) : modelData.label
                        }
                    }
                }
            }

            Label {
                visible: root.context.visualTools.activeToolId === "crosshair"
                text: root.context.visualTools.coordinateLabel
                color: Theme.palette.textMuted
                font.pixelSize: 11
                elide: Text.ElideLeft
            }
            ToolButton {
                width: 34
                height: 34
                padding: 0
                focusPolicy: Qt.NoFocus
                text: "↺"
                font.pixelSize: 18
                onClicked: root.context.videoViewport.resetView()
                ToolTip.visible: hovered
                ToolTip.text: qsTr("Reset View")
                background: Rectangle {
                    radius: 4
                    color: parent.hovered ? Theme.palette.hover : "transparent"
                }
            }
        }

        RowLayout {
            Layout.fillWidth: true
            Layout.preferredHeight: 38
            Layout.leftMargin: 4
            Layout.rightMargin: 4

            ToolButton {
                text: "|<"
                enabled: root.context.media.hasVideo
                onClicked: root.context.media.stepFrames(-1)
                ToolTip.visible: hovered
                ToolTip.text: qsTr("Previous source frame")
            }
            ToolButton {
                text: root.context.media.playing ? qsTr("Pause") : qsTr("Play")
                enabled: root.context.media.hasVideo || root.context.media.hasAudio
                onClicked: root.context.media.togglePlayback()
            }
            ToolButton {
                text: qsTr("Stop")
                enabled: root.context.media.hasMedia
                onClicked: root.context.media.stop()
            }
            ToolButton {
                text: ">|"
                enabled: root.context.media.hasVideo
                onClicked: root.context.media.stepFrames(1)
                ToolTip.visible: hovered
                ToolTip.text: qsTr("Next source frame")
            }
            Slider {
                Layout.fillWidth: true
                from: 0
                to: Math.max(1, root.context.media.durationMs)
                value: root.context.media.positionMs
                onMoved: root.context.media.seek(value)
            }
            Label {
                text: qsTr("%1 ms · frame %2/%3 · %4%")
                      .arg(root.context.media.displayedFrameStartMs)
                      .arg(Math.max(0, root.context.media.currentFrame))
                      .arg(Math.max(0, root.context.media.frameCount - 1))
                      .arg(Math.round(root.context.videoViewport.contentZoom * 100))
                color: Theme.palette.textMuted
            }
        }

        RowLayout {
            Layout.fillWidth: true
            Layout.leftMargin: 6
            Layout.rightMargin: 6
            visible: root.context.media.videoTracks.length > 1 || root.context.media.audioTracks.length > 1

            Label { text: qsTr("Video"); color: Theme.palette.textMuted }
            ComboBox {
                model: root.context.media.videoTracks
                textRole: "label"
                valueRole: "track"
                currentIndex: root.trackIndex(model, root.context.media.selectedVideoTrack)
                onActivated: root.context.media.selectVideoTrack(currentValue)
            }
            Label { text: qsTr("Audio"); color: Theme.palette.textMuted }
            ComboBox {
                model: root.context.media.audioTracks
                textRole: "label"
                valueRole: "track"
                currentIndex: root.trackIndex(model, root.context.media.selectedAudioTrack)
                onActivated: root.context.media.selectAudioTrack(currentValue)
            }
            Item { Layout.fillWidth: true }
            Label {
                text: root.context.media.variableFrameRate
                      ? qsTr("VFR · indexed source timing")
                      : qsTr("Source rate %1 fps").arg(root.context.media.frameRate.toFixed(3))
                color: Theme.palette.textMuted
            }
        }
    }
}
