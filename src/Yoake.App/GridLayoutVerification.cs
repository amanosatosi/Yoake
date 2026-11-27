using Avalonia.Controls;
using Avalonia.VisualTree;
using Yoake.Core.Subtitles;
using Yoake.UI;

namespace Yoake.App;

// Verify the shipping row/header layout against the document's actual metadata,
// not the obsolete assumption that every column is always visible.
internal static class GridLayoutVerification
{
    public static void Verify(MainWindow window,IReadOnlyList<AssEvent> events)
    {
        var header=window.FindControl<Grid>("ColumnHeader")!;
        var list=window.FindControl<ListBox>("SubtitleRows")!;
        var labels=new[]{"#","L","Start","End","Style","Actor","Effect","ML","MR","MV","Text"};
        static bool Used(string value)=>!string.IsNullOrWhiteSpace(value);
        static bool NonZero(string value)=>Used(value)&&(!int.TryParse(value,out var number)||number!=0);
        bool[] visible=[true,events.Any(e=>NonZero(e.DisplayLayer)),true,true,true,
            events.Any(e=>Used(e.DisplayActor)),events.Any(e=>Used(e.DisplayEffect)),
            events.Any(e=>NonZero(e.DisplayMarginL)),events.Any(e=>NonZero(e.DisplayMarginR)),
            events.Any(e=>NonZero(e.DisplayMarginV)),true];
        var headers=header.Children.OfType<TextBlock>().OrderBy(t=>Grid.GetColumn(t)).ToArray();
        Require(header.ColumnDefinitions.Count==labels.Length&&headers.Select(t=>t.Text).SequenceEqual(labels),
            "Grid header must contain the eleven columns in the expected order.");
        var rows=list.GetVisualDescendants().OfType<Grid>().Where(g=>Equals(g.Tag,"SubtitleRow")&&g.Bounds.Height>0).ToArray();
        Require(rows.Length>0,"Grid must realize at least one laid-out subtitle row.");
        foreach(var row in rows)
            Require(row.ColumnDefinitions.Count==labels.Length,"Realized row must have the same eleven columns as its header.");
        for(var i=0;i<labels.Length;i++)
        {
            var column=header.ColumnDefinitions[i];var width=column.ActualWidth;
            Require(double.IsFinite(width)&&(visible[i]?width>0:width==0),
                $"Header {labels[i]} must be {(visible[i]?"visible":"collapsed")}; actual width {width}.");
            Require(headers[i].IsVisible==visible[i]&&Grid.GetColumn(headers[i])==i,
                $"Header {labels[i]} visibility or position differs from the document.");
            foreach(var row in rows)
            {
                var cell=row.Children.OfType<TextBlock>().Single(t=>Grid.GetColumn(t)==i);
                var actual=row.ColumnDefinitions[i].ActualWidth;
                Require(cell.IsVisible==visible[i]&&double.IsFinite(actual)&&(visible[i]?actual>0:actual==0),
                    $"Row cell {labels[i]} visibility/width differs from its header (width {actual}).");
                if(i<labels.Length-1)
                    Require(row.ColumnDefinitions[i].Width==column.Width&&Math.Abs(actual-width)<=1,
                        $"Row cell {labels[i]} must align with its header: {actual} vs {width}.");
            }
        }
    }

    private static void Require(bool condition,string message)
    {if(!condition)throw new InvalidOperationException(message);}
}
