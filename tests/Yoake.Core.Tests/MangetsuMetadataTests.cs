using System.Text;
using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class MangetsuMetadataTests
{
    private static string Event(string kind,string style,string actor,string effect,string text)=>$"{kind}: 0,0:00:00.00,0:00:01.00,{style},{actor},0,0,0,{effect},{text}\n";
    private static AssDocument ImportSource()
    {
        var document=AssDocument.CreateEmpty();
        var source=document.Serialize()+
            Event("Comment","Default","mangetsu-colorcode-applied-styles","mangetsu-colorcoding","{Default}")+
            Event("Comment","Default","日本語 မြန်မာ","mangetsu-colorcoding",@"{\c&HFF0000&\future(opaque)}")+
            Event("Comment","Default","","mangetsu-style",@"{\vert1\boxp(1,2,3,4)\future(unknown)}")+
            Event("Comment","Default","","",@"{uketsugu}{\rDefault}first 日本語")+
            Event("Dialogue","Default","","",@"{uketsugu}second မြန်မာ")+
            Event("Dialogue","Default","","","ordinary source dialogue");
        return AssDocument.Parse(source);
    }
    [Fact]
    public void StyleImportPreservesMetadataOrderingInheritedEventsAndRemapsCollisions()
    {
        var destination=AssDocument.CreateEmpty();var editor=new SubtitleEditor(destination);
        editor.SetMangetsuMetadata(MangetsuMetadataKind.Actor,"Existing",@"{\c&H00FF00&}");
        var ordinary=editor.Insert(null,true);ordinary.Text="destination dialogue";
        var before=destination.Serialize();var source=ImportSource();var sourceBefore=source.Serialize();
        var styles=editor.CopyStyles(source.Styles,source);
        Assert.Equal("Default 1",styles.Single().Name);
        var records=destination.Events.ToArray();
        Assert.Equal(MangetsuMetadataKind.Style,MangetsuMetadata.Kind(records[0]));
        Assert.Equal("Default 1",records[0].Style);Assert.Contains(@"\future(unknown)",records[0].Text);
        var metadata=MangetsuMetadata.Initial(destination);
        Assert.Equal(4,metadata.Length);Assert.All(metadata.Skip(1),r=>Assert.Equal(MangetsuMetadataKind.Actor,MangetsuMetadata.Kind(r)));
        Assert.Equal("{Default 1}",metadata.Single(e=>e.Actor=="mangetsu-colorcode-applied-styles").Text);
        var inherited=records.Where(MangetsuMetadata.IsInherited).ToArray();
        Assert.Equal(2,inherited.Length);Assert.Contains(@"{\rDefault 1}first 日本語",inherited[0].Text);Assert.Contains("second မြန်မာ",inherited[1].Text);
        Assert.Same(ordinary,records[^1]);Assert.DoesNotContain(records,e=>e.Text=="ordinary source dialogue");
        Assert.Equal(sourceBefore,source.Serialize());
        var after=destination.Serialize();editor.Undo.Undo();Assert.Equal(before,destination.Serialize());
        editor.Undo.Redo();Assert.Equal(after,destination.Serialize());
        var bytes=new UnicodeEncoding(false,true,true).GetPreamble().Concat(new UnicodeEncoding(false,true,true).GetBytes(after)).ToArray();
        Assert.Equal(after,AssDocument.LoadBytes(bytes).Serialize());
    }
    [Fact]
    public void ClipboardAndLibraryTransfersRetainExtensionsAndInheritedRecords()
    {
        var source=ImportSource();var sourceEditor=new SubtitleEditor(source);var clipboard=sourceEditor.CopyStyleText(source.Styles);
        var destination=AssDocument.CreateEmpty();var editor=new SubtitleEditor(destination);editor.PasteStyles(clipboard);
        Assert.Contains(destination.Events,e=>MangetsuMetadata.Kind(e)==MangetsuMetadataKind.Style);
        Assert.Equal(2,destination.Events.Count(MangetsuMetadata.IsInherited));
        var root=Path.Combine(Path.GetTempPath(),"Yoake-library-"+Guid.NewGuid());
        try
        {
            var path=Path.Combine(root,"styles.json");var library=new StyleLibraryStore(path);var collection=library.Create("Mangetsu");
            library.Import(source,library.Editor(collection));library.Save();
            var restored=new StyleLibraryStore(path);var document=restored.Editor(restored.Collections.Single()).Document;
            Assert.Equal(new[] { MangetsuMetadataKind.Style,MangetsuMetadataKind.Actor,MangetsuMetadataKind.Actor },MangetsuMetadata.Initial(document).Select(MangetsuMetadata.Kind));
            Assert.Equal(2,document.Events.Count(MangetsuMetadata.IsInherited));
        }
        finally { if(Directory.Exists(root))Directory.Delete(root,true); }
    }
    [Fact]
    public void AuthoringUpdatesAndRemovalAreUndoableAndPreserveUnrelatedSource()
    {
        var document=AssDocument.CreateEmpty();var editor=new SubtitleEditor(document);var line=editor.Insert(null,true);line.Text="ordinary";
        var before=document.Serialize();
        editor.SetMangetsuMetadata(MangetsuMetadataKind.Actor,"မြန်မာ",@"{\1c&HFF0000&}");
        editor.SetMangetsuMetadata(MangetsuMetadataKind.Style,"Default",@"{\boxp(10,20,5,15)\unknown(x)}");
        Assert.Equal(MangetsuMetadataKind.Style,MangetsuMetadata.Kind(document.Events[0]));Assert.Same(line,document.Events[^1]);
        var metadata=document.Events[0];var withBoth=document.Serialize();
        editor.SetMangetsuMetadata(MangetsuMetadataKind.Style,"Default",@"{\vert1}");Assert.Same(metadata,document.Events[0]);
        editor.Undo.Undo();Assert.Equal(withBoth,document.Serialize());
        editor.RemoveMangetsuMetadata(MangetsuMetadataKind.Style,"Default");Assert.DoesNotContain(metadata,document.Events);
        editor.Undo.Undo();Assert.Equal(withBoth,document.Serialize());
        editor.Undo.Undo();editor.Undo.Undo();Assert.Equal(before,document.Serialize());
    }
    [Fact]
    public void InvalidTargetsAndLateInactiveMetadataCannotAlterActiveDefaults()
    {
        var document=AssDocument.CreateEmpty();var editor=new SubtitleEditor(document);var before=document.Serialize();
        Assert.Throws<ArgumentException>(()=>editor.SetMangetsuMetadata(MangetsuMetadataKind.Style,"Missing",@"{\vert1}"));Assert.Equal(before,document.Serialize());
        var source=AssDocument.Parse(AssDocument.CreateEmpty().Serialize()+Event("Dialogue","Default","","","first")+Event("Comment","Default","","mangetsu-style",@"{\vert1}"));
        editor.CopyStyles(source.Styles,source);Assert.Empty(document.Events);
    }
    [Fact]
    public void MetadataTargetMatchingUsesRendererLeadingSpaceAndDefaultRules()
    {
        var document=AssDocument.Parse(AssDocument.CreateEmpty().Serialize()+Event("Comment"," *default",""," mangetsu-style",@"{\vert1}"));
        var editor=new SubtitleEditor(document);var metadata=document.Events.Single();var before=document.Serialize();
        Assert.Equal(MangetsuMetadataKind.Style,MangetsuMetadata.Kind(metadata));
        editor.SetMangetsuMetadata(MangetsuMetadataKind.Style,"Default",@"{\vert0}");Assert.Same(metadata,Assert.Single(document.Events));Assert.Equal(@"{\vert0}",metadata.Text);
        editor.Undo.Undo();Assert.Equal(before,document.Serialize());
        metadata.Effect="mangetsu-style ";Assert.Equal(MangetsuMetadataKind.None,MangetsuMetadata.Kind(metadata));
    }
    [Fact]
    public void NonReservedActorSpellingDoesNotRewriteOpaquePayloadAsAWhitelist()
    {
        var source=ImportSource();source.Events[0].Actor="{{mangetsu-colorcode-applied-styles}}";
        var destination=AssDocument.CreateEmpty();new SubtitleEditor(destination).CopyStyles(source.Styles,source);
        Assert.Equal("{Default}",destination.Events.Single(e=>e.Actor=="{{mangetsu-colorcode-applied-styles}}").Text);
    }
    [Fact]
    public void UnresolvedStyleExtensionMetadataSurvivesImportAndRoundTrip()
    {
        var source=AssDocument.Parse(AssDocument.CreateEmpty().Serialize()+Event("Comment","Future style","","mangetsu-style",@"{\futureExtension(opaque)}"));
        var destination=AssDocument.CreateEmpty();var editor=new SubtitleEditor(destination);editor.CopyStyles(source.Styles,source);
        var metadata=Assert.Single(destination.Events);Assert.Equal("Future style",metadata.Style);Assert.Equal(@"{\futureExtension(opaque)}",metadata.Text);
        Assert.Equal(destination.Serialize(),AssDocument.Parse(destination.Serialize()).Serialize());
        var conflicting=AssDocument.CreateEmpty();var other=new SubtitleEditor(conflicting);var style=conflicting.Styles[0].Clone();style.Name="Future style";other.AddStyle(style);var before=conflicting.Serialize();
        Assert.Throws<InvalidOperationException>(()=>other.CopyStyles(source.Styles,source));Assert.Equal(before,conflicting.Serialize());
    }
}
