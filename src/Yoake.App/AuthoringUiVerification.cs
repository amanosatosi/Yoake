using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Yoake.Core.Commands;
using Yoake.Core.Subtitles;
using Yoake.UI;
using Yoake.UI.Controls;
using Yoake.UI.ViewModels;

namespace Yoake.App;

// Runs against the shipping controls after they are opened. DIP geometry is
// checked at two window widths; 96/120/144/192-DPI captures aid visual review.
// Raster captures do not simulate Windows monitor-DPI transitions or real IME.
internal sealed class AuthoringUiVerification(MainWindow window, MainWindowViewModel model, StylesWindow styles, string report, string? mediaFixtures)
{
    private int _stage;
    private long _previewRevision;
    private double _normalEditorWidth;
    private AssStyle? _first, _second;
    private AssColorDialog? _colors;
    private Task<bool>? _mediaLoading;
    private long _draftRevision;
    private byte[]? _beforeDraft;
    private string? _committedText;
    private Guid _originalTab;
    private const string Sample = @"{\fad(200,200)\bord3\1c&HFFFFFF&\3c&H000000&}<仮|かり>の糸\N{\k20}こ{\k15}れ{\k30}は{\1grd(0,&HFF0000&,&H0000FF&)}テスト
{\bord2\t(0,500,\bord6\1c&H00FFFF&)}Text မြန်မာ é 👩‍👩‍👧‍👦
{\fnArial\future(opaque)\p1}m 0 0 l 20 0 20 20{\p0}";

