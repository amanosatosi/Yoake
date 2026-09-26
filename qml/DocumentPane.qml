import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

Item {
    id: root
    required property var context
    readonly property bool textEditorFocused: activeEditor.textEntryFocus
    readonly property bool linkedMediaFailed: context.linkedMediaError.length > 0
                                               || context.media.errorString.length > 0

    function openFind(replace) {
        findReplace.replaceMode = replace
        findReplace.visible = true
        findReplace.focusQuery()
    }

    function findNext(backwards) {
        if (!findReplace.visible)
            findReplace.visible = true
        findReplace.find(backwards)
    }

    function splitAtCursor() { activeEditor.splitAtCursor() }
    function splitAtCurrentPosition() { activeEditor.splitAtCurrentPosition() }

    function linkedMediaStatus() {
        if (root.context.linkedMediaError.length > 0)
            return root.context.linkedMediaError
        if (root.context.media.errorString.length > 0)
            return qsTr("Linked media error: %1").arg(root.context.media.errorString)
        const linked = root.context.linkedVideoFile.length > 0
                       ? root.context.linkedVideoFile : root.context.linkedAudioFile
        if (root.context.media.indexing)
            return qsTr("ASS media: %1 - FFMS2 indexing %2%").arg(linked)
                .arg(Math.round(root.context.media.indexingProgress * 100))
        if (root.context.media.hasVideo && root.context.media.hasAudio)
            return root.context.media.audioReady
                   ? qsTr("ASS media loaded: %1 - embedded audio ready").arg(linked)
                   : qsTr("ASS video loaded: %1 - opening embedded audio...").arg(linked)
        if (root.context.media.hasVideo)
            return qsTr("ASS video loaded: %1 - no audio track").arg(linked)
        if (root.context.media.hasAudio)
            return root.context.media.audioReady
                   ? qsTr("ASS audio loaded: %1").arg(linked)
                   : qsTr("ASS audio opening: %1").arg(linked)
        return qsTr("ASS linked media queued: %1").arg(linked)
    }

    ColumnLayout {
        anchors.fill: parent
        spacing: 0

        Rectangle {
            Layout.fillWidth: true
            implicitHeight: 28
            visible: root.context.linkedVideoFile.length > 0
                     || root.context.linkedAudioFile.length > 0
            color: root.linkedMediaFailed
                   ? Theme.palette.timingRegion : Theme.palette.surface
            border.color: root.linkedMediaFailed
                          ? Theme.palette.warning : Theme.palette.border

            RowLayout {
                anchors.fill: parent
                anchors.leftMargin: 6
                anchors.rightMargin: 4
                spacing: 6
                BusyIndicator {
                    Layout.preferredWidth: 18
                    Layout.preferredHeight: 18
                    running: root.context.media.indexing
                             || (root.context.media.hasAudio && !root.context.media.audioReady)
                    visible: running
                }
                Label {
                    Layout.fillWidth: true
                    text: root.linkedMediaStatus()
                    color: root.linkedMediaFailed
                           ? Theme.palette.warning : Theme.palette.textMuted
                    elide: Text.ElideMiddle
                }
                ToolButton {
                    text: qsTr("Retry")
                    visible: root.linkedMediaFailed
                    onClicked: root.context.loadLinkedMedia()
                }
            }
        }

        FindReplaceBar {
            id: findReplace
            Layout.fillWidth: true
            visible: false
            context: root.context
            onCloseRequested: visible = false
        }

        SplitView {
            Layout.fillWidth: true
            Layout.fillHeight: true
            orientation: Qt.Vertical

            SplitView {
                SplitView.preferredHeight: parent.height * 0.54
                SplitView.minimumHeight: 350
                orientation: Qt.Horizontal

                VideoPane {
                    SplitView.preferredWidth: parent.width * 0.51
                    SplitView.minimumWidth: 440
                    context: root.context
                }

                SplitView {
                    SplitView.fillWidth: true
                    SplitView.minimumWidth: 480
                    orientation: Qt.Vertical

                    AudioPane {
                        SplitView.preferredHeight: 160
                        SplitView.minimumHeight: 125
                        context: root.context
                    }

                    AssEditor {
                        id: activeEditor
                        SplitView.fillHeight: true
                        SplitView.minimumHeight: 190
                        context: root.context
                    }
                }
            }

            SubtitleGrid {
                id: subtitleGrid
                SplitView.fillHeight: true
                SplitView.minimumHeight: 180
                context: root.context
                onRequestTextFocus: activeEditor.focusText()
            }
        }
    }
}
