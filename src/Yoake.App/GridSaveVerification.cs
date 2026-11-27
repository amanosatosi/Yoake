using System.Globalization;
using Yoake.Core.Media;
using Yoake.Core.Subtitles;
using Yoake.UI;
using Yoake.UI.ViewModels;

namespace Yoake.App;

/// <summary>Exact source verification for subtitle-only GUI saves. Only the two
/// grid project-view properties may be updated; media/link tests use their own contracts.</summary>
internal sealed class GridSaveVerification
{
    private readonly string _before;
    private readonly int _row;
    private readonly int? _scroll;

    private GridSaveVerification(string before,int row,int? scroll)
    {
        _before=before;_row=row;_scroll=scroll;
    }

    public static GridSaveVerification Capture(MainWindow window,MainWindowViewModel model,string before)
        => new(before,Math.Max(0,model.Events.ToList().IndexOf(model.SelectedEvent!)),window.GetSubtitleGridScrollRow());

    public string VerifySaved(MainWindowViewModel model,string path,string message)
    {
        var expected=AssDocument.Parse(_before);
        var previous=SubtitleProjectView.Read(expected);
        var saved=model.ActiveEditor!.Document;
        var view=SubtitleProjectView.Read(saved);
        // A non-realized grid can have no measured scroll position. In that
        // case the saved document supplies only that fallback value, never
        // the expected event data or any other project property.
        var scroll=_scroll??view.ScrollPosition;
        if(_row!=0||scroll is >0||previous.ActiveLine is not null||previous.ScrollPosition is not null||
            view.ActiveLine is not null||view.ScrollPosition is not null)
        {
            if(view.ActiveLine!=_row||view.ScrollPosition is null||view.ScrollPosition!=scroll)
                throw new InvalidOperationException(message+$" Grid view expected active row {_row}, scroll {_scroll?.ToString(CultureInfo.InvariantCulture)??"(unrealized)"}; actual row {view.ActiveLine}, scroll {view.ScrollPosition}.");
            new SubtitleEditor(expected).SetProjectProperties(new Dictionary<string,string>
            {
                ["Active Line"]=_row.ToString(CultureInfo.InvariantCulture),
                ["Scroll Position"]=view.ScrollPosition.Value.ToString(CultureInfo.InvariantCulture)
            });
        }
        var exact=expected.Serialize();
        RequireExact(exact,saved.Serialize(),path,message+" (in-memory document)");
        RequireExact(exact,File.ReadAllText(path),path,message+" (saved UTF-8 source)");
        RequireExact(exact,AssDocument.Load(path).Serialize(),path,message+" (parsed file)");
        return exact;
    }

    internal static void RequireExact(string expected,string actual,string path,string message)
    {
        if(string.Equals(expected,actual,StringComparison.Ordinal))return;
        var index=0;var length=Math.Min(expected.Length,actual.Length);
        while(index<length&&expected[index]==actual[index])index++;
        var line=1;for(var i=0;i<index;i++)if(expected[i]=='\n')line++;
        var expectedPath=path+".expected.ass";var actualPath=path+".actual.ass";
        File.WriteAllText(expectedPath,expected);File.WriteAllText(actualPath,actual);
        throw new InvalidOperationException(message+$" First difference at UTF-16 offset {index}, line {line}; expected length {expected.Length}, actual length {actual.Length}. See {expectedPath} and {actualPath}.");
    }
}
