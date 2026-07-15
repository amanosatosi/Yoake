import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

Rectangle {
    id: root
    required property var context
    color: Theme.palette.panel
    border.color: Theme.palette.border

    function timeText(ms) {
        const total = Math.max(0, Math.floor(ms / 10))
        const cs = total % 100
        const seconds = Math.floor(total / 100) % 60
        const minutes = Math.floor(total / 6000) % 60
        const hours = Math.floor(total / 360000)
        return hours + ":" + String(minutes).padStart(2, "0") + ":" + String(seconds).padStart(2, "0") + "." + String(cs).padStart(2, "0")
    }

    function cps(text, startMs, endMs) {
        const duration = Math.max(1, endMs - startMs)
        const plain = text.replace(/\{[^}]*\}/g, "").replace(/\\[Nnh]/g, " ")
        return Math.round(plain.length * 1000 / duration)
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
                Label { width: 38; text: qsTr("CPS"); color: Theme.palette.textMuted; anchors.verticalCenter: parent.verticalCenter }
                Label { width: 100; text: qsTr("Style"); color: Theme.palette.textMuted; anchors.verticalCenter: parent.verticalCenter }
                Label { text: qsTr("Text"); color: Theme.palette.textMuted; anchors.verticalCenter: parent.verticalCenter }
            }
        }
        ListView {
            id: list
            Layout.fillWidth: true
            Layout.fillHeight: true
            clip: true
            reuseItems: true
            cacheBuffer: 240
            boundsBehavior: Flickable.StopAtBounds
            model: root.context ? root.context.lines : null
            ScrollBar.vertical: ScrollBar { }
            delegate: Rectangle {
                id: rowDelegate
                required property int index
                required property int subtitleLayer
                required property int startMs
                required property int endMs
                required property string style
                required property string subtitleText
                required property bool comment
                required property bool selected
                required property bool active
                width: ListView.view.width
                height: 25
                color: active ? Theme.palette.active
                              : selected ? Theme.palette.selection
                              : index % 2 ? Theme.palette.gridAlternate : Theme.palette.panel
                Row {
                    anchors.fill: parent
                    anchors.leftMargin: 4
                    spacing: 4
                    Label { width: 34; text: rowDelegate.index + 1; color: Theme.palette.textMuted; anchors.verticalCenter: parent.verticalCenter }
                    Label { width: 32; text: rowDelegate.subtitleLayer; color: Theme.palette.text; anchors.verticalCenter: parent.verticalCenter }
                    Label { width: 86; text: root.timeText(rowDelegate.startMs); color: Theme.palette.text; anchors.verticalCenter: parent.verticalCenter }
                    Label { width: 86; text: root.timeText(rowDelegate.endMs); color: Theme.palette.text; anchors.verticalCenter: parent.verticalCenter }
                    Label { width: 38; text: root.cps(rowDelegate.subtitleText, rowDelegate.startMs, rowDelegate.endMs); color: Theme.palette.textMuted; anchors.verticalCenter: parent.verticalCenter }
                    Label { width: 100; text: rowDelegate.style; color: Theme.palette.textMuted; elide: Text.ElideRight; anchors.verticalCenter: parent.verticalCenter }
                    Label {
                        width: Math.max(0, rowDelegate.width - 414)
                        text: rowDelegate.subtitleText.replace(/\\N/g, " ↵ ")
                        color: rowDelegate.comment ? Theme.palette.gridComment : Theme.palette.text
                        elide: Text.ElideRight
                        anchors.verticalCenter: parent.verticalCenter
                    }
                }
                MouseArea {
                    anchors.fill: parent
                    acceptedButtons: Qt.LeftButton
                    onClicked: mouse => root.context.lines.selectRow(
                        rowDelegate.index,
                        (mouse.modifiers & Qt.ControlModifier) !== 0,
                        (mouse.modifiers & Qt.ShiftModifier) !== 0)
                }
            }
        }
    }
}
