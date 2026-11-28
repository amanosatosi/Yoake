using System.Globalization;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using Yoake.UI;
using Yoake.UI.Controls;
using Yoake.Core.Subtitles;
using Yoake.Native;

namespace Yoake.FontDiagnostics;

public sealed class DiagnosticApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://Yoake.UI/")) { Source=new Uri("avares://Yoake.UI/Controls/AssEditor.axaml") });
    }
}

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if(args.Length!=4) { Console.Error.WriteLine("Usage: FontDiagnostics font.ttf samples.txt output-dir ui|mangetsu");return 2; }
        var font=Path.GetFullPath(args[0]);var output=Path.GetFullPath(args[2]);
        Directory.CreateDirectory(output);
        var samples=File.ReadAllLines(args[1]).Where(s=>s.Length>0).ToArray();
        using var report=new StreamWriter(Path.Combine(output,args[3]+".tsv"));
        report.AutoFlush=true;
        try
        {
            report.WriteLine("# SHA256="+Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(font))));
            report.WriteLine("# OS="+Environment.OSVersion+"; runtime="+Environment.Version+"; dynamic="+System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported);
            AppBuilder.Configure<DiagnosticApp>().With(UiFontFallbacks.CreateOptions()).UsePlatformDetect().SetupWithoutStarting();
            report.WriteLine("# Avalonia="+typeof(FontManager).Assembly.GetName().Version);
            using var stream=File.OpenRead(font);
            var system=(FontCollectionBase)FontManager.Current.SystemFonts;
            if(!system.TryAddGlyphTypeface(stream,out var face)||face is null)throw new InvalidOperationException("Cannot register diagnostic face: "+font);
            report.WriteLine("# loaded="+face.FamilyName+"; glyphs="+face.GlyphCount+"; typeface="+face.PlatformTypeface.GetType().FullName);
            if(args[3]=="mangetsu")
            {
                WindowsNativeRuntime.ConfigurePackagedRuntime(AppContext.BaseDirectory);
                NativeRender(font,face.FamilyName,samples,output,report);
                return 0;
            }
            if(args[3]!="ui")return 2;
            UiRender(face,samples,output,report);
            return 0;
        }
        catch(Exception e){report.WriteLine("# ERROR "+e);Console.Error.WriteLine(e);return 1;}
    }

    private static void Dump(TextWriter report,string pipeline,int sample,string text,string family,IEnumerable<GlyphInfo> glyphs)
    {
        foreach(var g in glyphs)
            report.WriteLine(FormattableString.Invariant($"{pipeline}\t{sample}\t{text}\t{family}\t{g.GlyphIndex}\t{g.GlyphCluster}\t{g.GlyphAdvance:R}\t{g.GlyphOffset.X:R}\t{g.GlyphOffset.Y:R}"));
    }

    private static void DumpLayout(TextWriter report,string pipeline,int sample,TextLayout layout)
    {
        foreach(var run in layout.TextLines.SelectMany(l=>l.TextRuns).OfType<ShapedTextRun>())
            Dump(report,pipeline,sample,run.Text.ToString(),run.GlyphRun.GlyphTypeface.FamilyName,run.GlyphRun.GlyphInfos);
    }

    private static void UiRender(GlyphTypeface face,string[] samples,string output,TextWriter report)
    {
        const double em=32;
        report.WriteLine("pipeline\tsample\trun_text\tresolved_family\tglyph\tcluster\tadvance_dip\toffset_x_dip\toffset_y_dip");
        var label=new TextBlock{FontSize=em,Foreground=Brushes.Black};
        var plain=new TextBox{FontSize=em,IsReadOnly=true};
        var editor=new AssTextBox{FontSize=em,BetterView=false};
        var grid=new SubtitleGridText{FontSize=em,Foreground=Brushes.Black};
        var panel=new StackPanel{Spacing=12,Margin=new Thickness(12),Background=Brushes.White};
        foreach(var c in new Control[]{label,plain,editor,grid})panel.Children.Add(c);
        var window=new Window{Width=1100,Height=520,Content=panel,Title="Myanmar diagnostics",RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Light};
        window.Show();
        try
        {
            for(var i=0;i<samples.Length;i++)
            {
                var sample=samples[i];
                foreach(var culture in new[]{CultureInfo.InvariantCulture,CultureInfo.GetCultureInfo("my-MM")})
                {
                    using var shaped=TextShaper.Current.ShapeText(sample,new TextShaperOptions(face,em,culture:culture));
                    Dump(report,"avalonia-direct-"+(culture.Name.Length==0?"invariant":culture.Name),i,sample,face.FamilyName,shaped);
                }
                foreach(var mode in new[]{"default","selected","reset","reselected"})
                {
                    UiFontResources.Apply(mode is "selected" or "reselected"?face.FamilyName:null,12);
                    window.FontFamily=(FontFamily)Application.Current!.Resources["YoakeUiFontFamily"]!;
                    label.Text=plain.Text=grid.SourceText=sample;
                    editor.SourceText=sample;
                    editor.SelectionStart=editor.SelectionEnd=0;
                    window.UpdateLayout();
                    var plainPresenter=plain.GetVisualDescendants().OfType<TextPresenter>().Single();
                    var editorPresenter=editor.GetVisualDescendants().OfType<AssTextPresenter>().Single();
                    DumpLayout(report,mode+"-label",i,label.TextLayout);
                    DumpLayout(report,mode+"-plain",i,plainPresenter.TextLayout);
                    DumpLayout(report,mode+"-editor",i,editorPresenter.TextLayout);
                    DumpLayout(report,mode+"-grid",i,grid.TextLayout);
                    var at=sample.IndexOfAny("မြန်မာကျောင်းပြည်".ToCharArray());
                    if(at>=0)
                    {
                        editor.SelectionStart=at;editor.SelectionEnd=at+1;window.UpdateLayout();
                        DumpLayout(report,mode+"-editor-selection",i,editorPresenter.TextLayout);
                        editor.SelectionStart=editor.SelectionEnd=0;
                        editor.SetCurrentValue(TextBox.TextProperty,sample+" က");window.UpdateLayout();
                        DumpLayout(report,mode+"-editor-typed",i,editorPresenter.TextLayout);
                        report.WriteLine("# edited source="+editor.SourceText);
                        editor.SourceText=sample;window.UpdateLayout();
                    }
                    using var bitmap=new RenderTargetBitmap(new PixelSize(1100,520),new Vector(96,96));
                    bitmap.Render(window);bitmap.Save(Path.Combine(output,$"{i:D2}-{mode}.png"));
                }
            }
        }
        finally{window.Close();}
    }

    private static void NativeRender(string font,string family,string[] samples,string output,TextWriter report)
    {
        // Embed the exact input font in the ASS track instead of relying on
        // Windows installed-font substitution. Keep full native renderer logs.
        var header="[Script Info]\nScriptType: v4.00+\nPlayResX: 1100\nPlayResY: 180\n[V4+ Styles]\nFormat: "+
            string.Join(",",AssDocument.StyleFormat)+"\nStyle: Default,"+family+",32,&H00000000,&H00000000,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,0,0,7,12,12,12,1\n[Fonts]\nfontname: diagnostic.ttf\n"+
            string.Join("\n",AssAttachmentCodec.Encode(File.ReadAllBytes(font)))+"\n\n[Events]\nFormat: "+string.Join(",",AssDocument.EventFormat)+"\n";
        using var renderer=new MangetsuSubtitleRenderer(header,1100,180);
        for(var i=0;i<samples.Length;i++)
        {
            var ass=header+"Dialogue: 0,0:00:00.00,0:00:10.00,Default,,0,0,0,,"+samples[i]+"\n";
            File.WriteAllText(Path.Combine(output,$"{i:D2}.ass"),ass);
            renderer.UpdateTrack(ass);
            var pixels=Enumerable.Repeat((byte)255,1100*180*4).ToArray();
            renderer.Composite(new DecodedVideoFrame(1100,180,4400,pixels),1);
            using var ppm=File.Create(Path.Combine(output,$"{i:D2}-mangetsu.ppm"));
            ppm.Write(System.Text.Encoding.ASCII.GetBytes("P6\n1100 180\n255\n"));
            var rgb=new byte[1100*180*3];int ink=0;
            for(int x=0,y=0;x<pixels.Length;x+=4,y+=3)
            {rgb[y]=pixels[x+2];rgb[y+1]=pixels[x+1];rgb[y+2]=pixels[x];if(rgb[y]<250||rgb[y+1]<250||rgb[y+2]<250)ink++;}
            ppm.Write(rgb);
            report.WriteLine(FormattableString.Invariant($"sample={i}\tink_pixels={ink}\ttext={samples[i]}"));
        }
    }
}
