import QtQuick
import QtQuick.Controls
import QtQuick.Dialogs as Dialogs
import QtQuick.Layouts
import Yoake

ApplicationWindow {
    id: window
    width: 1440
    height: 900
    minimumWidth: 980
    minimumHeight: 640
    visible: true
    title: Documents.currentDocument
           ? (Documents.currentDocument.modified ? "*" : "") + Documents.currentDocument.title + " — Yoake"
           : "Yoake"
    color: Theme.palette.background
    palette.window: Theme.palette.background
    palette.windowText: Theme.palette.text
    palette.base: Theme.palette.surfaceRaised
    palette.alternateBase: Theme.palette.surface
    palette.text: Theme.palette.text
    palette.button: Theme.palette.surface
    palette.buttonText: Theme.palette.text
    palette.brightText: Theme.palette.warning
    palette.highlight: Theme.palette.selection
    palette.highlightedText: Theme.palette.text
    palette.placeholderText: Theme.palette.textMuted
    palette.mid: Theme.palette.border
    palette.dark: Theme.palette.separator

    property var pendingCloseDocument: null
    property bool saveClosesDocument: false

    function textInputOwnsKeys() {
        const pane = documentPaneRepeater.itemAt(Documents.currentIndex)
        if (pane && pane.textEditorFocused)
            return true
        let item = window.activeFocusItem
        while (item && item !== window) {
            if (item instanceof TextInput || item instanceof TextEdit)
                return true
            item = item.parent
        }
        return false
    }

    function rowShortcut(sequence) {
        return textInputOwnsKeys() ? "" : sequence
    }

    function openFind(replace) {
        const pane = documentPaneRepeater.itemAt(Documents.currentIndex)
        if (pane)
            pane.openFind(replace)
    }

    function findNext(backwards) {
        const pane = documentPaneRepeater.itemAt(Documents.currentIndex)
        if (pane)
            pane.findNext(backwards)
    }

    onClosing: close => {
        if (!Documents.requestApplicationClose())
            close.accepted = false
    }

    menuBar: MenuBar {
        Menu {
            title: qsTr("&File")
            Action { text: qsTr("&New"); shortcut: StandardKey.New; onTriggered: Documents.createDocument() }
            Action { text: qsTr("&Open…"); shortcut: StandardKey.Open; onTriggered: subtitleOpenDialog.open() }
            MenuSeparator { }
            Action { text: qsTr("&Save"); shortcut: StandardKey.Save; onTriggered: Documents.saveCurrent() }
            Action { text: qsTr("Save &As…"); shortcut: StandardKey.SaveAs; onTriggered: { pendingCloseDocument = Documents.currentDocument; saveClosesDocument = false; subtitleSaveDialog.open() } }
            MenuSeparator { }
            Action { text: qsTr("&Close Tab"); shortcut: StandardKey.Close; onTriggered: Documents.requestClose(Documents.currentDocument) }
            Action { text: qsTr("E&xit"); shortcut: StandardKey.Quit; onTriggered: window.close() }
        }
        Menu {
            title: qsTr("&Edit")
            Action {
                text: Documents.currentDocument && Documents.currentDocument.undoText
                      ? qsTr("Undo %1").arg(Documents.currentDocument.undoText) : qsTr("Undo")
                shortcut: StandardKey.Undo
                enabled: Documents.currentDocument ? Documents.currentDocument.canUndo && !Documents.currentDocument.karaoke.active : false
                onTriggered: Documents.currentDocument.undo()
            }
            Action {
                text: Documents.currentDocument && Documents.currentDocument.redoText
                      ? qsTr("Redo %1").arg(Documents.currentDocument.redoText) : qsTr("Redo")
                shortcut: StandardKey.Redo
                enabled: Documents.currentDocument ? Documents.currentDocument.canRedo && !Documents.currentDocument.karaoke.active : false
                onTriggered: Documents.currentDocument.redo()
            }
            MenuSeparator { }
            Action { text: qsTr("Cut Subtitle Rows"); enabled: Documents.currentDocument ? Documents.currentDocument.lines.selectedCount > 0 && !Documents.currentDocument.karaoke.active : false; onTriggered: Documents.currentDocument.cutSelected() }
            Action { text: qsTr("Copy Subtitle Rows"); enabled: Documents.currentDocument ? Documents.currentDocument.lines.selectedCount > 0 : false; onTriggered: Documents.currentDocument.copySelected() }
            Action { text: qsTr("Paste Subtitle Rows"); enabled: Documents.currentDocument ? Documents.currentDocument.canPasteRows && !Documents.currentDocument.karaoke.active : false; onTriggered: Documents.currentDocument.pasteRows() }
            Action { text: qsTr("Select All Lines"); enabled: Documents.currentDocument ? Documents.currentDocument.lines.count > 0 : false; onTriggered: Documents.currentDocument.lines.selectAll() }
            MenuSeparator { }
            Action { text: qsTr("Find…"); shortcut: "Ctrl+F"; enabled: Documents.currentDocument !== null; onTriggered: window.openFind(false) }
            Action { text: qsTr("Replace…"); shortcut: "Ctrl+H"; enabled: Documents.currentDocument !== null; onTriggered: window.openFind(true) }
        }
        Menu {
            title: qsTr("&Subtitle")
            Action { text: qsTr("Insert Before"); shortcut: window.rowShortcut("Shift+Insert"); enabled: Documents.currentDocument && !Documents.currentDocument.karaoke.active; onTriggered: Documents.currentDocument.insertBeforeActive() }
            Action { text: qsTr("Insert After"); shortcut: window.rowShortcut("Insert"); enabled: Documents.currentDocument && !Documents.currentDocument.karaoke.active; onTriggered: Documents.currentDocument.insertAfterActive() }
            Action { text: qsTr("Duplicate Selected Lines"); shortcut: window.rowShortcut("Ctrl+D"); enabled: Documents.currentDocument && Documents.currentDocument.lines.selectedCount > 0 && !Documents.currentDocument.karaoke.active; onTriggered: Documents.currentDocument.duplicateSelected() }
            Action { text: qsTr("Delete Selected Lines"); shortcut: window.rowShortcut("Ctrl+Delete"); enabled: Documents.currentDocument && Documents.currentDocument.lines.selectedCount > 0 && !Documents.currentDocument.karaoke.active; onTriggered: Documents.currentDocument.deleteSelected() }
            Action { text: qsTr("Join Selected with Line Breaks"); shortcut: window.rowShortcut("Ctrl+J"); enabled: Documents.currentDocument && Documents.currentDocument.lines.selectedCount > 1 && !Documents.currentDocument.karaoke.active; onTriggered: Documents.currentDocument.joinSelected() }
            Action { text: qsTr("Split at Cursor"); shortcut: window.rowShortcut("Ctrl+Enter"); enabled: Documents.currentDocument && Documents.currentDocument.lines.activeRow >= 0 && !Documents.currentDocument.karaoke.active; onTriggered: { const pane = documentPaneRepeater.itemAt(Documents.currentIndex); if (pane) pane.splitAtCursor() } }
            Action { text: qsTr("Toggle Comment"); shortcut: window.rowShortcut("Alt+C"); enabled: Documents.currentDocument && Documents.currentDocument.lines.selectedCount > 0 && !Documents.currentDocument.karaoke.active; onTriggered: Documents.currentDocument.toggleSelectedComments() }
            Action { text: qsTr("Move Up"); shortcut: window.rowShortcut("Ctrl+Up"); enabled: Documents.currentDocument && Documents.currentDocument.lines.selectedCount > 0 && !Documents.currentDocument.karaoke.active; onTriggered: Documents.currentDocument.moveSelectedUp() }
            Action { text: qsTr("Move Down"); shortcut: window.rowShortcut("Ctrl+Down"); enabled: Documents.currentDocument && Documents.currentDocument.lines.selectedCount > 0 && !Documents.currentDocument.karaoke.active; onTriggered: Documents.currentDocument.moveSelectedDown() }
        }
        Menu {
            title: qsTr("&Timing")
            Action {
                text: qsTr("Set Selected Start to Current Position")
                shortcut: window.rowShortcut("Ctrl+1")
                enabled: Documents.currentDocument && Documents.currentDocument.lines.selectedCount > 0 && Documents.currentDocument.media.hasMedia && !Documents.currentDocument.karaoke.active
                onTriggered: Documents.currentDocument.setSelectedStartToPosition()
            }
            Action {
                text: qsTr("Set Selected End to Current Position")
                shortcut: window.rowShortcut("Ctrl+2")
                enabled: Documents.currentDocument && Documents.currentDocument.lines.selectedCount > 0 && Documents.currentDocument.media.hasMedia && !Documents.currentDocument.karaoke.active
                onTriggered: Documents.currentDocument.setSelectedEndToPosition()
            }
            Action { text: qsTr("Split at Current Position"); shortcut: window.rowShortcut("Alt+Shift+S"); enabled: Documents.currentDocument && Documents.currentDocument.lines.activeRow >= 0 && Documents.currentDocument.media.hasMedia && !Documents.currentDocument.karaoke.active; onTriggered: { const pane = documentPaneRepeater.itemAt(Documents.currentIndex); if (pane) pane.splitAtCurrentPosition() } }
            MenuSeparator { }
            Action { text: qsTr("Shift Earlier 100 ms"); enabled: Documents.currentDocument && Documents.currentDocument.lines.selectedCount > 0 && !Documents.currentDocument.karaoke.active; onTriggered: Documents.currentDocument.shiftSelectedTiming(-100) }
            Action { text: qsTr("Shift Later 100 ms"); enabled: Documents.currentDocument && Documents.currentDocument.lines.selectedCount > 0 && !Documents.currentDocument.karaoke.active; onTriggered: Documents.currentDocument.shiftSelectedTiming(100) }
            Action { text: qsTr("Shift Earlier 500 ms"); enabled: Documents.currentDocument && Documents.currentDocument.lines.selectedCount > 0 && !Documents.currentDocument.karaoke.active; onTriggered: Documents.currentDocument.shiftSelectedTiming(-500) }
            Action { text: qsTr("Shift Later 500 ms"); enabled: Documents.currentDocument && Documents.currentDocument.lines.selectedCount > 0 && !Documents.currentDocument.karaoke.active; onTriggered: Documents.currentDocument.shiftSelectedTiming(500) }
        }
        Menu {
            title: qsTr("&Video")
            Action { text: qsTr("Open Media…"); onTriggered: mediaOpenDialog.open() }
            Action { text: qsTr("Close Media"); enabled: Documents.currentDocument && Documents.currentDocument.media.hasMedia; onTriggered: Documents.currentDocument.media.close() }
            MenuSeparator { }
            Action { text: qsTr("Previous Frame"); shortcut: window.rowShortcut("Left"); enabled: Documents.currentDocument ? Documents.currentDocument.media.hasVideo : false; onTriggered: Documents.currentDocument.media.stepFrames(-1) }
            Action { text: qsTr("Next Frame"); shortcut: window.rowShortcut("Right"); enabled: Documents.currentDocument ? Documents.currentDocument.media.hasVideo : false; onTriggered: Documents.currentDocument.media.stepFrames(1) }
        }
        Menu {
            title: qsTr("&Audio")
            Action { text: qsTr("Open Media Audio…"); onTriggered: mediaOpenDialog.open() }
            Action { text: qsTr("Play / Pause"); shortcut: window.rowShortcut("Space"); enabled: Documents.currentDocument ? Documents.currentDocument.media.hasMedia : false; onTriggered: Documents.currentDocument.media.togglePlayback() }
            Action { text: qsTr("Stop"); enabled: Documents.currentDocument ? Documents.currentDocument.media.hasMedia : false; onTriggered: Documents.currentDocument.media.stop() }
        }
        Menu {
            title: qsTr("&View")
            Menu {
                title: qsTr("Theme")
                Repeater {
                    model: Theme.availableThemes
                    MenuItem {
                        required property string modelData
                        text: modelData
                        checkable: true
                        checked: Theme.currentTheme === modelData
                        onTriggered: Theme.currentTheme = modelData
                    }
                }
            }
        }
        Menu {
            title: qsTr("&Help")
            Action { text: qsTr("About Yoake"); onTriggered: aboutDialog.open() }
        }
    }

    Shortcut {
        sequence: "F3"
        enabled: Documents.currentDocument !== null
        onActivated: window.findNext(false)
    }
    Shortcut {
        sequence: "Shift+F3"
        enabled: Documents.currentDocument !== null
        onActivated: window.findNext(true)
    }

    ColumnLayout {
        anchors.fill: parent
        spacing: 0

        ToolBar {
            Layout.fillWidth: true
            implicitHeight: 36
            background: Rectangle {
                color: Theme.palette.surface
                border.color: Theme.palette.border
            }
            RowLayout {
                anchors.fill: parent
                anchors.leftMargin: 4
                anchors.rightMargin: 4
                spacing: 2

                ToolButton { text: qsTr("New"); onClicked: Documents.createDocument() }
                ToolButton { text: qsTr("Open"); onClicked: subtitleOpenDialog.open() }
                ToolButton {
                    text: qsTr("Save")
                    enabled: Documents.currentDocument !== null
                    onClicked: Documents.saveCurrent()
                }
                ToolSeparator { }
                ToolButton {
                    text: qsTr("Video")
                    enabled: Documents.currentDocument !== null
                    onClicked: mediaOpenDialog.open()
                    ToolTip.visible: hovered
                    ToolTip.text: qsTr("Open indexed video/audio")
                }
                ToolSeparator { }
                ToolButton {
                    text: qsTr("Undo")
                    enabled: Documents.currentDocument
                             ? Documents.currentDocument.canUndo && !Documents.currentDocument.karaoke.active : false
                    onClicked: Documents.currentDocument.undo()
                }
                ToolButton {
                    text: qsTr("Redo")
                    enabled: Documents.currentDocument
                             ? Documents.currentDocument.canRedo && !Documents.currentDocument.karaoke.active : false
                    onClicked: Documents.currentDocument.redo()
                }
                ToolSeparator { }
                ToolButton {
                    text: qsTr("Insert")
                    enabled: Documents.currentDocument && !Documents.currentDocument.karaoke.active
                    onClicked: Documents.currentDocument.insertAfterActive()
                }
                ToolButton {
                    text: qsTr("Delete")
                    enabled: Documents.currentDocument
                             ? Documents.currentDocument.lines.selectedCount > 0
                               && !Documents.currentDocument.karaoke.active : false
                    onClicked: Documents.currentDocument.deleteSelected()
                }
                ToolSeparator { }
                ToolButton {
                    text: qsTr("Prev")
                    enabled: Documents.currentDocument && Documents.currentDocument.media.hasVideo
                    onClicked: Documents.currentDocument.media.stepFrames(-1)
                }
                ToolButton {
                    text: Documents.currentDocument && Documents.currentDocument.media.playing
                          ? qsTr("Pause") : qsTr("Play")
                    enabled: Documents.currentDocument
                             && (Documents.currentDocument.media.hasVideo || Documents.currentDocument.media.hasAudio)
                    onClicked: Documents.currentDocument.media.togglePlayback()
                }
                ToolButton {
                    text: qsTr("Next")
                    enabled: Documents.currentDocument && Documents.currentDocument.media.hasVideo
                    onClicked: Documents.currentDocument.media.stepFrames(1)
                }
                Item { Layout.fillWidth: true }
                Label {
                    text: {
                        if (!Documents.currentDocument || !Documents.currentDocument.media.hasMedia)
                            return qsTr("No media")
                        const media = Documents.currentDocument.media
                        if (media.indexing)
                            return qsTr("FFMS2 indexing %1%").arg(Math.round(media.indexingProgress * 100))
                        if (!media.hasVideo && !media.hasAudio)
                            return qsTr("FFMS2 index unavailable")
                        const timing = media.variableFrameRate ? qsTr("FFMS2 VFR") : qsTr("FFMS2")
                        return timing + (media.indexCacheReused
                                         ? qsTr(" · index cache reused")
                                         : qsTr(" · index built"))
                    }
                    color: Theme.palette.textMuted
                }
            }
        }

        TabBar {
            id: tabs
            Layout.fillWidth: true
            implicitHeight: 32
            currentIndex: Documents.currentIndex
            onCurrentIndexChanged: if (currentIndex >= 0) Documents.currentIndex = currentIndex
            background: Rectangle { color: Theme.palette.tab; border.color: Theme.palette.border }

            Repeater {
                id: documentTabRepeater
                model: Documents
                TabButton {
                    id: tabButton
                    required property string documentTitle
                    required property bool documentModified
                    required property var documentContext
                    required property int index
                    width: Math.max(150, Math.min(260, implicitWidth))
                    height: 32
                    contentItem: RowLayout {
                        spacing: 6
                        Label {
                            Layout.fillWidth: true
                            text: (tabButton.documentModified ? "● " : "") + tabButton.documentTitle
                            color: tabButton.checked ? Theme.palette.text : Theme.palette.textMuted
                            elide: Text.ElideMiddle
                        }
                        ToolButton {
                            text: "×"
                            flat: true
                            Layout.preferredWidth: 24
                            Layout.preferredHeight: 24
                            padding: 0
                            onClicked: Documents.requestClose(tabButton.documentContext)
                            ToolTip.visible: hovered
                            ToolTip.text: qsTr("Close tab")
                        }
                    }
                    background: Rectangle {
                        color: tabButton.checked ? Theme.palette.tabActive
                                                 : tabButton.hovered ? Theme.palette.hover : Theme.palette.tab
                        border.color: Theme.palette.border
                    }
                }
            }
        }

        StackLayout {
            Layout.fillWidth: true
            Layout.fillHeight: true
            currentIndex: Documents.currentIndex
            Repeater {
                id: documentPaneRepeater
                model: Documents
                DocumentPane {
                    required property var documentContext
                    context: documentContext
                }
            }
        }

        Rectangle {
            Layout.fillWidth: true
            implicitHeight: 24
            color: Theme.palette.surface
            border.color: Theme.palette.border
            RowLayout {
                anchors.fill: parent
                anchors.leftMargin: 10
                anchors.rightMargin: 10
                Label { text: Documents.loading ? qsTr("Opening subtitle…") : qsTr("Ready"); color: Theme.palette.textMuted }
                Item { Layout.fillWidth: true }
                Label {
                    text: Documents.currentDocument && Documents.currentDocument.media.hasMedia
                          ? qsTr("%1 ms · frame %2").arg(Documents.currentDocument.media.positionMs).arg(Documents.currentDocument.media.currentFrame)
                          : qsTr("No media")
                    color: Theme.palette.textMuted
                }
            }
        }
    }

    Dialogs.FileDialog {
        id: subtitleOpenDialog
        title: qsTr("Open subtitle document")
        nameFilters: [qsTr("Advanced SubStation Alpha (*.ass)"), qsTr("All files (*)")]
        fileMode: Dialogs.FileDialog.OpenFiles
        onAccepted: {
            for (let index = 0; index < selectedFiles.length; ++index)
                Documents.openDocument(selectedFiles[index])
        }
    }
    Dialogs.FileDialog {
        id: subtitleSaveDialog
        title: qsTr("Save subtitle document")
        nameFilters: [qsTr("Advanced SubStation Alpha (*.ass)")]
        fileMode: Dialogs.FileDialog.SaveFile
        defaultSuffix: "ass"
        onAccepted: {
            if (saveClosesDocument)
                Documents.saveAndClose(pendingCloseDocument, selectedFile)
            else
                Documents.save(pendingCloseDocument, selectedFile)
        }
        onRejected: if (saveClosesDocument) Documents.cancelApplicationClose()
    }
    Dialogs.FileDialog {
        id: mediaOpenDialog
        title: qsTr("Open media")
        nameFilters: [qsTr("Media files (*.mkv *.mp4 *.webm *.avi *.mov *.mp3 *.wav *.flac *.ogg)"), qsTr("All files (*)")]
        onAccepted: if (Documents.currentDocument) Documents.currentDocument.media.open(selectedFile)
    }

    Dialog {
        id: closeDialog
        modal: true
        anchors.centerIn: parent
        title: qsTr("Unsaved subtitle document")
        standardButtons: Dialog.Save | Dialog.Discard | Dialog.Cancel
        property string documentTitle: ""
        Label {
            width: 420
            wrapMode: Text.WordWrap
            text: qsTr("Save changes to %1 before closing?").arg(closeDialog.documentTitle)
            color: Theme.palette.text
        }
        onAccepted: Documents.saveAndClose(pendingCloseDocument)
        onDiscarded: Documents.discardAndClose(pendingCloseDocument)
        onRejected: Documents.cancelApplicationClose()
    }

    Dialog {
        id: aboutDialog
        modal: true
        anchors.centerIn: parent
        title: qsTr("About Yoake")
        standardButtons: Dialog.Ok
        Label {
            width: 400
            wrapMode: Text.WordWrap
            color: Theme.palette.text
            text: qsTr("Yoake 0.1\nA Qt-native subtitle editor founded on Aegisub Toshi-ban workflows, with independent documents and Mangetsu-first rendering.")
        }
    }

    Dialog {
        id: errorDialog
        modal: true
        anchors.centerIn: parent
        title: qsTr("Yoake")
        standardButtons: Dialog.Ok
        property string message: ""
        Label { width: 440; wrapMode: Text.WordWrap; text: errorDialog.message; color: Theme.palette.text }
    }

    Connections {
        target: Documents
        function onCloseConfirmationRequested(document, title) {
            pendingCloseDocument = document
            closeDialog.documentTitle = title
            closeDialog.open()
        }
        function onSavePathRequested(document, closeAfterSave) {
            pendingCloseDocument = document
            saveClosesDocument = closeAfterSave
            subtitleSaveDialog.open()
        }
        function onOperationFailed(message) {
            errorDialog.message = message
            errorDialog.open()
        }
        function onApplicationCloseReady() { window.close() }
    }
}
