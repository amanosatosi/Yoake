import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

Item {
    id: root
    required property var context

    SplitView {
        anchors.fill: parent
        orientation: Qt.Horizontal

        Item {
            SplitView.preferredWidth: parent.width * 0.47
            SplitView.minimumWidth: 360
            ColumnLayout {
                anchors.fill: parent
                spacing: 1
                VideoPane { Layout.fillWidth: true; Layout.fillHeight: true; context: root.context }
                AudioPane { Layout.fillWidth: true; Layout.preferredHeight: 190; context: root.context }
            }
        }

        Item {
            SplitView.fillWidth: true
            SplitView.minimumWidth: 480
            ColumnLayout {
                anchors.fill: parent
                spacing: 1
                AssEditor { Layout.fillWidth: true; Layout.preferredHeight: 245; context: root.context }
                SubtitleGrid { Layout.fillWidth: true; Layout.fillHeight: true; context: root.context }
            }
        }
    }
}
