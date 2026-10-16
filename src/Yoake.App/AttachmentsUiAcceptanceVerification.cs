using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yoake.Core.Commands;
using Yoake.Core.Logging;
using Yoake.Core.Subtitles;
using Yoake.UI;
using Yoake.UI.ViewModels;

namespace Yoake.App;

internal sealed class AttachmentsUiAcceptanceVerification(IClassicDesktopStyleApplicationLifetime desktop,MainWindow window,MainWindowViewModel model,StartupOptions options)
{
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMilliseconds(120)};
    private bool _ticking;private int _stage;private AttachmentsWindow? _manager;private AttachmentPathWindow? _path;
    private object? _draft;private Guid? _document;private string _original="",_withAttachment="";
    private AssAttachment[] _rows=[];private Task<bool>? _load;
    private readonly DateTime _deadline=DateTime.UtcNow.AddMinutes(2);
    private string Root=>options.ProfileDirectory!;
    private string Script=>Path.Combine(Root,"attachment-script.ass");
    private string Fixture=>Path.Combine(Root,"日本語 fixture.bmp");
    private string UnsavedFixture=>Path.Combine(Root,"未保存.bmp");
    private string Output=>Path.Combine(Root,"output");
    private static byte[] Bytes=>Enumerable.Range(0,257).Select(i=>(byte)(i*73+19)).ToArray();
    private static string Source=>AssDocument.CreateEmpty().Serialize()+"Dialogue: 0,0:00:00.00,0:00:02.00,Default,,0,0,0,,{\\future(x)\\distort(1,2,3,4,5,6,7,8)}原文\r\n[Unknown]\nOpaque: preserved\r\n";
    public void Start(){_timer.Tick+=Tick;_timer.Start();}
    private T InManager<T>(string name)where T:Control=>_manager!.FindControl<T>(name)!;
    private async void Tick(object? sender,EventArgs args)
    {
        if(_ticking||!window.IsVisible||window.Bounds.Width<=0)return;_ticking=true;options.MarkStartupVerified();
        try
        {
            Require(DateTime.UtcNow<_deadline,"Attachment acceptance timeout at stage "+_stage);
            if(options.AttachmentRecovery)await Recover();else await Exercise();
        }
        catch(Exception error){_timer.Stop();Program.Fatal("Packaged attachments acceptance at stage "+_stage,error);desktop.Shutdown(1);}
        finally{_ticking=false;}
    }
    private async Task Exercise()
    {
        switch(_stage++)
        {
            case 0:
                Require(window.TryGetPlatformHandle() is {Handle:not 0},"Real native attachment editor window.");
                Directory.CreateDirectory(Output);File.WriteAllBytes(Fixture,Bytes);File.WriteAllBytes(UnsavedFixture,Bytes.Reverse().ToArray());File.WriteAllText(Script,Source);
                Require(model.OpenSubtitle(Script),"Open deterministic source.");_document=model.ActiveDocumentId;_original=model.ActiveEditor!.Document.Serialize();
                _load=model.OpenMediaAsync(Path.Combine(options.VerificationMedia!,"av.avi"));return;
            case 1:
                if(!_load!.IsCompleted){_stage--;return;}Require(await _load,"Native media retained by manager.");
                window.FindControl<TextBox>("SubtitleText")!.SetCurrentValue(TextBox.TextProperty,"pending 日本語\ntext");window.FindControl<TextBox>("LineStart")!.SetCurrentValue(TextBox.TextProperty,"invalid");return;
            case 2:
                _draft=model.Draft;OpenManager();return;
            case 3:
                _manager=window.OwnedWindows.OfType<AttachmentsWindow>().SingleOrDefault();if(_manager is null){_stage--;return;}
                Require(_manager.IsVisible&&_manager.TryGetPlatformHandle() is {Handle:not 0},"Real native attachments manager.");
                Press(InManager<Button>("AttachmentImport"),Key.Enter);return;
            case 4:
                _path=_manager!.OwnedWindows.OfType<AttachmentPathWindow>().SingleOrDefault();if(_path is null){_stage--;return;}
                Require(_path.TryGetPlatformHandle() is {Handle:not 0},"Real import dialog.");
                _path.FindControl<ComboBox>("AttachmentImportKind")!.SelectedItem=AssAttachmentKind.Graphic;
                var text=_path.FindControl<TextBox>("AttachmentPaths")!;text.SetCurrentValue(TextBox.TextProperty,Fixture);text.GetVisualDescendants().OfType<TextPresenter>().Single().PreeditText="未確定";Press(text,Key.Enter);return;
            case 5:
                Require(_path!.IsVisible,"IME preedit Enter cannot import.");_path.FindControl<TextBox>("AttachmentPaths")!.GetVisualDescendants().OfType<TextPresenter>().Single().PreeditText=null;
                Press(_path.FindControl<Button>("AttachmentPathConfirm")!,Key.Enter);return;
            case 6:
                _rows=Rows();if(_rows.Length!=1||model.AttachmentBusy){_stage--;return;}
                Require(_rows[0].Name==Path.GetFileName(Fixture)&&_rows[0].Kind==AssAttachmentKind.Graphic&&_rows[0].DecodedSize==Bytes.Length,"Actual list displays filename, type and decoded size.");
                Require(ReferenceEquals(model.Draft,_draft)&&model.Draft!.Start=="invalid"&&model.Draft.Text==@"pending 日本語\Ntext"&&window.FindControl<TextBox>("SubtitleText")!.Text=="pending 日本語\ntext","Import retains the uncommitted invalid draft, its lossless ASS source and displayed hard break.");
                _withAttachment=model.ActiveEditor!.Document.Serialize();Require(_withAttachment.Contains("Opaque: preserved")&&model.VideoFrame is not null&&model.HasAudio,"Source and native media retained.");
                InManager<ListBox>("AttachmentList").SelectedItem=_rows[0];File.WriteAllBytes(Path.Combine(Output,_rows[0].ExtractionName),[9,8,7]);Press(InManager<Button>("AttachmentExtract"),Key.Enter);return;
            case 7:
                if(!PrepareExtract()){_stage--;return;}return;
            case 8:
                var keep=window.OwnedWindows.SingleOrDefault(w=>w.Name=="AttachmentOverwriteDialog");if(keep is null){_stage--;return;}
                Press(keep.FindControl<Button>("AttachmentOverwriteCancel")!,Key.Enter);return;
            case 9:
                if(model.AttachmentBusy){_stage--;return;}
                Require(File.ReadAllBytes(Path.Combine(Output,Path.GetFileName(Fixture))).SequenceEqual(new byte[]{9,8,7}),"Declined overwrite keeps existing bytes.");
                Require(model.ActiveEditor!.Document.Serialize()==_withAttachment&&ReferenceEquals(model.Draft,_draft),"Extraction has no document/draft mutation.");
                Press(InManager<Button>("AttachmentExtractAll"),Key.Enter);return;
            case 10:
                if(!PrepareExtract()){_stage--;return;}return;
            case 11:
                var replace=window.OwnedWindows.SingleOrDefault(w=>w.Name=="AttachmentOverwriteDialog");if(replace is null){_stage--;return;}
                Press(replace.FindControl<Button>("AttachmentOverwriteConfirm")!,Key.Enter);return;
            case 12:
                if(model.AttachmentBusy){_stage--;return;}
                Require(File.ReadAllBytes(Path.Combine(Output,Path.GetFileName(Fixture))).SequenceEqual(Bytes),"Atomic extraction returns exact deterministic bytes.");
                Press(InManager<Button>("AttachmentRemove"),Key.Enter);return;
            case 13:
                if(Rows().Length!=0){_stage--;return;}
                Require(ReferenceEquals(model.Draft,_draft),"Remove preserves pending draft.");Press(InManager<Button>("AttachmentUndo"),Key.Enter);return;
            case 14:
                if(Rows().Length!=1){_stage--;return;}
                Require(model.ActiveEditor!.Document.Serialize()==_withAttachment&&ReferenceEquals(model.Draft,_draft),"One undo restores payload and keeps draft.");
                Press(InManager<Button>("AttachmentRedo"),Key.Enter);return;
            case 15:
                if(Rows().Length!=0){_stage--;return;}Press(InManager<Button>("AttachmentUndo"),Key.Enter);return;
            case 16:
                if(Rows().Length!=1){_stage--;return;}Press(InManager<Button>("AttachmentClose"),Key.Enter);return;
            case 17:
                if(window.OwnedWindows.OfType<AttachmentsWindow>().Any()){_stage--;return;}
                await Invoke(CommandIds.EditCancel);await Invoke(CommandIds.SubtitleSave);
                Require(File.ReadAllText(Script).Contains("filename: "+Path.GetFileName(Fixture)),"Real Save serializes embedded attachment.");
                _load=model.Registry.InvokeAsync(CommandIds.SubtitleRevert,new(model.ActiveDocumentId)).AsTask();return;
            case 18:
                var reload=window.OwnedWindows.SingleOrDefault(w=>w.Title=="Reload subtitle");if(reload is null){_stage--;return;}
                Press(reload.GetVisualDescendants().OfType<Button>().Single(b=>b.Content as string=="Reload"),Key.Enter);return;
            case 19:
                if(!_load!.IsCompleted){_stage--;return;}Require(await _load,"Reload through production command/dialog.");OpenManager();return;
            case 20:
                _manager=window.OwnedWindows.OfType<AttachmentsWindow>().SingleOrDefault();if(_manager is null||Rows().Length!=1){_stage--;return;}
                Require(Rows()[0].Decode().SequenceEqual(Bytes),"Save/reopen retains exact payload.");Press(InManager<Button>("AttachmentRemove"),Key.Enter);return;
            case 21:
                if(Rows().Length!=0){_stage--;return;}Press(InManager<Button>("AttachmentClose"),Key.Enter);return;
            case 22:
                if(window.OwnedWindows.OfType<AttachmentsWindow>().Any()){_stage--;return;}
                await Invoke(CommandIds.SubtitleSave);Require(!File.ReadAllText(Script).Contains("filename: "+Path.GetFileName(Fixture)),"Remove then Save persists removal.");
                await Invoke(CommandIds.SubtitleNew);await Invoke(CommandIds.GridInsertAfter);Require(model.ActiveDocumentId!=_document,"Separate document tab.");
                Require((await model.ListAttachmentsAsync(model.ActiveDocumentId!.Value)).Length==0,"Attachment lists are document-local.");await Invoke(CommandIds.WorkspaceActivateTab,_document);
                await Invoke("settings/autosave",new AutosaveOptions(true,1,null));OpenManager();return;
            case 23:
                _manager=window.OwnedWindows.OfType<AttachmentsWindow>().SingleOrDefault();if(_manager is null){_stage--;return;}
                Press(InManager<Button>("AttachmentImport"),Key.Enter);return;
            case 24:
                _path=_manager!.OwnedWindows.OfType<AttachmentPathWindow>().SingleOrDefault();if(_path is null){_stage--;return;}
                _path.FindControl<ComboBox>("AttachmentImportKind")!.SelectedItem=AssAttachmentKind.Graphic;_path.FindControl<TextBox>("AttachmentPaths")!.SetCurrentValue(TextBox.TextProperty,UnsavedFixture);
                Press(_path.FindControl<Button>("AttachmentPathConfirm")!,Key.Enter);return;
            case 25:
                if(Rows().Length!=1||model.AttachmentBusy){_stage--;return;}
                // A valid pending draft remains paused while the modal manager
                // is open. The real timer must capture it and the new payload.
                model.Draft!.Text="uncommitted attachment recovery 日本語";return;
            case 26:
                var entries=await model.ListRecoveryAsync();
                if(!entries.Any(e=>e.Metadata?.OriginalPath==Script&&e.Metadata.Draft?.Values.GetValueOrDefault("Text")=="uncommitted attachment recovery 日本語")){_stage--;return;}
                Require(!File.ReadAllText(Script).Contains(Path.GetFileName(UnsavedFixture)),"Unpublished import remains absent from original file.");
                _timer.Stop();options.FinishVerification("READY: native attachment menu/manager, real import/type/path/IME, displayed name/type/decoded size, exact extraction/overwrite-decline/overwrite-confirm, remove/one-step undo/redo with draft retained, Save/reopen, persisted removal, doc isolation, native media retained; production autosave contains an unsaved new attachment and valid paused draft, ready for forced termination.\n");StartupDiagnostics.Complete();return;
        }
    }
    private async Task Recover()
    {
        switch(_stage++)
        {
            case 0:
                await Invoke("settings/autosave",new AutosaveOptions(false,60,null));
                window.FindControl<MenuItem>("FileMenu")!.IsSubMenuOpen=true;Press(window.FindControl<MenuItem>("RecoveryMenu")!,Key.Enter);return;
            case 1:
                var recovery=window.OwnedWindows.OfType<RecoveryWindow>().SingleOrDefault();if(recovery is null){_stage--;return;}
                var versions=recovery.FindControl<ListBox>("RecoveryVersions")!;
                if(versions.ItemCount==0){_stage--;return;}
                var entries=await model.ListRecoveryAsync();var entry=entries.Where(e=>e.Metadata?.OriginalPath==Script&&e.Metadata.Draft?.Values.GetValueOrDefault("Text")=="uncommitted attachment recovery 日本語").OrderByDescending(e=>e.Metadata!.Sequence).First();
                recovery.FindControl<ListBox>("RecoveryFiles")!.SelectedItem=entry.Group;versions.SelectedItem=versions.Items.OfType<RecoveryEntry>().Single(e=>e.Path==entry.Path);
                Press(recovery.FindControl<Button>("RecoveryOpen")!,Key.Enter);return;
            case 2:
                if(window.OwnedWindows.OfType<RecoveryWindow>().Any()){_stage--;return;}
                Require(model.ActiveSubtitlePath is null&&model.Draft!.Text=="uncommitted attachment recovery 日本語","Attachment-bearing recovery is an unsaved tab with the paused valid draft.");OpenManager();return;
            case 3:
                _manager=window.OwnedWindows.OfType<AttachmentsWindow>().SingleOrDefault();if(_manager is null||Rows().Length!=1){_stage--;return;}
                Require(Rows()[0].Name==Path.GetFileName(UnsavedFixture)&&Rows()[0].Decode().SequenceEqual(Bytes.Reverse()),"Imported unsaved binary payload survives real crash and independent restart.");
                Require(!File.ReadAllText(Script).Contains(Path.GetFileName(UnsavedFixture)),"Recovery never replaces original file.");
                Require(model.ActiveEditor!.Document.GetSectionValue("[Unknown]","Opaque")=="preserved"&&model.Events[0].Text.Contains("\\distort(1,2,3,4,5,6,7,8)"),"Unknown metadata and Mangetsu source retained.");
                _timer.Stop();options.FinishVerification("PASS: independent restarted native recovery of imported unsaved attachment plus valid uncommitted draft through actual File menu, recovery versions/buttons and attachment manager; binary bytes/name/type/size exact, original untouched, metadata/styles/Mangetsu tags preserved.\n");StartupDiagnostics.Complete();desktop.Shutdown(0);return;
        }
    }
    private AssAttachment[] Rows()=>_manager?.FindControl<ListBox>("AttachmentList")?.Items.OfType<AssAttachment>().ToArray()??[];
    private bool PrepareExtract()
    {
        _path=_manager!.OwnedWindows.OfType<AttachmentPathWindow>().SingleOrDefault();if(_path is null)return false;
        _path.FindControl<TextBox>("AttachmentPaths")!.SetCurrentValue(TextBox.TextProperty,Output);Press(_path.FindControl<Button>("AttachmentPathConfirm")!,Key.Enter);return true;
    }
    private void OpenManager(){window.FindControl<MenuItem>("FileMenu")!.IsSubMenuOpen=true;Press(window.FindControl<MenuItem>("AttachmentsMenu")!,Key.Enter);}
    private async Task Invoke(string id,object? parameter=null)=>Require(await model.Registry.InvokeAsync(id,new(model.ActiveDocumentId),parameter),"Attachment acceptance command: "+id);
    private static void Press(Control control,Key key)
    {
        (TopLevel.GetTopLevel(control) as Window)?.Activate();Require(control.Focus(),"Actual native control focus: "+control.Name);
        control.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Source=control,Key=key});
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
