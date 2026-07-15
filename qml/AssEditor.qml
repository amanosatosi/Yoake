import QtQuick
import QtQuick.Controls
import QtQuick.Layouts
import Yoake

Rectangle {
    id: root
    required property var context
    color: Theme.palette.panel
    border.color: Theme.palette.border
    enabled: !root.context.karaoke.active
    opacity: enabled ? 1.0 : 0.62

    ColumnLayout {
        anchors.fill: parent
        anchors.margins: 4
        spacing: 3

        RowLayout {
            Layout.fillWidth: true
            CheckBox {
                text: qsTr("Comment")
                checked: root.context ? root.context.activeComment : false
                onToggled: if (root.context && root.context.activeComment !== checked) root.context.activeComment = checked
            }
            TextField {
                Layout.preferredWidth: 125
                placeholderText: qsTr("Style")
                text: root.context ? root.context.activeStyle : ""
                onEditingFinished: root.context.activeStyle = text
            }
            TextField {
                Layout.preferredWidth: 115
                placeholderText: qsTr("Actor")
                text: root.context ? root.context.activeActor : ""
                onEditingFinished: root.context.activeActor = text
            }
            TextField {
                Layout.fillWidth: true
                placeholderText: qsTr("Effect")
                text: root.context ? root.context.activeEffect : ""
                onEditingFinished: root.context.activeEffect = text
            }
            Label { text: qsTr("Layer"); color: Theme.palette.textMuted }
            SpinBox {
                Layout.preferredWidth: 78
                from: 0; to: 9999; editable: true
                value: root.context ? root.context.activeLayer : 0
                onValueModified: root.context.activeLayer = value
            }
        }

        RowLayout {
            Layout.fillWidth: true
            ToolButton {
                text: qsTr("Play line")
                enabled: root.context.media.hasAudio
                onClicked: root.context.media.playRange(root.context.activeStartMs, root.context.activeEndMs)
            }
            Label { text: qsTr("Start"); color: Theme.palette.textMuted }
            SpinBox {
                Layout.preferredWidth: 112
                from: 0; to: 2147483647; editable: true
                value: root.context ? root.context.activeStartMs : 0
                onValueModified: root.context.activeStartMs = value
            }
            Label { text: qsTr("End"); color: Theme.palette.textMuted }
            SpinBox {
                Layout.preferredWidth: 112
                from: 0; to: 2147483647; editable: true
                value: root.context ? root.context.activeEndMs : 0
                onValueModified: root.context.activeEndMs = value
            }
            Label {
                text: qsTr("Duration %1 ms").arg(Math.max(0,
                    root.context.activeEndMs - root.context.activeStartMs))
                color: Theme.palette.textMuted
            }
            Item { Layout.fillWidth: true }
            ToolButton {
                text: qsTr("Start <- video")
                enabled: root.context.media.hasVideo
                onClicked: root.context.activeStartMs = root.context.media.displayedFrameStartMs
            }
            ToolButton {
                text: qsTr("End <- video")
                enabled: root.context.media.hasVideo
                onClicked: root.context.activeEndMs = root.context.media.displayedFrameEndMs
            }
        }

        RowLayout {
            Layout.fillWidth: true
            spacing: 2
            ToolButton { text: "B"; font.bold: true; onClicked: editor.insert(editor.cursorPosition, "{\\b1}") }
            ToolButton { text: "I"; font.italic: true; onClicked: editor.insert(editor.cursorPosition, "{\\i1}") }
            ToolButton { text: "U"; font.underline: true; onClicked: editor.insert(editor.cursorPosition, "{\\u1}") }
            ToolButton { text: "\\N"; onClicked: editor.insert(editor.cursorPosition, "\\N") }
            ToolSeparator { }
            Label { text: qsTr("Text"); color: Theme.palette.textMuted }
            Item { Layout.fillWidth: true }
            ToolButton { text: qsTr("Duplicate"); onClicked: root.context.duplicateSelected() }
            ToolButton { text: qsTr("Insert after"); onClicked: root.context.insertAfterActive() }
        }

        ScrollView {
            Layout.fillWidth: true
            Layout.fillHeight: true
            TextArea {
                id: editor
                color: Theme.palette.text
                selectionColor: Theme.palette.selection
                selectedTextColor: Theme.palette.text
                wrapMode: TextEdit.Wrap
                font.family: "Cascadia Mono"
                font.pixelSize: 14
                text: root.context ? root.context.activeText : ""
                background: Rectangle { color: Theme.palette.surfaceRaised; border.color: Theme.palette.border; radius: 3 }
                onTextChanged: {
                    if (activeFocus && root.context && root.context.activeText !== text)
                        root.context.activeText = text
                }
                TapHandler {
                    acceptedButtons: Qt.LeftButton
                    onDoubleTapped: eventPoint => {
                        const point = eventPoint.position
                        const position = editor.positionAt(point.x, point.y)
                        const range = AssBoundaries.selectionRange(editor.text, position)
                        if (range.start >= 0 && range.end > range.start)
                            editor.select(range.start, range.end)
                    }
                }
                AssHighlighter { textDocument: editor.textDocument; theme: Theme }
            }
        }
    }
}
