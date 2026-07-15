import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

Item {
    id: root
    required property var context

    SplitView {
        anchors.fill: parent
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
                    SplitView.fillHeight: true
                    SplitView.minimumHeight: 190
                    context: root.context
                }
            }
        }

        SubtitleGrid {
            SplitView.fillHeight: true
            SplitView.minimumHeight: 180
            context: root.context
        }
    }
}
