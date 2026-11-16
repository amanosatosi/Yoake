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
using System.Text;

namespace Yoake.App;

internal sealed class AssistantsUiAcceptanceVerification(IClassicDesktopStyleApplicationLifetime desktop,
    MainWindow window,MainWindowViewModel model,StartupOptions options)
{
    private readonly DispatcherTimer _timer=new() { Interval=TimeSpan.FromMilliseconds(200) };
    private int _stage;private bool _ticking;
    private EditorAssistantWindow? _assistant;
    private MangetsuMetadataWindow? _metadata;
    private PostprocessWindow? _postprocess;
    private string _postprocessBefore="",_recombineBefore="";
    private KanjiTimingWindow? _kanji;
    private string _kanjiBefore="",_sortMetadataBefore="",_karaokeAbsoluteBefore="";
    private SubtitleTrackWindow? _track;
    private Task? _extract;
    private Guid _originalDocument;
    private string _embeddedBefore="";
    private SubtitleEncodingWindow? _encoding;
    private Task<int?>? _encodingResult;
    private byte[] _legacyBytes=[];
    private string _before="",_translated="";
    private T Get<T>(string name) where T:Control=>_assistant!.FindControl<T>(name)??throw new InvalidOperationException("Missing Assistant control: "+name);
    public void Start() { _timer.Tick+=Tick;window.Opened+=(_,_)=>_timer.Start(); }
    private async void Tick(object? sender,EventArgs args)
    {
        if(_ticking)return;_ticking=true;options.MarkStartupVerified();
        try
        {
            switch(_stage++)
            {
                case 0:
                    await Invoke(CommandIds.GridInsertAfter);model.Draft!.Text=@"{\future(opaque)\bord2}日本語{\i1}二番";await Invoke(CommandIds.EditCommit);
                    _before=model.ActiveEditor!.Document.Serialize();
                    window.FindControl<MenuItem>("ToolsMenu")!.IsSubMenuOpen=true;Press(window.FindControl<MenuItem>("TranslationAssistantMenu")!);return;
                case 1:
                    _assistant=window.OwnedWindows.OfType<EditorAssistantWindow>().Single();
                    Require(_assistant.TryGetPlatformHandle() is { Handle:not 0 },"Translation Assistant needs a real native window.");
                    Require(Get<TextBox>("AssistantOriginal").Text=="日本語","Translation starts on the first text block.");
                    Get<TextBox>("AssistantTranslation").Text="မြန်မာ\nစာ";return;
                case 2:
                    var input=Get<TextBox>("AssistantTranslation");input.Focus();var presenter=input.GetVisualDescendants().OfType<TextPresenter>().Single();
                    presenter.PreeditText="မ";Key(input,Avalonia.Input.Key.Enter);
                    Require(model.ActiveEditor!.Document.Serialize()==_before,"IME Enter must not publish translation.");presenter.PreeditText=null;
                    Key(input,Avalonia.Input.Key.Enter);return;
                case 3:
                    Require(model.SelectedEvent!.Text==@"{\future(opaque)\bord2}မြန်မာ\Nစာ{\i1}二番","Translate Japanese to Burmese without rewriting override source.");
                    Require(Get<TextBox>("AssistantOriginal").Text=="二番","Enter must advance to the next block.");
                    Get<TextBox>("AssistantTranslation").Text="ဒုတိယ";return;
                case 4:
                    Press(Get<Button>("AssistantPreview"));return;
                case 5:
                    _translated=model.ActiveEditor!.Document.Serialize();Require(_translated.Contains("ဒုတိယ",StringComparison.Ordinal),"Commit and stay must publish the current block.");
                    _assistant!.Close();await Invoke(CommandIds.EditUndo);await Invoke(CommandIds.EditUndo);
                    Require(model.ActiveEditor.Document.Serialize()==_before,"Both translations undo independently to exact source.");
                    await Invoke(CommandIds.EditRedo);await Invoke(CommandIds.EditRedo);
                    Require(model.ActiveEditor.Document.Serialize()==_translated,"Redo preserves the translated source.");
                    var style=model.ActiveEditor.Document.Styles[0].Clone();style.Name="Burmese";model.ActiveEditor.AddStyle(style);
                    await Invoke("tool/style/assistant");return;
                case 6:
                    _assistant=window.OwnedWindows.OfType<EditorAssistantWindow>().Single();
                    Get<AutoCompleteBox>("AssistantStyle").Text="Burmese";return;
                case 7:
                    Press(Get<Button>("AssistantPreview"));return;
                case 8:
                    Require(model.SelectedEvent!.Style=="Burmese","Styling Assistant must apply the selected existing style.");
                    _assistant!.Close();var path=Path.Combine(options.ProfileDirectory!,"assistant.ass");Require(model.SaveActiveSubtitle(path),"Save assistant output.");
                    var exact=model.ActiveEditor!.Document.Serialize();await Invoke("subtitle/close");Require(model.OpenSubtitle(path),"Reopen assistant output from disk after closing the document.");
                    Require(model.ActiveEditor!.Document.Serialize()==exact,"Translated ASS and styles must survive reopen.");
                    await Invoke("mangetsu/metadata");return;
                case 9:
                    _metadata=window.OwnedWindows.OfType<MangetsuMetadataWindow>().Single();
                    _metadata.FindControl<TextBox>("MangetsuMetadataText")!.Text=@"{\vert1\boxp(10,20,5,15)\future(opaque)}";return;
                case 10:
                    Press(_metadata!.FindControl<Button>("MangetsuMetadataApply")!);return;
                case 11:
                    Require(MangetsuMetadata.Kind(model.Events[0])==MangetsuMetadataKind.Style&&model.Events[0].Text.Contains(@"\future(opaque)",StringComparison.Ordinal),"Metadata editor must preserve future tags in the initial style comment.");
                    _metadata!.FindControl<ComboBox>("MangetsuMetadataKind")!.SelectedItem=MangetsuMetadataKind.Actor;
                    _metadata.FindControl<AutoCompleteBox>("MangetsuMetadataTarget")!.Text="Nene";
                    _metadata.FindControl<TextBox>("MangetsuMetadataText")!.Text=@"{\1c&HFF0000&}";return;
                case 12:
                    Press(_metadata!.FindControl<Button>("MangetsuMetadataApply")!);return;
                case 13:
                    Require(MangetsuMetadata.Kind(model.Events[0])==MangetsuMetadataKind.Style&&MangetsuMetadata.Kind(model.Events[1])==MangetsuMetadataKind.Actor,"Style extensions must precede Actor Colorcoding and dialogue.");
                    _metadata!.Close();var metadataSource=model.ActiveEditor!.Document.Serialize();
                    await Invoke(CommandIds.EditUndo);Require(MangetsuMetadata.Initial(model.ActiveEditor.Document).Length==1,"Actor metadata must undo independently.");
                    await Invoke(CommandIds.EditRedo);Require(model.ActiveEditor.Document.Serialize()==metadataSource,"Metadata redo restores exact source.");
                    var metadataPath=Path.Combine(options.ProfileDirectory!,"metadata.ass");Require(model.SaveActiveSubtitle(metadataPath),"Save metadata.");await Invoke("subtitle/close");Require(model.OpenSubtitle(metadataPath),"Reopen metadata from disk.");
                    Require(model.ActiveEditor!.Document.Serialize()==metadataSource,"Metadata order and payload survive file round-trip.");
                    _encoding=new SubtitleEncodingWindow();_encodingResult=_encoding.ShowDialog<int?>(window);return;
                case 14:
                    var choice=_encoding!.FindControl<AutoCompleteBox>("SubtitleEncoding")!;
                    choice.SelectedItem=choice.ItemsSource!.Cast<SubtitleEncodingChoice>().Single(c=>c.CodePage==932);
                    Press(_encoding.FindControl<Button>("OpenWithEncoding")!);return;
                case 15:
                    Require(_encodingResult!.IsCompleted&&_encodingResult.Result==932,"The native encoding chooser must return Shift-JIS.");
                    var legacySource=AssDocument.CreateEmpty().Serialize()+"Dialogue: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,,日本語\r\n";
                    _legacyBytes=SubtitleEncodings.Get(932).GetBytes(legacySource);
                    var legacyPath=Path.Combine(options.ProfileDirectory!,"legacy.ass");File.WriteAllBytes(legacyPath,_legacyBytes);
                    Require(await model.Registry.InvokeAsync("subtitle/open/charset",new(),new OpenSubtitleEncodingRequest(legacyPath,932)),"Open legacy charset command.");return;
                case 16:
                    Require(model.SelectedEvent!.Text=="日本語"&&model.ActiveEditor!.Document.EncodingCodePage==932,"Open the Japanese source without replacement characters.");
                    window.FindControl<TextBox>("SubtitleText")!.Text="မြန်မာစာ";return;
                case 17:
                    await Invoke(CommandIds.EditCommit);
                    var utf8Path=Path.Combine(options.ProfileDirectory!,"burmese.ass");
                    Require(await model.Registry.InvokeAsync("subtitle/save/utf8",new(),utf8Path),"Save UTF-8 command.");
                    Require(AssDocument.Load(utf8Path).Events.Single().Text=="မြန်မာစာ","Burmese translation must survive UTF-8 save.");
                    Require(File.ReadAllBytes(Path.Combine(options.ProfileDirectory!,"legacy.ass")).AsSpan().SequenceEqual(_legacyBytes),"Encoding conversion must preserve the original Japanese source file.");
                    var timingPath=Path.Combine(options.ProfileDirectory!,"postprocess-input.ass");
                    File.WriteAllText(timingPath,AssDocument.CreateEmpty().Serialize()+"Dialogue: 0,0:00:01.00,0:00:02.00,Default,,0,0,0,,{\\future(opaque)}日本語 မြန်မာ\r\n");
                    Require(model.OpenSubtitle(timingPath),"Open timing fixture.");_postprocessBefore=model.ActiveEditor!.Document.Serialize();await Invoke("tool/time/postprocess");return;
                case 18:
                    _postprocess=window.OwnedWindows.OfType<PostprocessWindow>().Single();
                    _postprocess.FindControl<CheckBox>("PostprocessAdjacent")!.IsChecked=false;
                    _postprocess.FindControl<CheckBox>("PostprocessLeadInEnabled")!.IsChecked=true;
                    _postprocess.FindControl<CheckBox>("PostprocessLeadOutEnabled")!.IsChecked=true;
                    _postprocess.FindControl<NumericUpDown>("PostprocessLeadIn")!.Value=90;
                    _postprocess.FindControl<NumericUpDown>("PostprocessLeadOut")!.Value=110;
                    Press(_postprocess.FindControl<Button>("PostprocessPreview")!);return;
                case 19:
                    if(model.HasEditOperation){_stage--;return;}
                    Require(model.ActiveEditor!.Document.Serialize()==_postprocessBefore,"Timing Preview must preserve the original source.");
                    Press(_postprocess!.FindControl<Button>("PostprocessApply")!);return;
                case 20:
                    if(model.HasEditOperation){_stage--;return;}
                    Require(model.SelectedEvent!.StartMilliseconds==910&&model.SelectedEvent.EndMilliseconds==2110,"Native postprocessor must apply configured leads.");
                    Require(model.SelectedEvent.Text==@"{\future(opaque)}日本語 မြန်မာ","Postprocessing must preserve exact text and override tags.");
                    await Invoke(CommandIds.EditUndo);Require(model.ActiveEditor!.Document.Serialize()==_postprocessBefore,"Timing postprocessing must undo in one step.");
                    await Invoke(CommandIds.EditRedo);
                    var recombinePath=Path.Combine(options.ProfileDirectory!,"recombine.ass");
                    File.WriteAllText(recombinePath,AssDocument.CreateEmpty().Serialize()+
                        "Dialogue: 0,0:00:01.00,0:00:02.00,Default,,0,0,0,,日本語\n"+
                        "Dialogue: 0,0:00:02.00,0:00:03.00,Default,,0,0,0,,日本語\\Nမြန်မာ\n"+
                        "Dialogue: 0,0:00:03.00,0:00:04.00,Default,,0,0,0,,မြန်မာ\n");
                    Require(model.OpenSubtitle(recombinePath),"Open recombination fixture.");_recombineBefore=model.ActiveEditor!.Document.Serialize();return;
                case 21:
                    var grid=window.FindControl<ListBox>("SubtitleRows")!;var row=grid.ContainerFromIndex(0) as Control;
                    Require(row is { Bounds.Height:>0 }&&row.Focus(),"A realized subtitle grid row must own focus.");
                    row!.RaiseEvent(new KeyEventArgs { RoutedEvent=InputElement.KeyDownEvent,Key=Avalonia.Input.Key.A,KeyModifiers=Avalonia.Input.KeyModifiers.Control,Source=row });return;
                case 22:
                    Require(model.SelectedEvents.Count==3&&window.FindControl<ListBox>("SubtitleRows")!.SelectedItems!.Count==3,$"Select all actual grid rows through Ctrl-A (model {model.SelectedEvents.Count}, grid {window.FindControl<ListBox>("SubtitleRows")!.SelectedItems!.Count}).");
                    window.FindControl<MenuItem>("SubtitleMenu")!.IsSubMenuOpen=true;Press(window.FindControl<MenuItem>("RecombineMenu")!);return;
                case 23:
                    Require(model.Events.Count==2&&model.Events[0].Text=="日本語"&&model.Events[1].Text=="မြန်မာ","The native Recombine menu must remove the combined duplicate.");
                    await Invoke(CommandIds.EditUndo);Require(model.ActiveEditor!.Document.Serialize()==_recombineBefore,"Recombine must restore exact source in one undo step.");
                    _originalDocument=model.ActiveDocumentId!.Value;_embeddedBefore=model.ActiveEditor.Document.Serialize();
                    model.Draft!.Start="unfinished";
                    var embedded=Path.Combine(options.VerificationMedia??throw new InvalidOperationException("Embedded subtitle fixture directory is required."),"subtitles.mkv");
                    _extract=model.Registry.InvokeAsync("subtitle/open/video",new(model.ActiveDocumentId),new OpenEmbeddedSubtitlesRequest(embedded,LoadMedia:OperatingSystem.IsWindows())).AsTask();return;
                case 24:
                    _track=window.OwnedWindows.OfType<SubtitleTrackWindow>().SingleOrDefault();
                    if(_track is null){Require(_extract is { IsCompleted:false },"Extraction must display a track chooser.");_stage--;return;}
                    Require(_track.TryGetPlatformHandle() is { Handle:not 0 },"Track chooser must own a native window.");
                    var tracks=_track.FindControl<ListBox>("SubtitleTracks")!;
                    tracks.SelectedItem=tracks.Items.Cast<Yoake.Core.Media.MatroskaSubtitleTrack>().Single(t=>t.Codec=="S_TEXT/ASS");return;
                case 25:
                    Press(_track!.FindControl<Button>("ExtractSubtitleTrack")!);return;
                case 26:
                    if(_extract is { IsCompleted:false }){_stage--;return;}
                    Require(_extract is { IsCompletedSuccessfully:true }&&model.ActiveDocumentId!=_originalDocument&&model.Events.Count==2,"Selected embedded track must open independently.");
                    Require(model.Events[0].Text==@"{\k10\future(opaque)}日本語, မြန်မာ é","Embedded ASS source must preserve Unicode, commas and opaque tags.");
                    if(OperatingSystem.IsWindows())Require(model.VideoSourcePath==Path.Combine(options.VerificationMedia!,"subtitles.mkv")&&model.FrameTimes.Count==3&&model.VideoFrame is not null,"Packaged Windows extraction must open the independent episode video provider and decoded preview.");
                    Require(!model.SaveActiveSubtitle(Path.Combine(options.VerificationMedia!,"subtitles.mkv"))&&model.SubtitleStatus.Contains("ASS destination",StringComparison.Ordinal),"Save As must protect the original subtitle container.");
                    var extractedPath=Path.Combine(options.ProfileDirectory!,"embedded.ass");Require(model.SaveActiveSubtitle(extractedPath),"Extracted track must save to an ASS destination.");
                    await Invoke("subtitle/close");
                    Require(model.ActiveDocumentId==_originalDocument&&model.Draft!.Start=="unfinished"&&model.ActiveEditor!.Document.Serialize()==_embeddedBefore,"Extraction must retain the original invalid draft and source.");
                    model.Draft!.Start=model.SelectedEvent!.Start;
                    Require(model.OpenSubtitle(extractedPath)&&model.Events.Count==2,"Saved extracted subtitles must reopen.");
                    var kanjiDocument=AssDocument.CreateEmpty();var kanjiEditor=new SubtitleEditor(kanjiDocument);
                    foreach(var name in new[] { "Romaji","Kanji" }){var named=kanjiDocument.Styles[0].Clone();named.Name=name;kanjiEditor.AddStyle(named);}
                    var reading=kanjiEditor.Insert(null,true);reading.Style="Romaji";reading.End="0:00:00.60";reading.Text=@"{\k10}Bo{\k20}ku{\k30}wa";
                    var writing=kanjiEditor.Insert(null,true);writing.Style="Kanji";writing.Text=@"{\future(opaque)\bord2}僕は";
                    var kanjiPath=Path.Combine(options.ProfileDirectory!,"kanji.ass");kanjiDocument.Save(kanjiPath);Require(model.OpenSubtitle(kanjiPath),"Open matching fixture.");_kanjiBefore=model.ActiveEditor!.Document.Serialize();
                    window.FindControl<MenuItem>("ToolsMenu")!.IsSubMenuOpen=true;Press(window.FindControl<MenuItem>("KanjiTimingMenu")!);return;
                case 27:
                    _kanji=window.OwnedWindows.OfType<KanjiTimingWindow>().Single();Require(_kanji.TryGetPlatformHandle() is { Handle:not 0 },"Kanji copier needs a native window.");
                    _kanji!.FindControl<ComboBox>("KanjiSourceStyle")!.SelectedItem="Romaji";_kanji!.FindControl<ComboBox>("KanjiDestinationStyle")!.SelectedItem="Kanji";
                    Press(_kanji!.FindControl<Button>("KanjiStart")!);return;
                case 28:
                    Require(_kanji!.FindControl<ListBox>("KanjiSourceSyllables")!.Items.Count==3&&_kanji!.FindControl<ListBox>("KanjiDestinationCharacters")!.Items.Count==2,"Copier displays source syllables and destination graphemes.");
                    var countInput=_kanji!.FindControl<NumericUpDown>("KanjiSourceCount")!;var countPresenter=countInput.GetVisualDescendants().OfType<TextPresenter>().First();
                    countPresenter.PreeditText="か";Key(countInput,Avalonia.Input.Key.Escape);Require(_kanji!.IsVisible,"IME Escape must not close matching.");countPresenter.PreeditText=null;
                    Require(countInput.Value==2&&_kanji!.FindControl<NumericUpDown>("KanjiDestinationCount")!.Value==1,"Aegisub kana lookahead should suggest Bo+ku for 僕 before は/wa.");
                    Press(_kanji!.FindControl<Button>("KanjiSuggest")!);
                    countInput.Value=2;_kanji!.FindControl<NumericUpDown>("KanjiDestinationCount")!.Value=1;Press(_kanji!.FindControl<Button>("KanjiLink")!);return;
                case 29:
                    Require(_kanji!.FindControl<NumericUpDown>("KanjiSourceCount")!.Value==1&&_kanji!.FindControl<NumericUpDown>("KanjiDestinationCount")!.Value==1,"Next-group interpolation should suggest wa for は.");
                    var syllables=_kanji!.FindControl<ListBox>("KanjiSourceSyllables")!;var syllable=syllables.ContainerFromIndex(0) as Control;Require(syllable is { Bounds.Height:>0 },"Source syllable must be realized for keyboard matching.");Press(syllable!);return;
                case 30:
                    var matchList=_kanji!.FindControl<ListBox>("KanjiSourceSyllables")!;Require(matchList.IsKeyboardFocusWithin,"Keyboard focus must remain in matching after the final source item disappears.");Key(matchList,Avalonia.Input.Key.Enter);return;
                case 31:
                    Require(model.ActiveEditor!.Document.Serialize()==_kanjiBefore,"Accepted matches must remain unapplied until Apply.");Press(_kanji!.FindControl<Button>("KanjiApply")!);return;
                case 32:
                    Require(model.Events[0].Text==@"{\k10}Bo{\k20}ku{\k30}wa"&&model.Events[1].Text==@"{\k30}{\future(opaque)\bord2}僕{\k30}は","Apply matches timing without changing source syllables or destination overrides.");
                    _kanji!.Close();var matched=model.ActiveEditor!.Document.Serialize();await Invoke(CommandIds.EditUndo);Require(model.ActiveEditor.Document.Serialize()==_kanjiBefore,"All accepted timing copies undo in one step.");await Invoke(CommandIds.EditRedo);Require(model.ActiveEditor.Document.Serialize()==matched,"Karaoke matching redo restores exact source.");
                    var matchedPath=Path.Combine(options.ProfileDirectory!,"matched.ass");Require(model.SaveActiveSubtitle(matchedPath),"Save matched karaoke.");await Invoke("subtitle/close");Require(model.OpenSubtitle(matchedPath)&&model.ActiveEditor.Document.Serialize()==matched,"Matched karaoke round-trips on disk.");
                    var sortPath=Path.Combine(options.ProfileDirectory!,"sort-metadata.ass");
                    File.WriteAllText(sortPath,AssDocument.CreateEmpty().Serialize()+
                        "Comment: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,mangetsu-style,{\\bord2}\r\n"+
                        "Comment: 0,0:00:00.00,0:00:01.00,Default,Nene,0,0,0,mangetsu-colorcoding,{\\1c&H112233&}\r\n"+
                        "Comment: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,{uketsugu},日本語 မြန်မာ\r\n"+
                        "Dialogue: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,,Z {uketsugu}\r\n"+
                        "Comment: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,mangetsu-style,{\\future(inactive)}\r\n"+
                        "Dialogue: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,,A\r\n");
                    Require(model.OpenSubtitle(sortPath),"Open renderer-metadata sorting fixture.");_sortMetadataBefore=model.ActiveEditor!.Document.Serialize();return;
                case 33:
                    window.FindControl<MenuItem>("SubtitleMenu")!.IsSubMenuOpen=true;return;
                case 34:
                    window.FindControl<MenuItem>("SortAllLinesMenu")!.IsSubMenuOpen=true;return;
                case 35:
                    Press(window.FindControl<MenuItem>("SortAllTextMenu")!);return;
                case 36:
                    if(model.HasEditOperation){_stage--;return;}
                    Require(MangetsuMetadata.Kind(model.Events[0])==MangetsuMetadataKind.Style&&MangetsuMetadata.Kind(model.Events[1])==MangetsuMetadataKind.Actor&&MangetsuMetadata.IsInherited(model.Events[2]),"Sorting must preserve active metadata and imported inheritance order.");
                    Require(model.Events[3].Text=="A"&&model.Events[4].Text==@"{\future(inactive)}"&&model.Events[5].Text=="Z {uketsugu}"&&MangetsuMetadata.Initial(model.ActiveEditor!.Document).Length==2,"Sort dialogue around protected renderer rows without activating late metadata.");
                    var sortedMetadata=model.ActiveEditor!.Document.Serialize();await Invoke(CommandIds.EditUndo);Require(model.ActiveEditor.Document.Serialize()==_sortMetadataBefore,"Metadata-aware sort must undo exactly.");await Invoke(CommandIds.EditRedo);Require(model.ActiveEditor.Document.Serialize()==sortedMetadata,"Metadata-aware sort redo must be exact.");
                    var sortedPath=Path.Combine(options.ProfileDirectory!,"sorted-metadata.ass");Require(model.SaveActiveSubtitle(sortedPath),"Save sorted metadata.");await Invoke("subtitle/close");Require(model.OpenSubtitle(sortedPath)&&model.ActiveEditor.Document.Serialize()==sortedMetadata,"Sorting metadata must round-trip through save/reopen.");
                    var absolutePath=Path.Combine(options.ProfileDirectory!,"absolute-karaoke.ass");
                    File.WriteAllText(absolutePath,AssDocument.CreateEmpty().Serialize()+
                        "Comment: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,mangetsu-style,{\\bord2}\r\n"+
                        "Comment: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,{uketsugu},base\r\n"+
                        "Dialogue: 0,0:00:01.00,0:00:04.00,Default,,0,0,0,,{uketsugu}{\\an8\\kt20\\k30}日本{\\kt100\\kf20}မြန်မာ{\\kt50\\ko10}é\r\n");
                    Require(model.OpenSubtitle(absolutePath),"Open absolute karaoke fixture.");_karaokeAbsoluteBefore=model.ActiveEditor!.Document.Serialize();return;
                case 37:
                    var karaokeGrid=window.FindControl<ListBox>("SubtitleRows")!;var karaokeRow=karaokeGrid.ContainerFromIndex(0) as Control;Require(karaokeRow is { Bounds.Height:>0 }&&karaokeRow.Focus(),"Focus an actual grid row for selection-wide karaoke splitting.");
                    karaokeRow!.RaiseEvent(new KeyEventArgs { RoutedEvent=InputElement.KeyDownEvent,Key=Avalonia.Input.Key.A,KeyModifiers=KeyModifiers.Control,Source=karaokeRow });return;
                case 38:
                    Require(model.SelectedEvents.Count==3,"Select metadata and dialogue through the real grid keyboard router.");window.FindControl<MenuItem>("SubtitleMenu")!.IsSubMenuOpen=true;Press(window.FindControl<MenuItem>("SplitByKaraokeMenu")!);return;
                case 39:
                    Require(model.Events.Count==5&&MangetsuMetadata.Kind(model.Events[0])==MangetsuMetadataKind.Style&&MangetsuMetadata.IsInherited(model.Events[1]),"Karaoke splitting must retain renderer records and produce three dialogue syllables.");
                    var absoluteLines=model.Events.Skip(2).ToArray();Require(absoluteLines.Select(e=>e.Start).SequenceEqual(new[] { "0:00:01.20","0:00:02.00","0:00:01.50" })&&absoluteLines.Select(e=>e.End).SequenceEqual(new[] { "0:00:01.50","0:00:02.20","0:00:04.00" }),"Absolute karaoke starts must retain gaps and backwards resets.");
                    Require(absoluteLines.All(e=>!e.Text.Contains(@"\kt",StringComparison.Ordinal))&&absoluteLines[1].Text.Contains("မြန်မာ",StringComparison.Ordinal)&&absoluteLines[2].Text.Contains("é",StringComparison.Ordinal),"Split timing is relative while Unicode and karaoke tag modes survive.");
                    var absoluteAfter=model.ActiveEditor!.Document.Serialize();await Invoke(CommandIds.EditUndo);Require(model.ActiveEditor.Document.Serialize()==_karaokeAbsoluteBefore,"Absolute splitting undoes in one step.");await Invoke(CommandIds.EditRedo);Require(model.ActiveEditor.Document.Serialize()==absoluteAfter,"Absolute split redo restores exact records.");
                    var absoluteSaved=Path.Combine(options.ProfileDirectory!,"absolute-split.ass");Require(model.SaveActiveSubtitle(absoluteSaved),"Save split karaoke.");await Invoke("subtitle/close");Require(model.OpenSubtitle(absoluteSaved)&&model.ActiveEditor.Document.Serialize()==absoluteAfter,"Absolute karaoke splitting must round-trip through save/reopen.");
                    _timer.Stop();options.FinishVerification("PASS: native Translation/Styling, Japanese/Burmese IME preedit, Mangetsu metadata, Shift-JIS/UTF-8, Save/close/reopen, Timing Postprocessor, Recombine, embedded Matroska track selection/extraction independent Save/reopen, and Kanji Timing Copier interpolation/grouping/keyboard/Apply, metadata-safe sorting and absolute karaoke splitting with undo/redo.\n");StartupDiagnostics.Complete();desktop.Shutdown(0);return;
            }
        }
        catch(Exception error) { _timer.Stop();Program.Fatal("Assistants acceptance stage "+_stage,error);desktop.Shutdown(1); }
        finally { _ticking=false; }
    }
    private async Task Invoke(string id) { Require(await model.Registry.InvokeAsync(id,new(model.ActiveDocumentId)),"Assistant command failed: "+id+" · "+model.SubtitleStatus); }
    private static void Press(Control control) { Require(control.Focus(),"Assistant action must be focusable.");Key(control,Avalonia.Input.Key.Enter); }
    private static void Key(Control control,Key key)=>control.RaiseEvent(new KeyEventArgs { RoutedEvent=InputElement.KeyDownEvent,Key=key,Source=control });
    private static void Require(bool condition,string message) { if(!condition)throw new InvalidOperationException(message); }
}