    public bool Tick()
    {
        var text=window.FindControl<AssTextBox>("SubtitleText")!;
        switch(_stage++)
        {
            case 0:
                foreach(var gridSample in new[]{"日本語", "မြန်မာ", "Latin é", "العربية", "👩‍👩‍👧‍👦"})
                {var line=model.ActiveEditor!.Insert(null,false);model.ActiveEditor.SetField(line,"Text",gridSample,"Mixed-script UI fixture");}
                model.ActiveEditor!.ToggleComment([model.Events.Last()]);
                window.RequestedThemeVariant=styles.RequestedThemeVariant=ThemeVariant.Dark;
                text.SetCurrentValue(TextBox.TextProperty,Sample);
                Invoke(model.Registry,CommandIds.EditCommit);
                text.CaretIndex=text.SelectionStart=text.SelectionEnd=Sample.Length;
                return false;
            case 1:
                CheckGrid();CheckNavigation(text);CheckStandardStyleVisibility();CheckChrome();
                foreach(var scale in new[]{1d,1.25,1.5,2})Capture(window,$"editor-dark-{scale*100:0}",scale);
                Capture(styles,"styles-dark-normal",1);
                CheckMainFields();CheckSyntax(text);
                var font=Named<FontPicker>(styles,"StyleFont");Require(font.InstalledFamilies.Count>0,"Installed font browser must load actual families.");font.OpenBrowser();Require(font.IsBrowserOpen,"Font dropdown must open immediately without requiring a typed search.");
                _normalEditorWidth=window.FindControl<Grid>("EventEditorRegion")!.Bounds.Width;
                window.RequestedThemeVariant=styles.RequestedThemeVariant=ThemeVariant.Light;
                return false;
            case 2:
                Named<FontPicker>(styles,"StyleFont").CloseBrowser();
                Capture(window,"editor-light-normal",1);Capture(styles,"styles-light-normal",1);CheckSyntax(text);
                window.Width=1040;window.Height=760;styles.Width=940;styles.Height=650;
                // Hosted Windows may constrain both requested window sizes to
                // its work area. Also narrow the real splitter pane so reflow
                // is exercised even on that desktop, without a fake window.
                var workspace=window.FindControl<Grid>("UpperWorkspace")!;
                workspace.ColumnDefinitions[0].Width=new GridLength(1,GridUnitType.Star);
                workspace.ColumnDefinitions[2].Width=new GridLength(480);
                return false;
            case 3:
                Capture(window,"editor-light-narrow",1);Capture(styles,"styles-light-narrow",1);CheckMainFields();CheckStyleFields();
                Require(window.FindControl<Grid>("EventEditorRegion")!.Bounds.Width<_normalEditorWidth-50,"Responsive verification must exercise a genuinely narrower editor pane.");
                window.RequestedThemeVariant=styles.RequestedThemeVariant=ThemeVariant.Dark;
                return false;
            case 4:
                CheckMainFields();Capture(window,"editor-dark-narrow",1);
                PrepareStyleSwitching();
                return false; // Newly selected library controls need a real layout pass.
            case 5:
                Named<FontPicker>(styles,"StyleFont").GetVisualDescendants().OfType<AutoCompleteBox>().Single().SetCurrentValue(AutoCompleteBox.TextProperty,"Missing 日本 字体");
                FocusSelectedStyle("ScriptStyles");
                return false;
            case 6:
                Require(Named<FontPicker>(styles,"StyleFont").FontName==_first!.Get("Fontname"),"Returning to the already selected script row must activate its inline draft.");
                FocusSelectedStyle("LibraryStyles");
                return false;
            case 7:
                Require(Named<FontPicker>(styles,"StyleFont").FontName=="Missing 日本 字体","Returning to the already selected library row must retain its committed draft.");
                FinishStyleSwitching();
                var sample=Named<TextBox>(styles,"PreviewSample");
                sample.SetCurrentValue(TextBox.TextProperty,"obsolete preview");
                styles.Preview.Clear();
                Require(!styles.Preview.HasFrame&&!styles.Preview.HasCurrentFrame,"Clearing a style selection must remove its old preview.");
                sample.SetCurrentValue(TextBox.TextProperty,@"Yoake 0123\N日本語 テスト\Nမြန်မာ");
                _previewRevision=styles.Preview.RequestedRevision;
                return false;
            case 8:
                _colors=new AssColorDialog(new AssColor(255,128,192,128),Path.Combine(Path.GetDirectoryName(report)!,"recent-colors.txt"));_colors.Show(window);return false;
            case 9:
                Require(_colors is not null,"Canonical color dialog must open.");
                Require(Named<ColorSpectrum>(_colors!,"ColorSpectrum").Bounds.Width>=256&&Named<ColorSpectrum>(_colors!,"ColorSpectrum").Bounds.Height>=256,"Canonical picker must realize a full 2D spectrum.");
                Require(Named<StackPanel>(_colors!,"RecentColors").Bounds.Height>0,"Recent colors must have a separate visible area.");Capture(_colors!,"color-picker-dark",1);_colors!.RequestedThemeVariant=ThemeVariant.Light;return false;
            case 10:
                Capture(_colors!,"color-picker-light",1);_colors!.Close();return false;
            case 11:
                Require(mediaFixtures is not null,"Packaged authoring verification requires deterministic media fixtures.");
                _mediaLoading=model.OpenMediaAsync(Path.Combine(mediaFixtures!,"video.avi"));return false;
            case 12:
                if(!_mediaLoading!.IsCompleted){_stage--;return false;}
                Require(_mediaLoading.Result&&model.VideoFrame is not null,"Real video must load into MainWindow.");
                Require(model.FrameTimes.Count==10&&model.Keyframes.Count>0,"FFMS2 must supply actual frame/keyframe metadata.");
                var slider=window.FindControl<VideoFrameSlider>("VideoSeekBar")!;
                Capture(slider,"video-keyframe-ruler",2);
                Require(slider.RenderedKeyframeMarks>0,"Keyframe metadata must reach actual ruler drawing.");
                var snap=slider.FrameAt(slider.Bounds.Width*0.55,true);
                Require(model.Keyframes.Contains(snap),"Shift-click coordinates must snap to an actual FFMS2 keyframe.");
                _committedText=model.SelectedEvent!.Text;_beforeDraft=FramePixels();
                foreach(var value in new[]{"L","Live","{\\an5}Live draft 日本語"})text.SetCurrentValue(TextBox.TextProperty,value);
                _draftRevision=model.PreviewRevision;
                Require(model.SelectedEvent.Text==_committedText&&model.Draft!.IsChanged,"Typing must leave permanent text unchanged before idle commit.");return false;
            case 13:
                if(model.DisplayedPreviewRevision<_draftRevision){_stage--;return false;}
                Require(model.DisplayedPreviewRevision==model.PreviewRevision,"Displayed preview must reflect the latest draft revision.");
                Require(model.Draft!.IsChanged&&model.SelectedEvent!.Text==_committedText,"Mangetsu must show the draft before idle commit.");
                Require(!_beforeDraft!.AsSpan().SequenceEqual(FramePixels()),"Live draft must change actual composited video pixels.");
                Capture(window,"editor-live-draft",1);Invoke(model.Registry,CommandIds.EditCancel);
                Require(model.SelectedEvent!.Text==_committedText&&!model.Draft!.IsChanged,"Escape must revert the entire pending edit burst.");
                _mediaLoading=model.OpenMediaAsync(Path.Combine(mediaFixtures!,"audio.wav"));return false;
            case 14:
                if(!_mediaLoading!.IsCompleted){_stage--;return false;}
                Require(_mediaLoading.Result&&model.WaveformSamples is {Count:>100},"Real audio must generate the signed waveform.");
                Require(model.VideoFrame is null&&model.FrameTimes.Count==0,"Replacing video with audio must clear the old video presentation.");
                Require(model.WaveformSamples!.Envelopes.Min(p=>p.Minimum)<-0.1&&model.WaveformSamples.Envelopes.Max(p=>p.Maximum)>0.1,"Audio fixture must produce both signed extrema.");
                var audio=window.FindControl<AudioWaveformControl>("AudioDisplay")!;audio.VisibleSeconds=2;audio.ViewportStart=0;
                return false;
            case 15:
                Capture(window,"editor-signed-waveform",1);
                _originalTab=model.Tabs.Single(t=>t.IsActive).Id;
                Require(model.OpenSubtitle(Path.Combine(mediaFixtures!,"large.ass")),"Large multilingual ASS fixture must open.");return false;
            case 16:
                CheckVirtualizedGrid();model.SelectedEvent=model.Events[^1];return false;
            case 17:
                CheckVirtualizedGrid();Require(window.FindControl<ListBox>("SubtitleRows")!.GetVisualDescendants().OfType<ListBoxItem>().Any(r=>ReferenceEquals(r.DataContext,model.Events[^1])),"Scrolling to the last of 20,000 rows must realize that row.");
                model.SelectedEvent=model.Events[10000];return false;
            case 18:
                CheckVirtualizedGrid();Capture(window,"editor-large-mixed-script",1);
                var activate=model.Registry.InvokeAsync(CommandIds.WorkspaceActivateTab,new(),_originalTab);Require(activate.IsCompletedSuccessfully&&activate.Result,"Return to original editing tab after virtualization probe.");return false;
            default:
                Require(styles.Preview.LastError is null,"Latest style preview failed: "+styles.Preview.LastError);
                if(!styles.Preview.HasCurrentFrame||styles.Preview.DisplayedRevision<_previewRevision)return false;
                CheckStyleFields();Capture(styles,"styles-dark-narrow",1);
                return true;
        }
    }
    private void CheckVirtualizedGrid()
    {
        Require(model.Events.Count==20000,"Large-file verification must use all 20,000 events.");
        var realized=window.FindControl<ListBox>("SubtitleRows")!.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
        Require(realized.Length>0&&realized.Length<100,"Grid must virtualize the large file instead of realizing every event.");
        Require(realized.All(r=>Math.Abs(r.Bounds.Height-30)<0.1),"Recycled mixed-script containers must retain the fixed row height.");
    }
    private byte[] FramePixels()
    {
        using var buffer=model.VideoFrame!.Lock();var pixels=new byte[buffer.RowBytes*buffer.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address,pixels,0,pixels.Length);return pixels;
    }
    private void CheckChrome()
    {
        if(!OperatingSystem.IsWindows())return;
        Require(window.RedundantCaptionHidden,"The framework caption text must be suppressed above the first tab.");
        var strip=window.FindControl<Border>("TitleTabStrip")!;
        var tabs=window.FindControl<ScrollViewer>("TabScroll")!;
        Require(strip.Padding.Right>0,"Caption buttons must reserve their measured area.");
        var right=tabs.TranslatePoint(new Point(tabs.Bounds.Width,0),window)!.Value.X;
        Require(right<=window.ClientSize.Width-strip.Padding.Right,"Tabs must end before caption buttons.");
    }
    private void CheckStandardStyleVisibility()
    {
        var scroll=Named<ScrollViewer>(styles,"StyleProperties");
        Require(scroll.Extent.Height<=scroll.Viewport.Height+1,"All standard style fields must fit without scrolling at ordinary desktop size.");
        foreach(var name in new[]{"StyleFontsize","StyleScaleX","StyleScaleY","StyleSpacing","StyleAngle","StyleMarginL","StyleMarginR","StyleMarginV","StyleEncoding","StyleAlignment"})
        {
            var field=styles.GetVisualDescendants().OfType<Control>().Single(c=>c.Name==name);
            var y=field.TranslatePoint(default,scroll)!.Value.Y;
            Require(y>=0&&y+field.Bounds.Height<=scroll.Bounds.Height+1,name+" must remain visible with the preview.");
        }
        Require(styles.Preview.Bounds.Height>=150,"Preview must remain visible while standard fields are edited.");
    }
    private void CheckGrid()
    {
        var rows=window.FindControl<ListBox>("SubtitleRows")!;
        var containers=rows.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
        Require(containers.Length>=6,"Mixed-script grid fixture must realize all ordinary scripts.");
        Require(containers.All(r=>Math.Abs(r.Bounds.Height-30)<0.1),"Japanese/Burmese/Arabic/Latin/emoji/comment/selected rows must all be exactly 30 DIP.");
        var header=window.FindControl<Grid>("ColumnHeader")!;var labels=header.Children.OfType<TextBlock>().Select(t=>t.Text).ToArray();
        Require(labels.SequenceEqual(new[]{"#","L","Start","End","Style","Actor","Effect","Text"}),"Compact grid must omit Type and label layer L.");
        Require(!window.FindControl<Border>("SubtitleGridRegion")!.GetVisualDescendants().OfType<Button>().Any(),"Grid must not have a permanent button toolbar.");
        Require(rows.GetVisualDescendants().OfType<Grid>().Any(g=>g.Classes.Contains("comment")),"Comment state must reach the theme-aware row class.");
    }
    private static void CheckNavigation(AssTextBox text)
    {
        text.CaretIndex=text.SelectionStart=text.SelectionEnd=1;
        Require(text.MoveAtVisualBoundary(Key.Up,KeyModifiers.None)&&text.CaretIndex==0,"Up on first visual line must reach text start.");
        text.CaretIndex=text.SelectionStart=text.SelectionEnd=1;
        Require(text.MoveAtVisualBoundary(Key.Up,KeyModifiers.Shift)&&text.SelectionStart==1&&text.SelectionEnd==0,"Shift+Up must preserve selection anchor.");
        var end=text.Text!.Length;text.CaretIndex=text.SelectionStart=text.SelectionEnd=end-1;
        Require(text.MoveAtVisualBoundary(Key.Down,KeyModifiers.None)&&text.CaretIndex==end,"Down on final visual line must reach text end.");
        text.CaretIndex=text.SelectionStart=text.SelectionEnd=end-1;
        Require(text.MoveAtVisualBoundary(Key.Down,KeyModifiers.Shift)&&text.SelectionStart==end-1&&text.SelectionEnd==end,"Shift+Down must extend selection to end.");
    }

