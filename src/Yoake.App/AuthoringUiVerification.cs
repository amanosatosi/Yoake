using Avalonia;
using Avalonia.Controls;
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
internal sealed class AuthoringUiVerification(MainWindow window, MainWindowViewModel model, StylesWindow styles, string report)
{
    private int _stage;
    private long _previewRevision;
    private double _normalEditorWidth;
    private AssStyle? _first, _second;
    private const string Sample = @"{\fad(200,200)\bord3\1c&HFFFFFF&\3c&H000000&}<仮|かり>の糸\N{\k20}こ{\k15}れ{\k30}は{\1grd(0,&HFF0000&,&H0000FF&)}テスト
{\bord2\t(0,500,\bord6\1c&H00FFFF&)}Text မြန်မာ é 👩‍👩‍👧‍👦
{\fnArial\future(opaque)\p1}m 0 0 l 20 0 20 20{\p0}";

    public bool Tick()
    {
        var text=window.FindControl<AssTextBox>("SubtitleText")!;
        switch(_stage++)
        {
            case 0:
                window.RequestedThemeVariant=styles.RequestedThemeVariant=ThemeVariant.Dark;
                text.SetCurrentValue(TextBox.TextProperty,Sample);
                Invoke(model.Registry,CommandIds.EditCommit);
                text.CaretIndex=text.SelectionStart=text.SelectionEnd=Sample.Length;
                return false;
            case 1:
                foreach(var scale in new[]{1d,1.25,1.5,2})Capture(window,$"editor-dark-{scale*100:0}",scale);
                Capture(styles,"styles-dark-normal",1);
                CheckMainFields();CheckSyntax(text);
                _normalEditorWidth=window.FindControl<Grid>("EventEditorRegion")!.Bounds.Width;
                window.RequestedThemeVariant=styles.RequestedThemeVariant=ThemeVariant.Light;
                return false;
            case 2:
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
                Require(Named<ListBox>(styles,"ScriptStyles").Focus(),"The script list must accept keyboard focus.");
                return false;
            case 6:
                Require(Named<FontPicker>(styles,"StyleFont").FontName==_first!.Get("Fontname"),"Returning to the already selected script row must activate its inline draft.");
                Require(Named<ListBox>(styles,"LibraryStyles").Focus(),"The library list must accept keyboard focus.");
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
            default:
                Require(styles.Preview.LastError is null,"Latest style preview failed: "+styles.Preview.LastError);
                if(!styles.Preview.HasCurrentFrame||styles.Preview.DisplayedRevision<_previewRevision)return false;
                CheckStyleFields();Capture(styles,"styles-dark-narrow",1);
                return true;
        }
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
        Invoke(styles.Registry,"script/style/new");
        Require(_first.Get("Fontsize")=="72"&&_first.Get("PrimaryColour")=="&H80402010","Style selection switch must commit the numeric/color draft exactly.");
        _second=model.ActiveEditor.Document.Styles.Last();
        var scriptList=Named<ListBox>(styles,"ScriptStyles");scriptList.SelectedItem=_first.Name;
        Invoke(styles.Registry,"styles/to-library");
    }
    private void FinishStyleSwitching()
    {
        var picker=Named<FontPicker>(styles,"StyleFont");
        picker.GetVisualDescendants().OfType<AutoCompleteBox>().Single().SetCurrentValue(AutoCompleteBox.TextProperty,"Missing 日本 字体");
        Named<ListBox>(styles,"ScriptStyles").SelectedItem=_second!.Name;
        var stored=new StyleLibraryStore(model.StyleLibraryPath);
        Require(stored.Editor(stored.Collections[0]).Document.Styles.Any(s=>s.Get("Fontname")=="Missing 日本 字体"),"Library-to-script switching must save the exact missing-font draft.");
        Invoke(styles.Registry,"script/style/delete");
        Require(!model.ActiveEditor!.Document.Styles.Contains(_second)&&model.SelectedEvent!.Style==_first!.Name,"Unused style deletion must succeed without changing event references.");
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
