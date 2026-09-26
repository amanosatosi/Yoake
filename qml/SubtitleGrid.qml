import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

Rectangle {
    id: root
    required property var context
    signal requestTextFocus()
    color: Theme.palette.panel
    border.color: Theme.palette.border

    property int actorColumnWidth: width >= 850 ? 112 : 0

    function timeText(ms) {
        const total = Math.max(0, Math.floor(ms / 10))
        const cs = total % 100
        const seconds = Math.floor(total / 100) % 60
        const minutes = Math.floor(total / 6000) % 60
        const hours = Math.floor(total / 360000)
        return hours + ":" + String(minutes).padStart(2, "0") + ":" + String(seconds).padStart(2, "0") + "." + String(cs).padStart(2, "0")
    }

    function shortcut(event) {
        const doc = root.context
        if (!doc || doc.karaoke.active)
            return false
        const control = (event.modifiers & Qt.ControlModifier) !== 0
        const shift = (event.modifiers & Qt.ShiftModifier) !== 0
        switch (event.key) {
        case Qt.Key_Up:
            if (control) return false
            doc.lines.moveActive(-1, shift); return true
        case Qt.Key_Down:
            if (control) return false
            doc.lines.moveActive(1, shift); return true
        case Qt.Key_Home: doc.lines.selectFirst(); return true
        case Qt.Key_End: doc.lines.selectLast(); return true
        case Qt.Key_Delete:
            if (control) return false
            doc.deleteSelected(); return true
        case Qt.Key_A:
            if (control) { doc.lines.selectAll(); return true }
            return false
        case Qt.Key_C:
            if (control) { doc.copySelected(); return true }
            return false
        case Qt.Key_X:
            if (control) { doc.cutSelected(); return true }
            return false
        case Qt.Key_V:
            if (control) { doc.pasteRows(); return true }
            return false
        default: return false
        }
    }

    ColumnLayout {
        anchors.fill: parent
        spacing: 0
        Rectangle {
            Layout.fillWidth: true
            implicitHeight: 25
            color: Theme.palette.surface
            Row {
                anchors.fill: parent
                anchors.leftMargin: 4
                spacing: 4
                Label { width: 34; text: "#"; color: Theme.palette.textMuted; anchors.verticalCenter: parent.verticalCenter }
                Label { width: 32; text: qsTr("L"); color: Theme.palette.textMuted; anchors.verticalCenter: parent.verticalCenter }
                Label { width: 86; text: qsTr("Start"); color: Theme.palette.textMuted; anchors.verticalCenter: parent.verticalCenter }
                Label { width: 86; text: qsTr("End"); color: Theme.palette.textMuted; anchors.verticalCenter: parent.verticalCenter }
                Label { width: 42; text: qsTr("CPS"); color: Theme.palette.textMuted; anchors.verticalCenter: parent.verticalCenter }
                Label { width: 104; text: qsTr("Style"); color: Theme.palette.textMuted; anchors.verticalCenter: parent.verticalCenter }
                Label { width: root.actorColumnWidth; text: qsTr("Actor"); visible: width > 0; color: Theme.palette.textMuted; anchors.verticalCenter: parent.verticalCenter }
                Label { text: qsTr("Text"); color: Theme.palette.textMuted; anchors.verticalCenter: parent.verticalCenter }
            }
        }
        ListView {
            id: list
            Layout.fillWidth: true
            Layout.fillHeight: true
            clip: true
            reuseItems: true
            cacheBuffer: 300
            boundsBehavior: Flickable.StopAtBounds
            keyNavigationEnabled: false
            objectName: "subtitleGridView"
            model: root.context ? root.context.lines : null
            ScrollBar.vertical: ScrollBar { }
            Keys.onPressed: event => {
                if (root.shortcut(event))
                    event.accepted = true
            }
            delegate: Rectangle {
                id: rowDelegate
                required property int index
                required property int subtitleLayer
                required property int startMs
                required property int endMs
                required property real cps
                required property string style
                required property string actor
                required property string subtitleText
                required property bool comment
                required property bool selected
                required property bool active
                width: ListView.view.width
                height: 25
                color: active ? Theme.palette.active
                              : selected ? Theme.palette.selection
                              : index % 2 ? Theme.palette.gridAlternate : Theme.palette.panel
                Rectangle {
                    width: 3
                    anchors.top: parent.top
                    anchors.bottom: parent.bottom
                    color: rowDelegate.comment ? Theme.palette.gridComment : "transparent"
                }
                Row {
                    anchors.fill: parent
                    anchors.leftMargin: 7
                    spacing: 4
                    Label { width: 31; text: rowDelegate.index + 1; color: Theme.palette.textMuted; anchors.verticalCenter: parent.verticalCenter }
                    Label { width: 32; text: rowDelegate.subtitleLayer; color: Theme.palette.text; anchors.verticalCenter: parent.verticalCenter }
                    Label { width: 86; text: root.timeText(rowDelegate.startMs); color: Theme.palette.text; anchors.verticalCenter: parent.verticalCenter }
                    Label { width: 86; text: root.timeText(rowDelegate.endMs); color: Theme.palette.text; anchors.verticalCenter: parent.verticalCenter }
                    Label { width: 42; text: Math.round(rowDelegate.cps); color: Theme.palette.textMuted; anchors.verticalCenter: parent.verticalCenter }
                    Label { width: 104; text: rowDelegate.style; color: Theme.palette.textMuted; elide: Text.ElideRight; anchors.verticalCenter: parent.verticalCenter }
                    Label { width: root.actorColumnWidth; text: rowDelegate.actor; visible: width > 0; color: Theme.palette.textMuted; elide: Text.ElideRight; anchors.verticalCenter: parent.verticalCenter }
                    Label {
                        width: Math.max(0, rowDelegate.width - 7 - (31 + 32 + 86 + 86 + 42 + 104 + root.actorColumnWidth + 28))
                        text: rowDelegate.subtitleText.replace(/\\N/gi, " ↵ ").replace(/\\n/gi, " ↵ ")
                        color: rowDelegate.comment ? Theme.palette.gridComment : Theme.palette.text
                        font.italic: rowDelegate.comment
                        elide: Text.ElideRight
                        anchors.verticalCenter: parent.verticalCenter
                    }
                }
                MouseArea {
                    anchors.fill: parent
                    acceptedButtons: Qt.LeftButton | Qt.RightButton
                    onPressed: mouse => {
                        list.forceActiveFocus(Qt.MouseFocusReason)
                        if (mouse.button === Qt.RightButton) {
                            const model = root.context.lines
                            if (rowDelegate.selected)
                                model.activateRow(rowDelegate.index)
                            else
                                model.selectRow(rowDelegate.index)
                            rowMenu.popup()
                        }
                    }
                    onClicked: mouse => {
                        if (mouse.button !== Qt.LeftButton)
                            return
                        root.context.lines.selectRow(rowDelegate.index,
                            (mouse.modifiers & Qt.ControlModifier) !== 0,
                            (mouse.modifiers & Qt.ShiftModifier) !== 0)
                    }
                    onDoubleClicked: mouse => {
                        if (mouse.button !== Qt.LeftButton)
                            return
                        root.context.lines.selectRow(rowDelegate.index)
                        root.requestTextFocus()
                    }
                }
            }
            Connections {
                target: root.context ? root.context.lines : null
                function onActiveRowChanged() {
                    if (root.context.lines.activeRow >= 0)
                        list.positionViewAtIndex(root.context.lines.activeRow, ListView.Contain)
                }
            }
        }
    }

    Menu {
        id: rowMenu
        MenuItem { text: qsTr("Insert Before"); enabled: root.context && !root.context.karaoke.active; onTriggered: root.context.insertBeforeActive() }
        MenuItem { text: qsTr("Insert After"); enabled: root.context && !root.context.karaoke.active; onTriggered: root.context.insertAfterActive() }
        MenuSeparator { }
        MenuItem { text: qsTr("Duplicate"); enabled: root.context && root.context.lines.selectedCount > 0 && !root.context.karaoke.active; onTriggered: root.context.duplicateSelected() }
        MenuItem { text: qsTr("Delete"); enabled: root.context && root.context.lines.selectedCount > 0 && !root.context.karaoke.active; onTriggered: root.context.deleteSelected() }
        MenuItem { text: qsTr("Copy"); enabled: root.context && root.context.lines.selectedCount > 0; onTriggered: root.context.copySelected() }
        MenuItem { text: qsTr("Cut"); enabled: root.context && root.context.lines.selectedCount > 0 && !root.context.karaoke.active; onTriggered: root.context.cutSelected() }
        MenuItem { text: qsTr("Paste"); enabled: root.context && root.context.canPasteRows && !root.context.karaoke.active; onTriggered: root.context.pasteRows() }
        MenuSeparator { }
        MenuItem { text: qsTr("Join with line breaks"); enabled: root.context && root.context.lines.selectedCount > 1 && !root.context.karaoke.active; onTriggered: root.context.joinSelected() }
        MenuItem { text: qsTr("Toggle Comment"); enabled: root.context && root.context.lines.selectedCount > 0 && !root.context.karaoke.active; onTriggered: root.context.toggleSelectedComments() }
        MenuItem { text: qsTr("Move Up"); enabled: root.context && root.context.lines.selectedCount > 0 && !root.context.karaoke.active; onTriggered: root.context.moveSelectedUp() }
        MenuItem { text: qsTr("Move Down"); enabled: root.context && root.context.lines.selectedCount > 0 && !root.context.karaoke.active; onTriggered: root.context.moveSelectedDown() }
    }
}
