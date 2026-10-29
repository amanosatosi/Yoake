using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Yoake.Core.Fonts;
using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class FontCollectorTests:IDisposable
{
    private readonly string _root=Path.Combine(Path.GetTempPath(),"yoake-font-tests-"+Guid.NewGuid());
    public FontCollectorTests()=>Directory.CreateDirectory(_root);
    public void Dispose()=>Directory.Delete(_root,true);
    private static AssDocument Document(string text="日本語 မြန်မာ",string style="Default",bool comment=false)=>AssDocument.Parse(
        "[V4+ Styles]\nFormat: Name,Fontname,Bold,Italic\nStyle: Default,Family A,-1,0\nStyle: Other,Family B,0,-1\nStyle: Unused,Not Used,0,0\n"+
        "[Events]\nFormat: "+string.Join(',',AssDocument.EventFormat)+"\n"+(comment?"Comment: ":"Dialogue: ")+"0,0:00:01.00,0:00:02.00,"+style+",,0,0,0,,"+text+"\n[Future]\nkeep: raw\n");
    [Fact] public void StyleOnlyUnicodeRequirementsAreScalarSafeAndReadOnly()
    {var document=Document("日本語 မြန်မာ 👩‍👩‍👧 é");var source=document.Serialize();var requirements=FontUsageSnapshot.Capture(document).Scan();var font=Assert.Single(requirements.Fonts);Assert.Equal(new FontRequest("Family A",1,false),font.Request);Assert.Contains(0x1F469,font.Characters);Assert.Contains(0x0301,font.Characters);Assert.DoesNotContain(font.Characters,c=>c is >=0xD800 and <=0xDFFF);Assert.Equal(source,document.Serialize());}
    [Fact] public void InlineVariantsNamedResetsAndFnZeroUseVisibleSegments()
    {
        var requirements=FontUsageSnapshot.Capture(Document(@"a{\fnInline\b700\i1}b{\rOther}c{\fn0\b\i}d{\r}e")).Scan();
        Assert.Equal(3,requirements.Fonts.Length);Assert.Contains(requirements.Fonts,f=>f.Request==new FontRequest("Inline",700,true)&&f.Characters.SequenceEqual(new[]{(int)'b'}));
        Assert.Contains(requirements.Fonts,f=>f.Request==new FontRequest("Family B",0,true)&&f.Characters.SequenceEqual(new[]{(int)'c',(int)'d'}));
        Assert.Contains(requirements.Fonts,f=>f.Request==new FontRequest("Family A",1,false)&&f.Characters.SequenceEqual(new[]{(int)'a',(int)'e'}));
    }
    [Fact] public void CommentsDrawingsOverrideSyntaxAndTransformOnlyFontsAreExcluded()
    {Assert.Empty(FontUsageSnapshot.Capture(Document(@"{\fnIgnored}comment",comment:true)).Scan().Fonts);var result=FontUsageSnapshot.Capture(Document(@"{\p1\fnDrawing}m 0 0 l 20 10{\p0\r\t(0,10,\fnAnimated)}visible")).Scan();Assert.Equal("Family A",Assert.Single(result.Fonts).Request.Family);Assert.Empty(result.Warnings);}
    [Fact] public void EscapesAndFuriganaDoNotTurnMarkupIntoRequiredGlyphs()
    {var font=Assert.Single(FontUsageSnapshot.Capture(Document(@"<病|やまい>\N\n\h\{\|\" )).Scan().Fonts);Assert.Contains((int)'病',font.Characters);Assert.Contains((int)'や',font.Characters);Assert.Contains(0xA0,font.Characters);Assert.Contains((int)'{',font.Characters);Assert.Contains((int)'|',font.Characters);Assert.DoesNotContain((int)'<',font.Characters);Assert.DoesNotContain((int)'N',font.Characters);}
    [Fact] public void OptionalUnusedStyleCheckIsExplicitAndDuplicateRequestsMerge()
    {var document=Document("a{\\fnFamily A}b");Assert.Single(FontUsageSnapshot.Capture(document).Scan().Fonts);Assert.Equal(3,FontUsageSnapshot.Capture(document).Scan(true).Fonts.Length);}
    [Fact] public void UnknownStylesReportMeaningfullyAndNeverChangeSource()
    {var document=Document(style:"Absent");var source=document.Serialize();var requirements=FontUsageSnapshot.Capture(document).Scan();Assert.Empty(requirements.Fonts);Assert.Single(requirements.Warnings);Assert.Equal(source,document.Serialize());}
    [Fact] public void InvalidTimingDraftAndPhysicalNewlinesAreFontProjectionOnly()
    {var document=Document();var source=document.Serialize();var draft=new Dictionary<string,string>{{"Text","{\\fnDraft}a\nb"},{"Start","unfinished"}};var snapshot=FontUsageSnapshot.Capture(document,document.Events[0],draft);var font=Assert.Single(snapshot.Scan().Fonts);Assert.Equal("Draft",font.Request.Family);Assert.Equal(new[]{(int)'a',(int)'b'},font.Characters);Assert.Equal(source,document.Serialize());Assert.Equal("unfinished",draft["Start"]);}
    [Fact] public void CapturedFontRequirementsAreImmutableAndForeignDraftsRejected()
    {var document=Document("a");var snapshot=FontUsageSnapshot.Capture(document);document.Events[0].Text="{\\fnChanged}b";Assert.Equal("Family A",Assert.Single(snapshot.Scan().Fonts).Request.Family);Assert.Throws<InvalidOperationException>(()=>FontUsageSnapshot.Capture(document,Document().Events[0]));}
    [Fact] public void ScanCancellationAndNativeResolverOwnershipAreExplicit()
    {var factory=new FakeFactory();var snapshot=FontUsageSnapshot.Capture(Document());FontCollection.Scan(snapshot,false,factory);Assert.Equal(1,factory.Disposals);using var canceled=new CancellationTokenSource();canceled.Cancel();Assert.Throws<OperationCanceledException>(()=>snapshot.Scan(token:canceled.Token));}
    [Fact] public void MissingAndSubstitutedRequestsCannotBecomeCopiedFallbacks()
    {var factory=new FakeFactory{Result=u=>new(u,FontMatch.Substituted,"Fallback","Regular",0,false,false,[],[],["Not the requested font"])};var report=FontCollection.Scan(FontUsageSnapshot.Capture(Document()),false,factory);Assert.Equal(1,report.MissingFonts);Assert.False(report.Complete);Assert.Empty(Assert.Single(report.Fonts).Files);}
    [Theory] [InlineData(0,false)] [InlineData(2,true)] [InlineData(4,false)] [InlineData(8,false)] [InlineData(512,true)]
    public void EmbeddingMetadataIsReadFromActualTableBits(int flags,bool restricted)
    {using var stream=new MemoryStream(FontBytes((ushort)flags));var metadata=OpenTypeMetadata.Read(stream);Assert.Equal((ushort)flags,Assert.Single(metadata.Faces).EmbeddingFlags);Assert.Equal(restricted,metadata.CopyRestricted);}
    [Fact] public void UnicodeNamesAndCffHeaderUseBoundedOpenTypeMetadata()
    {var bytes=FontBytes(0,true);using var stream=new MemoryStream(bytes);var metadata=OpenTypeMetadata.Read(stream);Assert.Equal("日本語 Font",Assert.Single(Assert.Single(metadata.Faces).Families));}
    [Fact] public void CollectionMetadataUsesAbsoluteOffsetsAndAllFaceRestrictions()
    {var a=FontBytes(0);var b=FontBytes(2);var bytes=Collection(a,b);using var stream=new MemoryStream(bytes);var metadata=OpenTypeMetadata.Read(stream);Assert.Equal(2,metadata.Faces.Length);Assert.Equal(1,metadata.Faces[1].Index);Assert.True(metadata.CopyRestricted);}
    [Theory] [InlineData(0)] [InlineData(11)] [InlineData(30)]
    public void TruncatedFontMetadataFailsWithoutUnboundedReads(int bytes)=>Assert.Throws<InvalidDataException>(()=>OpenTypeMetadata.Read(new MemoryStream(FontBytes(0)[..bytes])));
    [Fact] public void MalformedCollectionAndNameOffsetsAreRejected()
    {var bytes=Collection(FontBytes(0),FontBytes(0));BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8),uint.MaxValue);Assert.Throws<InvalidDataException>(()=>OpenTypeMetadata.Read(new MemoryStream(bytes)));Assert.Throws<InvalidDataException>(()=>OpenTypeMetadata.ReadNames(new byte[]{0,0,0,1,255,255}));}
    [Fact] public async Task FolderCollectionDeduplicatesFilesAndNeverOverwritesExistingBatches()
    {
        var a=File("a/same.ttf",FontBytes(0));var b=File("b/same.ttf",FontBytes(0).Concat(new byte[]{1}).ToArray());var report=Report(a,a,b);var lease=new RecoveryLease();var first=await FontCollection.WriteAsync(report,FontCollectionMode.Folder,_root,true,lease,lease.Renew());
        Assert.Equal(2,first.Files);var paths=Directory.GetFiles(first.Path,"*.ttf");Assert.Equal(2,paths.Length);Assert.Equal(2,paths.Select(Path.GetFileName).Distinct(StringComparer.OrdinalIgnoreCase).Count());Assert.Contains(paths,p=>System.IO.File.ReadAllBytes(p).SequenceEqual(System.IO.File.ReadAllBytes(a.Path)));
        var second=await FontCollection.WriteAsync(report,FontCollectionMode.ScriptFolder,_root,true,lease,lease.Renew());Assert.NotEqual(first.Path,second.Path);Assert.True(Directory.Exists(first.Path));Assert.Empty(Directory.GetDirectories(_root,".yoake-fonts-*.tmp"));
    }
    [Fact] public async Task IdenticalBytesAtDifferentPhysicalPathsAreDeduplicated()
    {var bytes=FontBytes(0);var lease=new RecoveryLease();var result=await FontCollection.WriteAsync(Report(File("a/a.ttf",bytes),File("b/b.ttf",bytes)),FontCollectionMode.Folder,_root,true,lease,lease.Renew());Assert.Equal(1,result.Files);}
    [Fact] public async Task ZipCollectionHasExactUnicodeFontBytesAndAReadableRightsNotice()
    {
        var file=File("source/日本語.ttf",FontBytes(0));var destination=Path.Combine(_root,"日本語 archive.zip");System.IO.File.WriteAllText(destination,"user data");var lease=new RecoveryLease();
        var result=await FontCollection.WriteAsync(Report(file),FontCollectionMode.Zip,destination,true,lease,lease.Renew());Assert.NotEqual(destination,result.Path);Assert.Equal("user data",System.IO.File.ReadAllText(destination));
        using var archive=ZipFile.OpenRead(result.Path);using var data=archive.GetEntry("日本語.ttf")!.Open();using var memory=new MemoryStream();await data.CopyToAsync(memory);Assert.Equal(System.IO.File.ReadAllBytes(file.Path),memory.ToArray());Assert.NotNull(archive.GetEntry("FONT-LICENSE-NOTICE.txt"));
    }
    [Fact] public async Task MissingRestrictedAndUnacknowledgedFontsDoNotCreateOutput()
    {
        var lease=new RecoveryLease();var restricted=Report(File("source/restricted.ttf",FontBytes(2)));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>FontCollection.WriteAsync(restricted,FontCollectionMode.Folder,_root,true,lease,lease.Renew()));
        var allowed=Report(File("source/allowed.ttf",FontBytes(0)));await Assert.ThrowsAsync<InvalidOperationException>(()=>FontCollection.WriteAsync(allowed,FontCollectionMode.Folder,_root,false,lease,lease.Renew()));Assert.False(Directory.Exists(Path.Combine(_root,"fonts")));
    }
    [Fact] public async Task CanceledAndObsoletePublicationCleansAllTemporaryOutput()
    {
        var report=Report(File("source/font.ttf",FontBytes(0)));var lease=new RecoveryLease();var old=lease.Renew();lease.Invalidate();await Assert.ThrowsAsync<OperationCanceledException>(()=>FontCollection.WriteAsync(report,FontCollectionMode.Folder,_root,true,lease,old));
        using var canceled=new CancellationTokenSource();canceled.Cancel();await Assert.ThrowsAsync<OperationCanceledException>(()=>FontCollection.WriteAsync(report,FontCollectionMode.Zip,Path.Combine(_root,"fonts.zip"),true,lease,lease.Renew(),token:canceled.Token));Assert.Empty(Directory.GetDirectories(_root,".yoake-fonts-*.tmp"));Assert.False(Directory.Exists(Path.Combine(_root,"fonts")));Assert.False(System.IO.File.Exists(Path.Combine(_root,"fonts.zip")));
    }
    [Fact] public async Task ChangedOrLockedSourceAndInvalidDestinationLeaveNoHalfBatch()
    {
        var file=File("source/font.ttf",FontBytes(0));var report=Report(file);var lease=new RecoveryLease();System.IO.File.AppendAllText(file.Path,"changed");
        await Assert.ThrowsAsync<IOException>(()=>FontCollection.WriteAsync(report,FontCollectionMode.Folder,_root,true,lease,lease.Renew()));
        var blocker=Path.Combine(_root,"blocked");System.IO.File.WriteAllText(blocker,"user");await Assert.ThrowsAnyAsync<IOException>(()=>FontCollection.WriteAsync(report,FontCollectionMode.Folder,blocker,true,lease,lease.Renew()));Assert.Equal("user",System.IO.File.ReadAllText(blocker));Assert.Empty(Directory.GetDirectories(_root,".yoake-fonts-*.tmp"));
    }
    [Fact] public async Task CancelDuringStagingPreservesExistingUserFiles()
    {var file=File("source/font.ttf",FontBytes(0));var lease=new RecoveryLease();using var cancel=new CancellationTokenSource();var progress=new ImmediateProgress(p=>{if(p.Message.StartsWith("Prepared",StringComparison.Ordinal))cancel.Cancel();});await Assert.ThrowsAsync<OperationCanceledException>(()=>FontCollection.WriteAsync(Report(file),FontCollectionMode.Folder,_root,true,lease,lease.Renew(),progress,cancel.Token));Assert.True(System.IO.File.Exists(file.Path));Assert.False(Directory.Exists(Path.Combine(_root,"fonts")));Assert.Empty(Directory.GetDirectories(_root,".yoake-fonts-*.tmp"));}
    [Fact] public async Task LockedFontFileFailsWithoutPublishingOrChangingTheSource()
    {var file=File("source/font.ttf",FontBytes(0));var original=System.IO.File.ReadAllBytes(file.Path);var lease=new RecoveryLease();using(var locked=new FileStream(file.Path,FileMode.Open,FileAccess.ReadWrite,FileShare.None))await Assert.ThrowsAnyAsync<IOException>(()=>FontCollection.WriteAsync(Report(file),FontCollectionMode.Folder,_root,true,lease,lease.Renew()));Assert.Equal(original,System.IO.File.ReadAllBytes(file.Path));Assert.False(Directory.Exists(Path.Combine(_root,"fonts")));Assert.Empty(Directory.GetDirectories(_root,".yoake-fonts-*.tmp"));}
    [Fact] public void ResamplingDoesNotChangeTheCollectorFontRequirements()
    {var editor=new SubtitleEditor(Document(@"{\pos(20,30)\fnA\distort(0,0,1,0,1,1,0,1)}日本語{\rOther}မြန်မာ"));var before=FontUsageSnapshot.Capture(editor.Document).Scan();editor.ApplyResample(editor.CaptureResample().Calculate(new(640,480,1280,960)));var after=FontUsageSnapshot.Capture(editor.Document).Scan();Assert.Equal(before.Fonts.Select(f=>f.Request),after.Fonts.Select(f=>f.Request));Assert.Equal(before.Fonts.SelectMany(f=>f.Characters),after.Fonts.SelectMany(f=>f.Characters));}
    private ResolvedFontFile File(string name,byte[] bytes)
    {var path=Path.Combine(_root,name);Directory.CreateDirectory(Path.GetDirectoryName(path)!);System.IO.File.WriteAllBytes(path,bytes);return ResolvedFontFile.Inspect(path);}
    private static FontCollectionReport Report(params ResolvedFontFile[] files)=>new([new(new(new("Test",0,false),[(int)'a'],["Default"],[1]),FontMatch.Exact,"Test","Regular",0,false,false,[],files,[])],[]);
    private sealed class ImmediateProgress(Action<FontProgress> callback):IProgress<FontProgress>{public void Report(FontProgress value)=>callback(value);}
    private sealed class FakeFactory:IFontResolverFactory
    {
        public int Disposals;public Func<FontUsage,ResolvedFont>? Result;
        public IFontFileResolver Create()=>new Resolver(this);
        private sealed class Resolver(FakeFactory owner):IFontFileResolver
        {public ResolvedFont Resolve(FontUsage usage,CancellationToken token)=>owner.Result?.Invoke(usage)??new(usage,FontMatch.Missing,"","",0,false,false,[],[],["Controlled missing font"]);public void Dispose()=>owner.Disposals++;}
    }
    internal static byte[] FontBytes(ushort flags,bool cff=false)
    {
        var name=Encoding.BigEndianUnicode.GetBytes("日本語 Font");var bytes=new byte[80+name.Length];BinaryPrimitives.WriteUInt32BigEndian(bytes,cff?0x4F54544Fu:0x00010000u);BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4),2);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(12),0x4F532F32);BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20),44);BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(24),10);BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(52),flags);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(28),0x6E616D65);BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(36),54);BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(40),(uint)(18+name.Length));BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(56),1);BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(58),18);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(60),3);BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(62),1);BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(64),0x409);BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(66),1);BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(68),(ushort)name.Length);name.CopyTo(bytes,72);return bytes;
    }
    private static byte[] Collection(params byte[][] fonts)
    {
        var bytes=new byte[12+fonts.Length*4+fonts.Sum(f=>f.Length)];BinaryPrimitives.WriteUInt32BigEndian(bytes,0x74746366);BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4),0x10000);BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8),(uint)fonts.Length);var offset=12+fonts.Length*4;
        for(var i=0;i<fonts.Length;i++){BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(12+i*4),(uint)offset);fonts[i].CopyTo(bytes,offset);var count=BinaryPrimitives.ReadUInt16BigEndian(fonts[i].AsSpan(4));for(var j=0;j<count;j++){var field=offset+12+j*16+8;BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(field),BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(field))+(uint)offset);}offset+=fonts[i].Length;}return bytes;
    }
}
