import QtQuick
import QtQuick.Controls
import QtQuick.Layouts
import QtMultimedia
import Yoake

Rectangle {
    id: root
    required property var context
    color: "black"
    border.color: Theme.palette.border

    ColumnLayout {
        anchors.fill: parent
        spacing: 0
        Item {
            Layout.fillWidth: true
            Layout.fillHeight: true
            VideoOutput {
                id: video
                anchors.fill: parent
                fillMode: VideoOutput.PreserveAspectFit
                Component.onCompleted: root.context.media.attachVideoOutput(video)
                Component.onDestruction: root.context.media.detachVideoOutput(video)
            }
            SubtitleOverlay {
                id: overlay
                x: video.contentRect.x
                y: video.contentRect.y
                width: video.contentRect.width
                height: video.contentRect.height
                document: root.context
                timeMs: root.context.media.positionMs
            }
            Label {
                anchors.centerIn: parent
                visible: !root.context.media.hasMedia
                text: qsTr("Open media to begin previewing")
                color: Theme.palette.textMuted
            }
            Rectangle {
                anchors.left: parent.left
                anchors.right: parent.right
                anchors.bottom: parent.bottom
                height: rendererError.implicitHeight + 12
                visible: overlay.errorString.length > 0
                color: Theme.palette.videoOverlay
                Label {
                    id: rendererError
                    anchors.fill: parent
                    anchors.margins: 6
                    text: overlay.errorString
                    color: Theme.palette.warning
                    elide: Text.ElideRight
                }
            }
        }
        RowLayout {
            Layout.fillWidth: true
            Layout.margins: 4
            ToolButton { text: "◀"; onClicked: root.context.media.stepFrames(-1); ToolTip.visible: hovered; ToolTip.text: qsTr("Previous frame") }
            ToolButton { text: root.context.media.playing ? "❚❚" : "▶"; onClicked: root.context.media.togglePlayback() }
            ToolButton { text: "■"; onClicked: root.context.media.stop() }
            ToolButton { text: "▶"; onClicked: root.context.media.stepFrames(1); ToolTip.visible: hovered; ToolTip.text: qsTr("Next frame") }
            Slider {
                Layout.fillWidth: true
                from: 0
                to: Math.max(1, root.context.media.durationMs)
                value: root.context.media.positionMs
                onMoved: root.context.media.seek(value)
            }
            Label {
                text: qsTr("%1 / %2 ms · f%3").arg(root.context.media.positionMs)
                      .arg(root.context.media.durationMs).arg(root.context.media.currentFrame)
                color: Theme.palette.textMuted
            }
        }
    }
}