    private void CheckMainFields()
    {
        foreach(var name in new[]{"LineLayer","MarginLeft","MarginRight","MarginVertical"})
            CheckNumber(window.FindControl<NumericUpDown>(name)!);
        foreach(var name in new[]{"LineStart","LineEnd"})
            Require(window.FindControl<TextBox>(name)!.Bounds.Width>=90,$"{name} must retain its time value width.");
        var text=window.FindControl<AssTextBox>("SubtitleText")!;
        Require(text.Bounds.Width>=300&&text.Bounds.Height>=64,"ASS editor must retain useful text space after timing groups reflow.");
        Require(window.GetVisualDescendants().OfType<AssColorButton>().Count(b=>b.Bounds.Width>=30&&b.Bounds.Height>=24&&b.Command is not null)==4,"All four command-backed color swatches must be realized.");
        var audio=window.FindControl<Grid>("AudioRegion")!;var editor=window.FindControl<Grid>("EventEditorRegion")!;
        Require(audio.TranslatePoint(default,window)!.Value.Y<editor.TranslatePoint(default,window)!.Value.Y,"Audio must remain above the edit panel.");
        Require(window.FindControl<Slider>("AudioVolume")!.Bounds.Height>20&&window.FindControl<Slider>("AudioIntensity")!.Bounds.Height>20&&window.FindControl<Slider>("AudioSize")!.Bounds.Height>20,"Three independent audio controls must be realized.");
        Require(window.FindControl<Avalonia.Controls.Primitives.ScrollBar>("AudioPanner")!.Bounds.Width>200,"Audio must have an attached horizontal panner.");
        var video=window.FindControl<Grid>("VideoRegion")!;var tools=window.FindControl<Border>("VisualToolsBar")!;
        var videoBottom=video.TranslatePoint(new Point(0,video.Bounds.Height),window)!.Value.Y;var toolsTop=tools.TranslatePoint(default,window)!.Value.Y;
        Require(toolsTop-videoBottom<12,"Visual tools must immediately adjoin the video workspace.");
    }

