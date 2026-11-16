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
            "Format: Layer,Start,End,Style,Name,MarginL,MarginR,MarginV,Effect,Extra,Text\n"+
            @"Comment: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,{uketsugu},future-α,{\rDefault}first 日本語"+"\n"+
            "Dialogue: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,{uketsugu},raw🙂,second မြန်မာ, untouched\n"+
            "Format: "+string.Join(",",AssDocument.EventFormat)+"\n"+
            Event("Dialogue","Default","","","ordinary {uketsugu} source dialogue");
        var result=AssDocument.Parse(source);
        var first=result.Events[3];first.Layer="7";first.Actor="継承 日本語";first.Start="0:00:05.12";first.End="0:00:07.34";first.MarginL="101";first.MarginR="202";first.MarginV="303";
        var second=result.Events[4];second.Layer="2";second.Actor="မြန်မာ";second.Start="0:00:01.23";second.End="0:00:03.45";second.MarginL="404";second.MarginR="505";second.MarginV="606";
        return result;
    }
    [Theory]
    [InlineData("{uketsugu}","",true)]
    [InlineData("{uketsugu}","plain inherited content",true)]
    [InlineData("","{uketsugu}",false)]
    [InlineData("","ordinary {uketsugu} subtitle text",false)]
    [InlineData("other effect","{uketsugu}",false)]
    [InlineData("prefix{uketsugu}","ordinary",false)]
    [InlineData("{uketsugu}suffix","ordinary",false)]
    public void InheritanceUsesOnlyTheEffectMarker(string effect,string text,bool expected)
    {
        var document=AssDocument.Parse(AssDocument.CreateEmpty().Serialize()+Event("Dialogue","Default","Actor",effect,text));
        Assert.Equal(expected,MangetsuMetadata.IsInherited(Assert.Single(document.Events)));
    }
    [Fact]
    public void StyleImportPreservesMetadataOrderingInheritedEventsAndRemapsCollisions()
    {
        var destination=AssDocument.CreateEmpty();var editor=new SubtitleEditor(destination);
        editor.SetMangetsuMetadata(MangetsuMetadataKind.Actor,"Existing",@"{\c&H00FF00&}");
        var ordinary=editor.Insert(null,true);ordinary.Text="destination {uketsugu} dialogue";
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
        Assert.Equal(2,inherited.Length);Assert.All(inherited,line=>Assert.Equal("{uketsugu}",line.Effect));Assert.Equal(new[] { "0:00:05.12","0:00:01.23" },inherited.Select(line=>line.Start));Assert.Contains(@"{\rDefault 1}first 日本語",inherited[0].Text);Assert.Contains("second မြန်မာ",inherited[1].Text);Assert.Equal("future-α",inherited[0].Get("Extra"));Assert.Equal("raw🙂",inherited[1].Get("Extra"));
        Assert.Same(ordinary,records[^1]);Assert.DoesNotContain(records,e=>e.Text=="ordinary {uketsugu} source dialogue");
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
    [Theory]
    [InlineData("import")]
    [InlineData("clipboard")]
    [InlineData("library")]
    public void EveryTransferRetainsOpaqueFieldsAndInheritedOrderThroughDiskAndUndo(string route)
    {
        var source=ImportSource();var original=source.Serialize();
        var root=Path.Combine(Path.GetTempPath(),"Yoake-transfer-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            var destination=AssDocument.CreateEmpty();var editor=new SubtitleEditor(destination);
            editor.SetMangetsuMetadata(MangetsuMetadataKind.Style,"Default",@"{\future(destination)}");
            editor.SetMangetsuMetadata(MangetsuMetadataKind.Actor,"Existing",@"{\1c&H112233&}");
            var existingInheritance=editor.Insert(null,true);existingInheritance.Effect="{uketsugu}";existingInheritance.Actor="Existing inheritance";existingInheritance.Text="destination inherited template";
            var inheritanceFields=existingInheritance.FieldNames.Select(existingInheritance.Get).ToArray();
            var dialogue=editor.Insert(null,true);dialogue.Text="destination {uketsugu} dialogue";
            var before=destination.Serialize();
            IReadOnlyList<AssStyle> copied;
            if(route=="clipboard")copied=editor.PasteStyles(new SubtitleEditor(source).CopyStyleText(source.Styles));
            else if(route=="library")
            {
                var libraryPath=Path.Combine(root,"library.json");var library=new StyleLibraryStore(libraryPath);var collection=library.Create("Mangetsu");
                library.Import(source,library.Editor(collection));library.Save();
                var restored=new StyleLibraryStore(libraryPath);var libraryDocument=restored.Editor(restored.Collections.Single()).Document;
                copied=editor.CopyStyles(libraryDocument.Styles,libraryDocument);
            }
            else copied=editor.CopyStyles(source.Styles,source);
            var mapped=Assert.Single(copied).Name;
            var expected=source.Events.Take(5).Select(line=>
            {
                var copy=line.Clone();copy.Style=mapped;
                if(copy.Actor=="mangetsu-colorcode-applied-styles")copy.Text="{"+mapped+"}";
                if(copy.Text.Contains(@"\rDefault",StringComparison.Ordinal))copy.Text=copy.Text.Replace(@"\rDefault",@"\r"+mapped,StringComparison.Ordinal);
                return copy;
            }).OrderBy(line=>MangetsuMetadata.Kind(line) switch { MangetsuMetadataKind.Style=>0,MangetsuMetadataKind.Actor=>1,_=>2 }).ToArray();
            var imported=destination.Events.Where(line=>!ReferenceEquals(line,dialogue)&&!line.Text.Contains("destination",StringComparison.Ordinal)&&line.Actor!="Existing").ToArray();
            Assert.Equal(expected.Length,imported.Length);
            for(var i=0;i<expected.Length;i++)
            {
                Assert.Equal(expected[i].Kind,imported[i].Kind);Assert.Equal(expected[i].FieldNames,imported[i].FieldNames);
                Assert.Equal(expected[i].FieldNames.Select(expected[i].Get),imported[i].FieldNames.Select(imported[i].Get));
            }
            Assert.Equal(new[] { MangetsuMetadataKind.Style,MangetsuMetadataKind.Style,MangetsuMetadataKind.Actor,MangetsuMetadataKind.Actor,MangetsuMetadataKind.Actor },MangetsuMetadata.Initial(destination).Select(MangetsuMetadata.Kind));
            Assert.Same(existingInheritance,destination.Events[5]);Assert.Equal(inheritanceFields,existingInheritance.FieldNames.Select(existingInheritance.Get));
            Assert.False(MangetsuMetadata.IsInherited(dialogue));Assert.Same(dialogue,destination.Events[^1]);Assert.Equal(original,source.Serialize());
            var after=destination.Serialize();var path=Path.Combine(root,"roundtrip.ass");destination.Save(path);Assert.Equal(after,AssDocument.Load(path).Serialize());
            editor.Undo.Undo();Assert.Equal(before,destination.Serialize());editor.Undo.Redo();Assert.Equal(after,destination.Serialize());
        }
        finally { Directory.Delete(root,true); }
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
    public void UnresolvableDefaultCaseMetadataCannotBeSilentlyDroppedDuringImport()
    {
        var source=AssDocument.CreateEmpty();source.Styles[0].Name="default";
        source=AssDocument.Parse(source.Serialize()+Event("Comment","default","","mangetsu-style",@"{\future(opaque)}"));
        var sourceBefore=source.Serialize();Assert.Throws<ArgumentException>(()=>new SubtitleEditor(source).SetMangetsuMetadata(MangetsuMetadataKind.Style,"default",@"{\vert1}"));Assert.Equal(sourceBefore,source.Serialize());
        var destination=AssDocument.CreateEmpty();var before=destination.Serialize();var editor=new SubtitleEditor(destination);
        Assert.Throws<InvalidOperationException>(()=>editor.CopyStyles(source.Styles,source));Assert.Equal(before,destination.Serialize());Assert.False(editor.Undo.CanUndo);
    }
    [Theory]
    [InlineData("import")]
    [InlineData("clipboard")]
    [InlineData("library")]
    public void StarredStyleAliasesCannotOverwriteDestinationOrLoseRendererAssets(string route)
    {
        var source=AssDocument.CreateEmpty();var sourceEditor=new SubtitleEditor(source);var fancy=source.Styles[0].Clone();fancy.Name="*Fancy";sourceEditor.AddStyle(fancy);
        source=AssDocument.Parse(source.Serialize()+
            Event("Comment","Fancy","","mangetsu-style",@"{\future(opaque)\rFancy}")+
            Event("Comment","Fancy","mangetsu-colorcode-applied-styles","mangetsu-colorcoding","{Fancy}")+
            Event("Comment","Fancy","Nene","mangetsu-colorcoding",@"{\1c&H112233&}")+
            Event("Comment","Fancy","","{uketsugu}",@"{\rFancy}日本語"));
        var original=source.Serialize();var destination=AssDocument.CreateEmpty();var editor=new SubtitleEditor(destination);
        var existing=destination.Styles[0].Clone();existing.Name="Fancy";editor.AddStyle(existing);
        editor.SetMangetsuMetadata(MangetsuMetadataKind.Style,"Fancy",@"{\future(destination)}");var before=destination.Serialize();
        var root=Path.Combine(Path.GetTempPath(),"Yoake-starred-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            IReadOnlyList<AssStyle> copies;
            if(route=="clipboard")copies=editor.PasteStyles(new SubtitleEditor(source).CopyStyleText(source.Styles.Where(s=>s.Name=="*Fancy")));
            else if(route=="library")
            {
                var path=Path.Combine(root,"library.json");var library=new StyleLibraryStore(path);var collection=library.Create("Assets");library.Import(source,library.Editor(collection));library.Save();
                var restored=new StyleLibraryStore(path);var stored=restored.Editor(restored.Collections.Single()).Document;
                copies=editor.CopyStyles(stored.Styles.Where(s=>s.Name=="*Fancy"),stored);
            }
            else copies=editor.CopyStyles(source.Styles.Where(s=>s.Name=="*Fancy"),source);
            Assert.Equal("*Fancy 1",Assert.Single(copies).Name);
            Assert.Equal(@"{\future(destination)}",destination.Events[0].Text);
            var metadata=destination.Events.Single(e=>e.Text.Contains(@"\future(opaque)",StringComparison.Ordinal));Assert.Equal("*Fancy 1",metadata.Style);Assert.Equal(@"{\future(opaque)\rFancy 1}",metadata.Text);
            Assert.Equal("{Fancy 1}",destination.Events.Single(e=>MangetsuMetadata.IsAppliedStylesTarget(e.Actor)).Text);
            var inherited=Assert.Single(destination.Events,MangetsuMetadata.IsInherited);Assert.Equal("*Fancy 1",inherited.Style);Assert.Equal(@"{\rFancy 1}日本語",inherited.Text);Assert.Equal("{uketsugu}",inherited.Effect);
            Assert.Equal(original,source.Serialize());var after=destination.Serialize();Assert.Equal(after,AssDocument.Parse(after).Serialize());editor.Undo.Undo();Assert.Equal(before,destination.Serialize());editor.Undo.Redo();Assert.Equal(after,destination.Serialize());
        }
        finally { Directory.Delete(root,true); }
    }
    [Fact]
    public void ReservedActorAliasesEditTheSameRendererSettingAndRemoveTogether()
    {
        var document=AssDocument.Parse(AssDocument.CreateEmpty().Serialize()+
            Event("Comment","Default","{mangetsu-colorcode-applied-styles}","mangetsu-colorcoding","{Default}"));
        var editor=new SubtitleEditor(document);var existing=Assert.Single(document.Events);var before=document.Serialize();
        editor.SetMangetsuMetadata(MangetsuMetadataKind.Actor,"mangetsu-colorcode-applied-styles","{Default}{Future}");
        Assert.Same(existing,Assert.Single(document.Events));Assert.Equal("{mangetsu-colorcode-applied-styles}",existing.Actor);Assert.Equal("{Default}{Future}",existing.Text);
        editor.Undo.Undo();Assert.Equal(before,document.Serialize());
        editor.RemoveMangetsuMetadata(MangetsuMetadataKind.Actor,"mangetsu-colorcode-applied-styles");Assert.Empty(document.Events);editor.Undo.Undo();Assert.Equal(before,document.Serialize());
        Assert.False(MangetsuMetadata.IsAppliedStylesTarget("{{mangetsu-colorcode-applied-styles}}"));Assert.False(MangetsuMetadata.IsAppliedStylesTarget("mangetsu-colorcode-applied-styles "));
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
