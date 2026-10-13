using System.Text;
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

// CI kills the capture process after READY. The recovery phase is a separately
// launched process with the same on-disk profile and no in-memory shared state.
internal sealed class RecoveryUiAcceptanceVerification(IClassicDesktopStyleApplicationLifetime desktop,MainWindow window,MainWindowViewModel model,StartupOptions options)
{
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMilliseconds(120)};
    private int _stage;private bool _ticking;private OptionsWindow? _options;private RecoveryWindow? _dialog;
    private Guid? _originalId;private object? _originalDraft;private string _originalSource="";private RecoveryEntry[] _entries=[];
    private int _recoverIndex;
    private readonly DateTime _deadline=DateTime.UtcNow.AddMinutes(2);
    private string Root=>options.ProfileDirectory!;
    private string PathA=>Path.Combine(Root,"A","same.ass");private string PathB=>Path.Combine(Root,"B","same.ass");
    private static string Source=>AssDocument.CreateEmpty().Serialize()+"Dialogue: 0,0:00:00.00,0:00:02.00,Default,,0,0,0,,original\r\n[Future]\nOpaque: unchanged\r[Fonts]\r\nfontname: fixture.ttf\r\n!!!!\r\n";
    private TextBox Text=>window.FindControl<TextBox>("SubtitleText")!;
    public void Start(){_timer.Tick+=Tick;_timer.Start();}
    private async void Tick(object? sender,EventArgs args)
    {
        if(_ticking||!window.IsVisible||window.Bounds.Width<=0)return;_ticking=true;options.MarkStartupVerified();
        try
        {
            Require(DateTime.UtcNow<_deadline,"Recovery acceptance timed out at stage "+_stage);
            if(options.RecoveryCapture)await Capture();else await Recover();
        }
        catch(Exception error){_timer.Stop();Program.Fatal("Packaged recovery acceptance at stage "+_stage,error);desktop.Shutdown(1);}
        finally{_ticking=false;}
    }
    private async Task Capture()
    {
        switch(_stage++)
        {
            case 0:
                Require(window.TryGetPlatformHandle() is {Handle:not 0},"Real native capture window.");
                Directory.CreateDirectory(Path.GetDirectoryName(PathA)!);Directory.CreateDirectory(Path.GetDirectoryName(PathB)!);
                File.WriteAllText(PathA,Source,new UnicodeEncoding(false,true));File.WriteAllText(PathB,Source,new UTF8Encoding(true));
                Require(model.OpenSubtitle(PathA),"Open first original.");Text.Text="committed translation 日本語";return;
            case 1:
                Require(model.Draft!.Text=="committed translation 日本語","Real TextBox binding.");await Invoke(CommandIds.EditCommit);
                await Invoke(CommandIds.SubtitleNew);await Invoke(CommandIds.GridInsertAfter);Text.Text="never saved";return;
            case 2:
                await Invoke(CommandIds.EditCommit);Require(model.OpenSubtitle(PathB),"Open same basename in another directory.");
                window.FindControl<MenuItem>("EditMenu")!.IsSubMenuOpen=true;Press(window.FindControl<MenuItem>("OptionsMenu")!,Key.Enter);return;
            case 3:
                _options=window.OwnedWindows.OfType<OptionsWindow>().SingleOrDefault();if(_options is null){_stage--;return;}
                Require(_options.TryGetPlatformHandle() is {Handle:not 0},"Real Options dialog.");_options.FindControl<TabControl>("OptionsTabs")!.SelectedIndex=1;return;
            case 4:
                _options!.FindControl<NumericUpDown>("AutosaveInterval")!.Value=1;
                Require(_options.FindControl<CheckBox>("AutosaveEnabled")!.IsChecked==true,"Autosave defaults enabled.");
                Press(_options.FindControl<Button>("AutosaveApply")!,Key.Enter);return;
            case 5:
                Require(model.AutosaveEnabled&&model.AutosaveIntervalSeconds==1,"Actual Options settings command.");
                _options!.Close();Text.Text="pending\r\n日本語 é";window.FindControl<TextBox>("LineStart")!.Text="unfinished";return;
            case 6:
                var margin=window.FindControl<NumericUpDown>("MarginLeft")!;
                var input=margin.GetVisualDescendants().OfType<TextBox>().Single();input.Focus();input.Text="-";return;
            case 7:
                Require(model.Draft!.Text=="pending\r\n日本語 é"&&model.Draft.Start=="unfinished"&&model.Draft.MarginL=="-","Invalid timing/margin and raw multiline input reach the draft.");
                Text.GetVisualDescendants().OfType<TextPresenter>().Single().PreeditText="IME_UNCOMMITTED";
                return;
            case 8:
                var entries=await model.ListRecoveryAsync();
                if(!entries.Any(e=>e.Metadata?.OriginalPath==PathA)||!entries.Any(e=>e.Metadata?.OriginalPath is null&&e.Metadata?.Title.StartsWith("Untitled")==true)||
                    !entries.Any(e=>e.Metadata?.OriginalPath==PathB&&e.Metadata.Draft?.Values.GetValueOrDefault("MarginL")=="-")){_stage--;return;}
                Require(File.ReadAllText(PathA)==Source&&File.ReadAllText(PathB)==Source,"Autosaving never writes originals.");
                Require(model.SelectedEvent!.Text=="original"&&model.Draft!.Text.Contains("日本語"),"Draft stays uncommitted.");
                _timer.Stop();options.FinishVerification("READY: production timer published multiple independent encoded ASS/draft bundles; original files untouched; invalid raw draft and IME preedit isolation prepared for abrupt termination.\n");StartupDiagnostics.Complete();return;
        }
    }
    private async Task Recover()
    {
        switch(_stage++)
        {
            case 0:
                Require(window.TryGetPlatformHandle() is {Handle:not 0},"Real restarted native window.");
                await Invoke("settings/autosave",new AutosaveOptions(false,60,null));
                _entries=(await model.ListRecoveryAsync()).Where(e=>e.CanRecover).GroupBy(e=>e.Metadata!.DocumentId).Select(g=>g.OrderByDescending(e=>e.Metadata!.Sequence).First()).ToArray();
                Require(_entries.Length==3,"Independent process finds three originating documents.");
                Require(model.OpenSubtitle(PathA),"Keep original open while recovering.");_originalId=model.ActiveDocumentId;_originalSource=model.ActiveEditor!.Document.Serialize();
                Text.Text="already open pending draft";window.FindControl<TextBox>("LineStart")!.Text="still invalid";return;
            case 1:
                _originalDraft=model.Draft;OpenManager();return;
            case 2:
                _dialog=window.OwnedWindows.OfType<RecoveryWindow>().SingleOrDefault();if(_dialog is null){_stage--;return;}
                Require(_dialog.IsVisible&&_dialog.TryGetPlatformHandle() is {Handle:not 0},"Recovery manager is a real native dialog.");
                var files=_dialog.FindControl<ListBox>("RecoveryFiles")!;
                if(files.ItemCount==0){_stage--;return;}
                files.SelectedItem=_entries[_recoverIndex].Group;return;
            case 3:
                var versions=_dialog!.FindControl<ListBox>("RecoveryVersions")!;
                var selected=versions.Items.OfType<RecoveryEntry>().Single(e=>e.Path==_entries[_recoverIndex].Path);versions.SelectedItem=selected;
                Require(_dialog.FindControl<Button>("RecoveryOpen")!.IsEnabled,"Validated selected version is recoverable.");
                Press(_dialog.FindControl<Button>("RecoveryOpen")!,Key.Enter);return;
            case 4:
                if(window.OwnedWindows.OfType<RecoveryWindow>().Any()){_stage--;return;}
                var entry=_entries[_recoverIndex];Require(model.ActiveDocumentId!=_originalId&&model.ActiveSubtitlePath is null&&model.Tabs.Single(t=>t.Id==model.ActiveDocumentId).IsDirty,"Recovery uses a separate unsaved dirty tab.");
                Require(model.ActiveEditor!.Document.GetScriptInfo("PlayResX")=="1920","Script metadata restored.");
                if(entry.Metadata!.OriginalPath==PathA)
                {
                    Require(model.Events[0].Text=="committed translation 日本語","Committed translation recovered.");
                    Require(model.ActiveEditor.Document.CaptureSnapshot().SerializeBytes().AsSpan().StartsWith(new byte[]{255,254}),"UTF-16 BOM survives recovery.");
                }
                else if(entry.Metadata.OriginalPath==PathB)
                {
                    Require(model.Events[0].Text=="original"&&model.Draft!.Text=="pending\r\n日本語 é"&&model.Draft.Start=="unfinished"&&model.Draft.MarginL=="-","Invalid raw draft restored separately from committed source.");
                    Require(!model.Draft.Text.Contains("IME_UNCOMMITTED"),"IME preedit cannot be recovered as committed input.");
                    Require(model.ActiveEditor.Document.Serialize().Contains("fontname: fixture.ttf\r\n!!!!\r\n")&&model.ActiveEditor.Document.Styles.Count==1,"Attachments and styles retained in the ASS snapshot.");
                    Require(!model.CommitDraft(),"Recovered invalid draft cannot corrupt source.");await Invoke(CommandIds.EditCancel);
                }
                else Require(model.Events[0].Text=="never saved","Never-saved identity recovered.");
                Require(File.ReadAllText(PathA)==Source&&File.ReadAllText(PathB)==Source,"Recovery does not replace either original file.");
                if(++_recoverIndex<_entries.Length){OpenManager();_stage=2;return;}
                await Invoke(CommandIds.WorkspaceActivateTab,_originalId);return;
            case 5:
                Require(ReferenceEquals(model.Draft,_originalDraft)&&model.Draft!.Start=="still invalid"&&model.ActiveEditor!.Document.Serialize()==_originalSource,"Original draft, source and document identity survive recovery of other versions.");
                Require((await model.ListRecoveryAsync()).Length>=3,"Opening or declining recovery keeps valid versions.");
                _timer.Stop();options.FinishVerification("PASS: abrupt-process-termination/restart recovery through native File menu, grouped version lists and real recover buttons; three independent documents including identical basenames and never-saved identity; UTF-16 BOM, styles, metadata, raw attachments, uncommitted text/newlines, invalid timing/margin and IME preedit isolation; originals untouched and already-open invalid draft/source retained.\n");StartupDiagnostics.Complete();desktop.Shutdown(0);return;
        }
    }
    private void OpenManager(){window.FindControl<MenuItem>("FileMenu")!.IsSubMenuOpen=true;Press(window.FindControl<MenuItem>("RecoveryMenu")!,Key.Enter);}
    private async Task Invoke(string id,object? parameter=null)=>Require(await model.Registry.InvokeAsync(id,new(model.ActiveDocumentId),parameter),"Acceptance command: "+id);
    private static void Press(Control control,Key key)
    {
        (TopLevel.GetTopLevel(control) as Window)?.Activate();Require(control.Focus(),"Actual native control focus: "+control.Name);
        control.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Source=control,Key=key});
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