    private void FocusSelectedStyle(string name)
    {
        // Avalonia keyboard focus belongs to ListBoxItem, not the ListBox
        // container. Exercise the same retained row a user tabs/clicks into.
        var row=Named<ListBox>(styles,name).GetVisualDescendants().OfType<ListBoxItem>().Single(i=>i.IsSelected);
        Require(row.Focus(),$"{name}: the selected style row must accept keyboard focus.");
    }

    private static void CheckNumber(NumericUpDown input)
    {
        var entry=input.GetVisualDescendants().OfType<TextBox>().Single(t=>t.Name=="PART_TextBox");
        Require(entry.Bounds.Width>=48&&entry.Bounds.Height>=20,$"{input.Name}: editable numeric area collapsed to {entry.Bounds}.");
        Require(input.Bounds.Width>=86,$"{input.Name}: numeric field width must be usable.");
        Require(input.Bounds.Height<=30,$"{input.Name}: compact numeric field must not retain a tall inner Fluent control (field {input.Bounds}, entry {entry.Bounds}).");
    }

    private void CheckStyleFields()
    {
        foreach(var input in styles.GetVisualDescendants().OfType<NumericUpDown>())CheckNumber(input);
        Require(styles.GetVisualDescendants().OfType<FontPicker>().Any(p=>p.Bounds.Width>=120),"Font picker must retain a usable field.");
        Require(styles.GetVisualDescendants().OfType<AssColorField>().Count(p=>p.Bounds.Width>=140)==4,"Four exact-value color fields must fit the inline editor.");
        Require(styles.GetVisualDescendants().OfType<AssAlignmentPicker>().Any(p=>p.Bounds.Width>=110&&p.Bounds.Height>=80),"Alignment grid must be realized.");
        Require(styles.Preview.Bounds.Width>=400&&styles.Preview.Bounds.Height>=150,"Real Mangetsu preview must have useful bounds.");
    }

