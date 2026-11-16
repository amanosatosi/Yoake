using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yoake.UI;
using Yoake.UI.Controls;

namespace Yoake.App;

internal static class TypographyUiAcceptanceVerification
{
    private static readonly Uri FontsKey=new("fonts:yoake-ui-verification");
    private sealed class FixtureCollection(string path):FontCollectionBase
    {
        public override Uri Key=>FontsKey;
        public void Load()
        { using var stream=File.OpenRead(path);Require(TryAddGlyphTypeface(stream,out _),"Load the actual pinned MUA Office face."); }
    }
    public static FontManagerOptions Options(StartupOptions options)
    {
        var result=UiFontFallbacks.CreateOptions();
        if(options.AssistantsAcceptance)
            result.FontFamilyMappings=new Dictionary<string,FontFamily> { ["MUA Office"]=new(FontsKey+"#MUA Office") };
        return result;
    }
    public static void Install(StartupOptions options)
    {
        if(!options.AssistantsAcceptance)return;
        var root=Path.Combine(options.VerificationMedia!,"fonts");
        var system=FontManager.Current.SystemFonts as FontCollectionBase??throw new InvalidOperationException("The system font collection must accept isolated test faces.");
        foreach(var name in new[]{"UI Latin.ttf","UI Legacy Trap.ttf"})
        { using var stream=File.OpenRead(Path.Combine(root,name));Require(system.TryAddGlyphTypeface(stream,out _),"Load isolated font fixture: "+name); }
        // MUA is addressable by name but not in the general system-cache search,
        // just like an installed face which the UI has not used yet. The cached
        // cmap-only trap must never win fallback for Burmese Unicode.
        var collection=new FixtureCollection(Path.Combine(root,"MUA Office.ttf"));collection.Load();
        FontManager.Current.AddFontCollection(collection);
        _=new Typeface("Yoake UI Legacy Trap").GlyphTypeface;
    }
    public static async Task VerifyAsync(MainWindow owner,StartupOptions options)
    {
        const string sample="marshall-sensei မြန်မာ ကျွန်တော် ပြန်ကြည့်ကြရအောင်";
        var family=new FontFamily("Yoake UI Latin");
        var panel=new StackPanel { Margin=new Thickness(16),Spacing=10 };
        panel.Children.Add(new TextBlock { Text="Burmese Unicode fallback · MUA Office",FontSize=20 });
        var label=new TextBlock { Text=sample,FontFamily=family,FontSize=20 };
        var original=new TextBox { Text=sample,IsReadOnly=true,FontFamily=family,FontSize=20 };
        var input=new AssTextBox { SourceText=sample,FontFamily=family,FontSize=20 };
        var grid=new SubtitleGridText { SourceText=sample,FontFamily=family,FontSize=20 };
        panel.Children.Add(label);panel.Children.Add(original);panel.Children.Add(input);panel.Children.Add(grid);
        var window=new Window { Name="TypographyVerification",Title="Burmese UI shaping",Width=1100,Height=340,Content=panel,WindowStartupLocation=WindowStartupLocation.CenterOwner };
        window.Show(owner);
        try
        {
            await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout,DispatcherPriority.Background);
            var originalPresenter=original.GetVisualDescendants().OfType<TextPresenter>().Single();
            var inputPresenter=input.GetVisualDescendants().OfType<AssTextPresenter>().Single();
            // Independently recorded with hb-shape using the exact SHA-pinned
            // MUA Office 2.10 fixture. Includes pre-base reordering and the
            // multiple-character medial ligature (629), not just cmap coverage.
            int[] expected=[292,257,252,290,257,276,232,629,252,290,281,248,276,290,292,253,252,290,514,232,242,287,290,514,232,259,281,265,276,236,290];
            void Check(TextLayout layout,string control)
            {
                var runs=layout.TextLines.SelectMany(line=>line.TextRuns).OfType<ShapedTextRun>().ToArray();
                var myanmar=runs.Where(run=>run.Text.Span.IndexOfAnyInRange('\u1000','\u109f')>=0).ToArray();
                Require(myanmar.Length>0&&myanmar.All(run=>run.GlyphRun.GlyphTypeface.FamilyName=="MUA Office"),"All Burmese UI runs must resolve the preferred Unicode font, not the cached legacy face.");
                var glyphs=myanmar.SelectMany(run=>run.GlyphRun.GlyphInfos).Where(g=>g.GlyphIndex!=3).Select(g=>(int)g.GlyphIndex);
                Require(glyphs.SequenceEqual(expected),"Burmese UI glyphs must match the shaped MUA reference, including combining forms and reordering.");
                Require(runs.Where(run=>run.Text.ToString().Contains("marshall",StringComparison.Ordinal)).All(run=>run.GlyphRun.GlyphTypeface.FamilyName=="Yoake UI Latin"),$"Burmese fallback must preserve the primary Latin UI font in {control}: "+string.Join(",",runs.Select(run=>run.GlyphRun.GlyphTypeface.FamilyName)));
            }
            Check(label.TextLayout,"TextBlock");Check(originalPresenter.TextLayout,"Original");Check(inputPresenter.TextLayout,"ASS editor");Check(grid.TextLayout,"grid");
            input.CaretIndex=sample.IndexOf("မြန်မာ",StringComparison.Ordinal)+2;input.SelectionStart=input.CaretIndex-1;input.SelectionEnd=input.CaretIndex;
            await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout,DispatcherPriority.Background);Check(inputPresenter.TextLayout,"selected ASS editor");
            Require(input.SourceText==sample&&original.Text==sample&&grid.SourceText==sample,"Shaping and selection must leave source text unchanged.");
            using var bitmap=new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width,(int)window.Bounds.Height),new Vector(96,96));
            bitmap.Render(window);bitmap.Save(Path.Combine(options.ProfileDirectory!,"burmese-ui-shaping.png"));
            Console.WriteLine("PASS: actual MUA Office glyph sequence in ordinary text, Original, ASS editor and grid; cached cmap-only trap rejected; Latin font and Burmese source preserved.");
        }
        finally{window.Close();}
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
