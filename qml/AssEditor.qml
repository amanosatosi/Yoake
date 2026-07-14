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
        anchors.margins: 8
        spacing: 6

        RowLayout {
            Layout.fillWidth: true
            Label { text: qsTr("Layer"); color: Theme.palette.textMuted }
            SpinBox {
                from: 0; to: 9999; editable: true
                value: root.context ? root.context.activeLayer : 0
                onValueModified: root.context.activeLayer = value
            }
            Label { text: qsTr("Start ms"); color: Theme.palette.textMuted }
            SpinBox {
                Layout.preferredWidth: 125
                from: 0; to: 2147483647; editable: true
                value: root.context ? root.context.activeStartMs : 0
                onValueModified: root.context.activeStartMs = value
            }
            Label { text: qsTr("End ms"); color: Theme.palette.textMuted }
            SpinBox {
                Layout.preferredWidth: 125
                from: 0; to: 2147483647; editable: true
                value: root.context ? root.context.activeEndMs : 0
                onValueModified: root.context.activeEndMs = value
            }
            CheckBox {
                text: qsTr("Comment")
                checked: root.context ? root.context.activeComment : false
                onToggled: if (root.context && root.context.activeComment !== checked) root.context.activeComment = checked
            }
            Item { Layout.fillWidth: true }
        }

        RowLayout {
            Layout.fillWidth: true
            Label { text: qsTr("Style"); color: Theme.palette.textMuted }
            TextField {
                Layout.preferredWidth: 150
                text: root.context ? root.context.activeStyle : ""
                onEditingFinished: root.context.activeStyle = text
            }
            Label { text: qsTr("Actor"); color: Theme.palette.textMuted }
            TextField {
                Layout.preferredWidth: 150
                text: root.context ? root.context.activeActor : ""
                onEditingFinished: root.context.activeActor = text
            }
            Label { text: qsTr("Effect"); color: Theme.palette.textMuted }
            TextField {
                Layout.fillWidth: true
                text: root.context ? root.context.activeEffect : ""
                onEditingFinished: root.context.activeEffect = text
            }
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
                font.pixelSize: 15
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