    private static void CheckSyntax(AssTextBox text)
    {
        var presenter=text.GetVisualDescendants().OfType<AssTextPresenter>().Single();
        var categories=AssSyntax.Tokenize(text.Text!).Select(t=>t.Kind).ToHashSet();
        var painted=presenter.TextLayout.TextLines.SelectMany(l=>l.TextRuns).OfType<ShapedTextRun>()
            .Select(r=>(r.Properties.ForegroundBrush as ISolidColorBrush)?.Color).ToHashSet();
        foreach(var kind in Enum.GetValues<AssSyntaxKind>())
        {
            Require(categories.Contains(kind),$"Complex syntax fixture must exercise {kind}.");
            Require(presenter.SyntaxBrush(kind) is ISolidColorBrush brush&&painted.Contains(brush.Color),$"{kind} must reach actual shaped text runs in {text.ActualThemeVariant}.");
        }
        var colors=Enum.GetValues<AssSyntaxKind>().Select(k=>(presenter.SyntaxBrush(k) as ISolidColorBrush)?.Color).ToArray();
        Require(colors.Distinct().Count()==colors.Length,"Syntax categories must have distinct semantic brushes.");
    }

    private void PrepareStyleSwitching()
    {
        _first=model.ActiveEditor!.Document.Styles[0];
        Named<NumericUpDown>(styles,"StyleFontsize").Value=72;
        var color=Named<AssColorField>(styles,"StylePrimaryColour");
        color.GetVisualDescendants().OfType<TextBox>().Single().SetCurrentValue(TextBox.TextProperty,"&H80402010");
        Named<TextBox>(styles,"StyleName").SetCurrentValue(TextBox.TextProperty,_first.Name+" 日本");
        Invoke(styles.Registry,"script/style/new");
        Require(_first.Get("Fontsize")=="72"&&_first.Get("PrimaryColour")=="&H80402010",$"Style selection switch must commit the numeric/color draft exactly (size {_first.Get("Fontsize")}, color {_first.Get("PrimaryColour")}).");
        Require(_first.Name.EndsWith(" 日本",StringComparison.Ordinal)&&model.SelectedEvent!.Style==_first.Name,"The same draft must commit its Unicode rename and update event references before switching.");
        _second=model.ActiveEditor.Document.Styles.Last();
        var scriptList=Named<ListBox>(styles,"ScriptStyles");
        Require(scriptList.SelectedItems?.Count==1&&scriptList.SelectedItem as string==_second.Name,"New style must replace the previous selection with only the created style.");
        SelectOne(scriptList,_first.Name);
        Invoke(styles.Registry,"styles/to-library");
        Require(Named<ListBox>(styles,"LibraryStyles").SelectedItems?.Count==1,"Copying one script style must select only its new library preset.");
    }
    private void FinishStyleSwitching()
    {
        var picker=Named<FontPicker>(styles,"StyleFont");
        picker.GetVisualDescendants().OfType<AutoCompleteBox>().Single().SetCurrentValue(AutoCompleteBox.TextProperty,"Missing 日本 字体");
        SelectOne(Named<ListBox>(styles,"ScriptStyles"),_second!.Name);
        var stored=new StyleLibraryStore(model.StyleLibraryPath);
        Require(stored.Editor(stored.Collections[0]).Document.Styles.Any(s=>s.Get("Fontname")=="Missing 日本 字体"),"Library-to-script switching must save the exact missing-font draft.");
        Invoke(styles.Registry,"script/style/delete");
        Require(!model.ActiveEditor!.Document.Styles.Contains(_second)&&model.SelectedEvent!.Style==_first!.Name,"Unused style deletion must succeed without changing event references.");
    }

