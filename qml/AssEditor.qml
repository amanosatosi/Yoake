import QtQuick
import QtQuick.Controls
import QtQuick.Layouts
import Yoake

Rectangle {
    id: root
    required property var context
    property string lastActiveId: context ? context.lines.activeId : ""
    property bool syncingEditor: false
    readonly property bool textEntryFocus: editor.activeFocus || styleBox.activeFocus
        || actorBox.activeFocus || effectBox.activeFocus || layerBox.activeFocus
        || startBox.activeFocus || endBox.activeFocus || leftMarginBox.activeFocus
        || rightMarginBox.activeFocus || verticalMarginBox.activeFocus
    readonly property bool hasActiveLine: context && context.lines.activeRow >= 0
    color: Theme.palette.panel
    border.color: Theme.palette.border
    enabled: !root.context.karaoke.active
    opacity: enabled ? 1.0 : 0.62

    function focusText() {
        editor.forceActiveFocus()
        editor.cursorPosition = Math.min(editor.cursorPosition, editor.length)
    }

    function splitAtCursor() { root.context.splitActiveAtCursor(editor.cursorPosition) }
    function splitAtCurrentPosition() { root.context.splitActiveAtCursorAtPosition(editor.cursorPosition) }

    function syncEditor() {
        if (!root.context)
            return
        const nextId = root.context.lines.activeId
        const changedLine = nextId !== lastActiveId
        if (editor.text !== root.context.activeText) {
            const oldCursor = editor.cursorPosition
            syncingEditor = true
            editor.text = root.context.activeText
            editor.cursorPosition = changedLine ? 0 : Math.min(oldCursor, editor.length)
            syncingEditor = false
        } else if (changedLine) {
            editor.cursorPosition = 0
        }
        lastActiveId = nextId
        if (styleBox.editText !== root.context.activeStyle)
            styleBox.editText = root.context.activeStyle
        if (actorBox.editText !== root.context.activeActor)
            actorBox.editText = root.context.activeActor
        if (effectBox.editText !== root.context.activeEffect)
            effectBox.editText = root.context.activeEffect
    }

    ColumnLayout {
        anchors.fill: parent
        anchors.margins: 4
        spacing: 3

        RowLayout {
            Layout.fillWidth: true
            CheckBox {
                text: qsTr("Comment")
                enabled: root.hasActiveLine
                checked: root.context ? root.context.activeComment : false
                onToggled: if (root.context && root.context.activeComment !== checked) root.context.activeComment = checked
            }
            ComboBox {
                id: styleBox
                enabled: root.hasActiveLine
                Layout.preferredWidth: 150
                editable: true
                model: root.context ? root.context.styleNames : []
                currentIndex: root.context ? find(root.context.activeStyle) : -1
                editText: root.context ? root.context.activeStyle : ""
                onActivated: root.context.activeStyle = currentText
                onAccepted: root.context.activeStyle = editText
                onActiveFocusChanged: if (!activeFocus && root.context && root.context.activeStyle !== editText) root.context.activeStyle = editText
            }
            ComboBox {
                id: actorBox
                enabled: root.hasActiveLine
                Layout.preferredWidth: 120
                editable: true
                model: root.context ? root.context.actorSuggestions : []
                currentIndex: root.context ? find(root.context.activeActor) : -1
                editText: root.context ? root.context.activeActor : ""
                onActivated: root.context.activeActor = currentText
                onAccepted: root.context.activeActor = editText
                onActiveFocusChanged: if (!activeFocus && root.context && root.context.activeActor !== editText) root.context.activeActor = editText
            }
            ComboBox {
                id: effectBox
                enabled: root.hasActiveLine
                Layout.fillWidth: true
                editable: true
                model: root.context ? root.context.effectSuggestions : []
                currentIndex: root.context ? find(root.context.activeEffect) : -1
                editText: root.context ? root.context.activeEffect : ""
                onActivated: root.context.activeEffect = currentText
                onAccepted: root.context.activeEffect = editText
                onActiveFocusChanged: if (!activeFocus && root.context && root.context.activeEffect !== editText) root.context.activeEffect = editText
            }
            Label { text: qsTr("Layer"); color: Theme.palette.textMuted }
            SpinBox {
                id: layerBox
                enabled: root.hasActiveLine
                Layout.preferredWidth: 82
                from: -9999; to: 99999; editable: true
                value: root.context ? root.context.activeLayer : 0
                onValueModified: root.context.activeLayer = value
            }
        }

        RowLayout {
            Layout.fillWidth: true
            ToolButton {
                text: qsTr("Play line")
                enabled: root.context.lines.activeRow >= 0 && root.context.media.hasAudio
                onClicked: root.context.media.playRange(root.context.activeStartMs, root.context.activeEndMs)
            }
            Label { text: qsTr("Start ms"); color: Theme.palette.textMuted }
            SpinBox {
                id: startBox
                enabled: root.hasActiveLine
                Layout.preferredWidth: 112
                from: 0; to: 2147483647; editable: true
                value: root.context ? root.context.activeStartMs : 0
                onValueModified: root.context.activeStartMs = value
            }
            Label { text: qsTr("End ms"); color: Theme.palette.textMuted }
            SpinBox {
                id: endBox
                enabled: root.hasActiveLine
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
            Label { text: qsTr("Margins"); color: Theme.palette.textMuted }
            Label { text: "L"; color: Theme.palette.textMuted }
            SpinBox {
                id: leftMarginBox
                enabled: root.hasActiveLine
                Layout.preferredWidth: 76
                from: 0; to: 9999; editable: true
                value: root.context ? root.context.activeMarginLeft : 0
                onValueModified: root.context.activeMarginLeft = value
            }
            Label { text: "R"; color: Theme.palette.textMuted }
            SpinBox {
                id: rightMarginBox
                enabled: root.hasActiveLine
                Layout.preferredWidth: 76
                from: 0; to: 9999; editable: true
                value: root.context ? root.context.activeMarginRight : 0
                onValueModified: root.context.activeMarginRight = value
            }
            Label { text: "V"; color: Theme.palette.textMuted }
            SpinBox {
                id: verticalMarginBox
                enabled: root.hasActiveLine
                Layout.preferredWidth: 76
                from: 0; to: 9999; editable: true
                value: root.context ? root.context.activeMarginVertical : 0
                onValueModified: root.context.activeMarginVertical = value
            }
        }

        RowLayout {
            Layout.fillWidth: true
            spacing: 2
            ToolButton { text: "B"; font.bold: true; enabled: root.hasActiveLine; onClicked: editor.insert(editor.cursorPosition, "{\\b1}") }
            ToolButton { text: "I"; font.italic: true; enabled: root.hasActiveLine; onClicked: editor.insert(editor.cursorPosition, "{\\i1}") }
            ToolButton { text: "U"; font.underline: true; enabled: root.hasActiveLine; onClicked: editor.insert(editor.cursorPosition, "{\\u1}") }
            ToolButton { text: "\\N"; enabled: root.hasActiveLine; onClicked: editor.insert(editor.cursorPosition, "\\N") }
            ToolSeparator { }
            Label { text: qsTr("Text"); color: Theme.palette.textMuted }
            Item { Layout.fillWidth: true }
            ToolButton { text: qsTr("Split at cursor"); enabled: root.context.lines.activeRow >= 0; onClicked: root.context.splitActiveAtCursor(editor.cursorPosition) }
            ToolButton {
                text: qsTr("Split at position")
                enabled: root.context.lines.activeRow >= 0 && root.context.media.hasMedia
                onClicked: root.context.splitActiveAtCursorAtPosition(editor.cursorPosition)
            }
            ToolButton { text: qsTr("Duplicate"); enabled: root.context.lines.selectedCount > 0; onClicked: root.context.duplicateSelected() }
            ToolButton { text: qsTr("Insert after"); onClicked: root.context.insertAfterActive() }
        }

        ScrollView {
            Layout.fillWidth: true
            Layout.fillHeight: true
            TextArea {
                id: editor
                objectName: "subtitleTextEditor"
                enabled: root.hasActiveLine
                color: Theme.palette.text
                selectionColor: Theme.palette.selection
                selectedTextColor: Theme.palette.text
                wrapMode: TextEdit.Wrap
                font.family: "Cascadia Mono"
                font.pixelSize: 14
                text: root.context ? root.context.activeText : ""
                background: Rectangle { color: Theme.palette.surfaceRaised; border.color: Theme.palette.border; radius: 3 }
                onTextChanged: {
                    if (!root.syncingEditor && activeFocus && root.context && root.context.activeText !== text)
                        root.context.activeText = text
                }
                Keys.onPressed: event => {
                    if (event.key === Qt.Key_Return || event.key === Qt.Key_Enter) {
                        const control = (event.modifiers & Qt.ControlModifier) !== 0
                        const shift = (event.modifiers & Qt.ShiftModifier) !== 0
                        if (control && shift)
                            root.context.splitActiveAtCursorAtPosition(editor.cursorPosition)
                        else if (control)
                            root.context.splitActiveAtCursor(editor.cursorPosition)
                        else
                            editor.insert(editor.cursorPosition, "\\N")
                        event.accepted = true
                    }
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

    Connections {
        target: root.context
        function onActiveLineChanged() { root.syncEditor() }
        function onFindMatchChanged() {
            if (root.context && root.context.findMatchStart >= 0
                && root.context.findMatchLength > 0) {
                const start = root.context.findMatchStart
                editor.forceActiveFocus()
                editor.select(start, start + root.context.findMatchLength)
            } else {
                editor.deselect()
            }
        }
    }
}
