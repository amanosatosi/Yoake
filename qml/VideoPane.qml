import QtQuick
import QtQuick.Controls
import QtQuick.Layouts
import Yoake

Rectangle {
    id: root
    required property var context
    color: "black"
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
            Layout.fillWidth: true
            Layout.fillHeight: true

            VideoFrame {
                id: video
                anchors.fill: parent
                session: root.context.media
            }
            SubtitleOverlay {
                id: overlay
                x: video.contentRect.x
                y: video.contentRect.y
                width: video.contentRect.width
                height: video.contentRect.height
                document: root.context
                // Mangetsu receives the exact start timestamp of the accepted
                // FFMS2 source frame, never an approximate player position.
                timeMs: root.context.media.displayedFrameStartMs
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
            }
            Rectangle {
                anchors.left: parent.left
                anchors.right: parent.right
                anchors.bottom: parent.bottom
                height: rendererError.implicitHeight + 12
                visible: overlay.errorString.length > 0 || root.context.media.errorString.length > 0
                color: Theme.palette.videoOverlay
                Label {
                    id: rendererError
                    anchors.fill: parent
                    anchors.margins: 6
                    text: overlay.errorString.length > 0 ? overlay.errorString : root.context.media.errorString
                    color: Theme.palette.warning
                    elide: Text.ElideRight
                }
            }
        }

        RowLayout {
            Layout.fillWidth: true
            Layout.margins: 4

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
                text: qsTr("%1 ms · frame %2/%3")
                      .arg(root.context.media.displayedFrameStartMs)
                      .arg(Math.max(0, root.context.media.currentFrame))
                      .arg(Math.max(0, root.context.media.frameCount - 1))
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
