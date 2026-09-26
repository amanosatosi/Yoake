import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

Rectangle {
    id: root
    required property var context
    property bool replaceMode: false
    property string statusText: ""
    signal closeRequested()
    color: Theme.palette.surface
    border.color: Theme.palette.border
    implicitHeight: visible ? (replaceMode ? 78 : 42) : 0

    function find(backwards) {
        const found = context.findText(queryField.text, caseCheck.checked, backwards)
        root.statusText = found ? qsTr("Match") : qsTr("No matches")
    }

    function focusQuery() { queryField.forceActiveFocus(); queryField.selectAll() }

    function replaceNext() {
        const replaced = root.context.replaceNext(queryField.text, replacementField.text, caseCheck.checked)
        root.statusText = replaced ? qsTr("Replaced one match") : qsTr("No current match")
    }

    ColumnLayout {
        anchors.fill: parent
        anchors.margins: 4
        spacing: 2
        RowLayout {
            Layout.fillWidth: true
            TextField {
                id: queryField
                Layout.preferredWidth: 220
                Layout.fillWidth: true
                Layout.maximumWidth: 360
                placeholderText: qsTr("Find in subtitle text")
                onAccepted: root.find(false)
            }
            CheckBox { id: caseCheck; text: qsTr("Case sensitive") }
            Label { text: root.statusText; color: Theme.palette.textMuted }
            Item { Layout.fillWidth: true }
            ToolButton { text: qsTr("Previous"); onClicked: root.find(true) }
            ToolButton { text: qsTr("Next"); onClicked: root.find(false) }
            ToolButton { text: "×"; ToolTip.text: qsTr("Close"); onClicked: root.closeRequested() }
        }
        RowLayout {
            Layout.fillWidth: true
            visible: root.replaceMode
            TextField {
                id: replacementField
                Layout.preferredWidth: 260
                Layout.fillWidth: true
                Layout.maximumWidth: 420
                placeholderText: qsTr("Replace with")
                onAccepted: root.replaceNext()
            }
            Item { Layout.fillWidth: true }
            ToolButton {
                text: qsTr("Replace")
                onClicked: {
                    const ok = root.context.replaceCurrent(queryField.text, replacementField.text, caseCheck.checked)
                    root.statusText = ok ? qsTr("Replaced") : qsTr("No current match")
                }
            }
            ToolButton {
                id: replaceNextButton
                text: qsTr("Replace Next")
                onClicked: root.replaceNext()
            }
            ToolButton {
                text: qsTr("Replace All")
                onClicked: {
                    const count = root.context.replaceAll(queryField.text, replacementField.text, caseCheck.checked)
                    root.statusText = qsTr("Replaced %1 match(es)").arg(count)
                }
            }
        }
    }

    onVisibleChanged: if (visible) queryField.forceActiveFocus()
}