    private static void SelectOne(ListBox list,string name)
    {
        // Model an ordinary click replacing a multiple selection, without Ctrl.
        list.SelectedItems!.Clear();list.SelectedItem=name;
        Require(list.SelectedItems.Count==1&&list.SelectedItem as string==name,"Style row selection must replace the previous row exactly.");
    }

    private void Capture(Control control,string name,double scale)
    {
        var size=control.Bounds.Size;
        using var bitmap=new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(size.Width*scale),(int)Math.Ceiling(size.Height*scale)),new Vector(96*scale,96*scale));
        bitmap.Render(control);
        var folder=Path.Combine(Path.GetDirectoryName(report)!,"authoring-visuals");Directory.CreateDirectory(folder);
        bitmap.Save(Path.Combine(folder,name+".png"),PngBitmapEncoderOptions.Default);
    }
    private static T Named<T>(Control root,string name) where T:Control=>root.GetVisualDescendants().OfType<T>().Single(c=>c.Name==name);
    private static void Invoke(CommandRegistry registry,string id)
    {
        var result=registry.InvokeAsync(id,new());Require(result.IsCompletedSuccessfully&&result.Result,"Authoring command failed: "+id);
    }
    private static void Require(bool valid,string message){if(!valid)throw new InvalidOperationException(message);}
}
