using System.IO.Compression;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yoake.Core.Commands;
using Yoake.Core.Fonts;
using Yoake.Core.Hotkeys;
using Yoake.Core.Logging;
using Yoake.Core.Subtitles;
using Yoake.Native;
using Yoake.UI;
using Yoake.UI.ViewModels;

namespace Yoake.App;

internal sealed class FontCollectorUiAcceptanceVerification(IClassicDesktopStyleApplicationLifetime desktop,MainWindow window,MainWindowViewModel model,StartupOptions options)
{
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMilliseconds(100)};
    private bool _ready,_ticking;private int _stage;private FontCollectorWindow? _dialog;private IDisposable? _collectionFont,_italicFont;
    private SubtitleEditor? _editor;private object? _draft,_selection;private string _source="",_history="",_path="",_destination="";private ResolvedFontFile[] _files=[];private string _folder="",_zip="";
    private object? _waveform,_times,_keys;private string? _audio,_video;private int _frame;
    private T In<T>(string name)where T:Control=>_dialog!.FindControl<T>(name)??throw new InvalidOperationException("Missing font collection control: "+name);
    public void Start(){window.LayoutUpdated+=(sender,args)=>_ready=window.IsVisible&&window.Bounds.Width>0;_timer.Tick+=Tick;_timer.Start();}
    private async void Tick(object? sender,EventArgs args)
    {
        if(!_ready||_ticking)return;_ticking=true;options.MarkStartupVerified();
        try
        {
            switch(_stage++)
            {
                case 0:
                    Require(window.TryGetPlatformHandle() is not null,"Font collection requires the real native application.");
                    var fonts=Path.Combine(options.VerificationMedia!,"fonts");_collectionFont=WindowsFontFileResolver.RegisterAcceptanceFont(Path.Combine(fonts,"日本語 collection.ttc"));_italicFont=WindowsFontFileResolver.RegisterAcceptanceFont(Path.Combine(fonts,"Italic fixture.ttf"));
                    _path=Path.Combine(options.ProfileDirectory!,"font-collector.ass");
                    string Style(string name,int bold,int italic)=>$"Style: {name},Yoake CI 日本語,24,&H00FFFFFF,&H0000FFFF,&H00000000,&H64000000,{bold},{italic},0,0,100,100,0,0,1,2,0,2,10,10,10,1\n";
                    File.WriteAllText(_path,"[Script Info]\nPlayResX: 640\nPlayResY: 480\n[V4+ Styles]\nFormat: "+string.Join(',',AssDocument.StyleFormat)+"\n"+Style("Default",0,0)+Style("Bold",-1,0)+Style("Italic",0,-1)+"[Events]\nFormat: "+string.Join(',',AssDocument.EventFormat)+"\nDialogue: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,,regular{\\rBold}bold{\\rItalic}italic{\\fn0}reset{\\fnYoake CI Missing}missing\n[Future]\nkeep: exactly\n");
                    Require(model.OpenSubtitle(_path),"Open controlled font document.");_editor=model.ActiveEditor;_source=_editor!.Document.Serialize();_history=_editor.Undo.NextUndoName??"";
                    Require(await model.OpenMediaAsync(Path.Combine(options.VerificationMedia!,"av.avi")),"Load the existing native media session before font collection.");
                    _waveform=model.WaveformSamples;_times=model.FrameTimes;_keys=model.Keyframes;_audio=model.AudioSourcePath;_video=model.VideoSourcePath;_frame=model.CurrentFrame;
                    model.Draft!.Text+="{\\r}日本語 မြန်မာ 👩‍👩‍👧 é";model.Draft.Start="unfinished";_draft=model.Draft;_selection=model.SelectedEvent;
                    window.FindControl<MenuItem>("ToolsMenu")!.IsSubMenuOpen=true;return;
                case 1:
                    Press(window.FindControl<MenuItem>("FontCollectorMenu")!,Key.Enter);return;
                case 2:
                    _dialog=window.OwnedWindows.OfType<FontCollectorWindow>().Single();Require(_dialog.TryGetPlatformHandle() is not null,"Fonts Collector is a real native window.");Require(In<ComboBox>("FontCollectorMode").SelectedIndex==0,"Default action is a non-destructive check.");
                    foreach(var name in new[]{"FontCollectorMode","FontCollectorFonts","FontCollectorLog","FontCollectorStart","FontCollectorClose"})Within(In<Control>(name),_dialog);
                    Press(In<Button>("FontCollectorStart"),Key.Enter);return;
                case 3:
                    if(model.FontCollectorBusy){_stage--;return;}
                    Require(model.FontReport is {} report&&report.Fonts.Length>=4&&In<ListBox>("FontCollectorFonts").ItemCount==report.Fonts.Length,"Native font scan populates the real report list.");
                    Require(model.FontReport!.Fonts.Count(f=>f.Match==FontMatch.Exact)>=3&&model.FontReport.Fonts.Any(f=>f.Usage.Request.Family=="Yoake CI Missing"&&f.Match!=FontMatch.Exact&&f.Files.Length==0),"Exact private-font resolution succeeds and fallback substitutions cannot be collected as missing requested fonts.");
                    Require(model.FontReport.Fonts.Any(f=>f.FaceIndex==1&&f.Files.Any(p=>p.Path.EndsWith(".ttc",StringComparison.OrdinalIgnoreCase))),"Bold face maps to the actual TTC index and physical file.");
                    Require(model.FontReport.Fonts.Any(f=>f.MissingGlyphs.Any(c=>c>0x7F)),"Japanese/Burmese/supplementary coverage comes from the actual face, not a fake family-name match.");
                    _files=model.FontReport.Fonts.Where(f=>f.Match==FontMatch.Exact).SelectMany(f=>f.Files).DistinctBy(f=>f.Path,StringComparer.OrdinalIgnoreCase).ToArray();Require(_files.Length==2,"Regular/Bold share one TTC while Italic maps to its separate TTF.");
                    Require(!string.IsNullOrWhiteSpace(In<TextBox>("FontCollectorLog").Text)&&model.FontOutput is null,"Check-only log is readable and writes no collection.");Unchanged();
                    In<ComboBox>("FontCollectorMode").SelectedIndex=1;_destination=Path.Combine(options.ProfileDirectory!,"日本語 collection outputs");In<TextBox>("FontCollectorDestination").Text=_destination;Press(In<Button>("FontCollectorStart"),Key.Enter);return;
                case 4:
                    Require(!model.FontCollectorBusy&&model.FontOutput is null&&In<TextBlock>("FontCollectorStatus").Text!.Contains("permission"),"Copying requires explicit rights acknowledgement and keeps validation visible.");
                    In<CheckBox>("FontCollectorRights").IsChecked=true;var destination=In<TextBox>("FontCollectorDestination");destination.Focus();var presenter=destination.GetVisualDescendants().OfType<TextPresenter>().Single();presenter.PreeditText="にほん";Press(destination,Key.Enter);presenter.PreeditText="";return;
                case 5:
                    Require(!model.FontCollectorBusy&&model.FontOutput is null,"IME Enter cannot start collection.");Press(In<Button>("FontCollectorStart"),Key.Enter);return;
                case 6:
                    if(model.FontCollectorBusy){_stage--;return;}
                    Require(model.FontOutput is {Files:2}&&Directory.Exists(model.FontOutput.Path),"Real button publishes a complete deduplicated font folder.");_folder=model.FontOutput!.Path;await VerifyFolder(_folder);Unchanged();
                    In<ComboBox>("FontCollectorMode").SelectedIndex=3;_zip=Path.Combine(_destination,"日本語 release.zip");In<TextBox>("FontCollectorDestination").Text=_zip;Press(In<Button>("FontCollectorStart"),Key.Enter);return;
                case 7:
                    if(model.FontCollectorBusy){_stage--;return;}
                    Require(model.FontOutput is {Files:2}&&File.Exists(model.FontOutput.Path),"Actual ZIP mode publishes a valid archive.");await VerifyZip(model.FontOutput!.Path);Unchanged();
                    In<ComboBox>("FontCollectorMode").SelectedIndex=2;Press(In<Button>("FontCollectorStart"),Key.Enter);return;
                case 8:
                    if(model.FontCollectorBusy){_stage--;return;}
                    Require(model.FontOutput is {Files:2}&&Path.GetDirectoryName(model.FontOutput.Path)==Path.GetDirectoryName(_path),"Script-folder mode publishes its predictable fonts batch beside the subtitle.");await VerifyFolder(model.FontOutput!.Path);Unchanged();
                    In<ComboBox>("FontCollectorMode").SelectedIndex=3;In<TextBox>("FontCollectorDestination").Text=Path.Combine(_destination,"canceled.zip");Press(In<Button>("FontCollectorStart"),Key.Enter);Press(In<Button>("FontCollectorCancel"),Key.Enter);return;
                case 9:
                    if(model.FontCollectorBusy){_stage--;return;}
                    Require(model.FontOutput is null&&!File.Exists(Path.Combine(_destination,"canceled.zip"))&&!Directory.EnumerateFileSystemEntries(_destination).Any(p=>Path.GetFileName(p).StartsWith(".yoake-fonts-",StringComparison.Ordinal)),"Real cancellation cannot publish a partial ZIP or leave staging data.");Unchanged();
                    Press(In<Button>("FontCollectorClose"),Key.Enter);return;
                case 10:
                    Require(!window.OwnedWindows.OfType<FontCollectorWindow>().Any(),"Actual Close releases the collector.");Unchanged();
                    await Invoke("settings/hotkeys/set",new HotkeyChange(HotkeyContext.Default,"tool/font_collector",["F9"]));Require(window.FindControl<MenuItem>("FontCollectorMenu")!.InputGesture?.Key==Key.F9,"Fonts menu uses the effective shortcut.");Press(window.FindControl<ListBox>("SubtitleRows")!,Key.F9);return;
                case 11:
                    _dialog=window.OwnedWindows.OfType<FontCollectorWindow>().Single();Require(_dialog.IsVisible,"Remapped keyboard opens the same collector.");Press(In<Button>("FontCollectorClose"),Key.Enter);return;
                case 12:
                    Require(StartupDiagnostics.BindingWarningCount==0&&StartupDiagnostics.FrameworkErrorCount==0,"Fonts Collector must not introduce framework/binding errors.");Unchanged();
                    _timer.Stop();_collectionFont?.Dispose();_italicFont?.Dispose();options.FinishVerification("PASS: real Tools Fonts Collector native menu and remapped grid key; actual process-private pinned OFL TTF/TTC Windows resolution, bold TTC face index and file mapping, inline/reset/fn0 variants, Unicode scalar glyph coverage and missing/fallback reporting; readable report/log/progress, non-destructive check default, rights and IME guards; exact whole-font bytes in atomic folder/script-folder/ZIP output; deduplication, Unicode paths and real cancellation cleanup; invalid draft, ASS source, active row and undo retained.\n");StartupDiagnostics.Complete();desktop.Shutdown(0);return;
            }
        }
        catch(Exception error){_timer.Stop();_collectionFont?.Dispose();_italicFont?.Dispose();Program.Fatal("Packaged Font Collector at stage "+(_stage-1),error);desktop.Shutdown(1);}
        finally{_ticking=false;}
    }
    private void Unchanged()=>Require(ReferenceEquals(_editor,model.ActiveEditor)&&ReferenceEquals(_draft,model.Draft)&&ReferenceEquals(_selection,model.SelectedEvent)&&_editor!.Document.Serialize()==_source&&(_editor.Undo.NextUndoName??"")==_history&&model.Draft!.Start=="unfinished"&&File.ReadAllText(_path)==_source&&ReferenceEquals(_waveform,model.WaveformSamples)&&ReferenceEquals(_times,model.FrameTimes)&&ReferenceEquals(_keys,model.Keyframes)&&_audio==model.AudioSourcePath&&_video==model.VideoSourcePath&&_frame==model.CurrentFrame,"Font collection preserves exact source, original file, invalid draft, selection, undo and independent native media/timeline state.");
    private async Task VerifyFolder(string path)
    {
        await Task.Run(()=>{var files=Directory.GetFiles(path).Where(p=>Path.GetFileName(p)!="FONT-LICENSE-NOTICE.txt").ToArray();Require(files.Length==_files.Length,"Folder contains the complete unique font set.");foreach(var source in _files)Require(files.Any(p=>Path.GetFileName(p).Equals(Path.GetFileName(source.Path),StringComparison.OrdinalIgnoreCase)&&Hash(p)==Hash(source.Path)),"Collected font bytes equal the real resolved file.");});
    }
    private async Task VerifyZip(string path)
    {
        await Task.Run(()=>{using var zip=ZipFile.OpenRead(path);Require(zip.Entries.Count==_files.Length+1&&zip.GetEntry("FONT-LICENSE-NOTICE.txt") is not null,"ZIP contains fonts plus a rights notice.");foreach(var source in _files){var entry=zip.Entries.Single(e=>e.FullName.Equals(Path.GetFileName(source.Path),StringComparison.OrdinalIgnoreCase));using var stream=entry.Open();Require(Convert.ToHexString(SHA256.HashData(stream))==Hash(source.Path),"ZIP retains exact font bytes.");}});
    }
    private static string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream));}
    private async Task Invoke(string id,object? parameter=null)=>Require(await model.Registry.InvokeAsync(id,new(model.ActiveDocumentId),parameter),"Font collection preparation failed: "+id);
    private static void Press(Control control,Key key)
    {
        if(control is ListBox list)control=list.ContainerFromIndex(Math.Max(0,list.SelectedIndex)) as Control??throw new InvalidOperationException("Realized row receives the font collector shortcut.");
        (TopLevel.GetTopLevel(control) as Window)?.Activate();Require(control.Focus()&&control.IsKeyboardFocusWithin,"Native control owns focus: "+control.Name);control.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Source=control,Key=key});
    }
    private static void Within(Control control,Window owner)
    {var p=control.TranslatePoint(default,owner);Require(p is {} point&&control.Bounds.Width>0&&control.Bounds.Height>0&&point.X>=-1&&point.Y>=-1&&point.X+control.Bounds.Width<=owner.ClientSize.Width+1&&point.Y+control.Bounds.Height<=owner.ClientSize.Height+1,"Font control fits its native dialog: "+control.Name);}
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
