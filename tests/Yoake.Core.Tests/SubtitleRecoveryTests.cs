using System.Text;
using System.IO.Compression;
using Xunit;
using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class SubtitleRecoveryTests : IDisposable
{
    private readonly string _root=Path.Combine(Path.GetTempPath(),"Yoake-recovery-"+Guid.NewGuid());
    private const string Source="[Script Info]\r\nTitle: 日本語\n[X-Unknown]\rOpaque: raw\n[Fonts]\nfontname: fixture.ttf\n!!!!\n[Events]\r\nFormat: Layer,Start,End,Style,Name,MarginL,MarginR,MarginV,Effect,Text,Future\nDialogue: 0,0:00:00.00,0:00:02.00,Default,,01,02,03,,{\\distort(1,2,3,4,5,6,7,8)}原文,keep\r";
    private static RecoveryMetadata Metadata(Guid id,long sequence=1,RecoveryDraft? draft=null)=>new(){DocumentId=id,Sequence=sequence,Timestamp=DateTimeOffset.UtcNow,Title="same.ass",SelectedRow=0,Draft=draft};
    private static Task<string> Write(SubtitleRecoveryStore store,AssDocument document,RecoveryMetadata metadata,RecoveryLease? lease=null)
    {lease??=new();return store.WriteAsync(document.CaptureSnapshot(),metadata,lease,lease.Renew());}
    [Theory][InlineData(65001)][InlineData(1200)][InlineData(1201)][InlineData(12000)][InlineData(12001)]
    public async Task SnapshotRoundTripsEncodingBomUnknownSectionsTagsAndAttachments(int codePage)
    {
        var encoding=codePage switch{1200=>new UnicodeEncoding(false,true,true),1201=>new UnicodeEncoding(true,true,true),12000=>new UTF32Encoding(false,true,true),12001=>new UTF32Encoding(true,true,true),_=> (Encoding)new UTF8Encoding(true,true)};
        var bytes=encoding.GetPreamble().Concat(encoding.GetBytes(Source)).ToArray();var document=AssDocument.LoadBytes(bytes);
        var store=new SubtitleRecoveryStore(_root);var path=await Write(store,document,Metadata(Guid.NewGuid()));var recovered=await store.ReadAsync(path);
        Assert.Equal(Source,recovered.Document.Serialize());Assert.Equal(bytes,recovered.Document.CaptureSnapshot().SerializeBytes());
        Assert.Equal(0,document.Revision);Assert.Single(await store.ListAsync());Assert.Empty(Directory.GetFiles(_root,"*.tmp"));
    }
    [Fact] public async Task ImmutableCaptureCannotCombineLaterStylesAndTextWithEarlierRevision()
    {
        var document=AssDocument.Parse(Source);var snapshot=document.CaptureSnapshot();document.Events[0].Text="later";
        var store=new SubtitleRecoveryStore(_root);var lease=new RecoveryLease();var path=await store.WriteAsync(snapshot,Metadata(Guid.NewGuid()),lease,lease.Renew());
        var recovered=await store.ReadAsync(path);Assert.Equal(Source,recovered.Document.Serialize());Assert.Equal(snapshot.Revision,recovered.Metadata.Revision);
    }
    [Theory][InlineData("0:00:00.50","12")][InlineData("unfinished","-")]
    public async Task DraftIsSeparateFromValidAssAndRawNewlinesAndInvalidValuesSurvive(string start,string margin)
    {
        var document=AssDocument.Parse(Source);var values=document.Events[0].FieldNames.ToDictionary(n=>n,n=>document.Events[0].Get(n));
        values["Start"]=start;values["MarginL"]=margin;values["Text"]="未確定\r\nraw\ntext";
        var draft=new RecoveryDraft{Row=0,Values=values,OriginalText="原文",Caret=1,SelectionEnd=1};var store=new SubtitleRecoveryStore(_root);
        var path=await Write(store,document,Metadata(Guid.NewGuid(),draft:draft));var recovered=await store.ReadAsync(path);
        Assert.Equal(Source,recovered.Document.Serialize());Assert.Equal(start,recovered.Metadata.Draft!.Values["Start"]);Assert.Equal(margin,recovered.Metadata.Draft.Values["MarginL"]);Assert.Equal(values["Text"],recovered.Metadata.Draft.Values["Text"]);
    }
    [Fact] public async Task SameBasenameInDifferentDirectoriesAndNeverSavedIdentityDoNotCollide()
    {
        var store=new SubtitleRecoveryStore(_root);var document=AssDocument.Parse(Source);
        foreach(var original in new[]{Path.Combine(_root,"A","same.ass"),Path.Combine(_root,"B","same.ass"),null})
            await Write(store,document,Metadata(Guid.NewGuid()) with{OriginalPath=original});
        var entries=await store.ListAsync();Assert.Equal(3,entries.Length);Assert.Equal(3,entries.Select(e=>e.Group).Distinct().Count());
    }
    [Fact] public async Task ObsoleteCompletionAndCancellationKeepPreviousValidSnapshotAndCleanTemporaryFiles()
    {
        var store=new SubtitleRecoveryStore(_root);var document=AssDocument.Parse(Source);var metadata=Metadata(Guid.NewGuid());
        var first=await Write(store,document,metadata);var lease=new RecoveryLease();var stale=lease.Renew();lease.Invalidate();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>store.WriteAsync(document.CaptureSnapshot(),metadata with{Sequence=2},lease,stale));
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>store.WriteAsync(document.CaptureSnapshot(),metadata with{Sequence=3},lease,lease.Renew(),cancellation.Token));
        Assert.True(File.Exists(first));Assert.Single(await store.ListAsync());Assert.Empty(Directory.GetFiles(_root,"*.tmp"));
    }
    [Fact] public async Task StorageAndDirectoryFailuresDoNotDeleteLastValidVersion()
    {
        var document=AssDocument.Parse(Source);var metadata=Metadata(Guid.NewGuid());var store=new SubtitleRecoveryStore(_root);var first=await Write(store,document,metadata);
        var full=new SubtitleRecoveryStore(_root,storageLimit:1);await Assert.ThrowsAsync<IOException>(()=>Write(full,document,metadata with{Sequence=2}));
        Assert.Equal(Source,(await store.ReadAsync(first)).Document.Serialize());Assert.Empty(Directory.GetFiles(_root,"*.tmp"));
        var blocked=Path.Combine(_root,"not-directory");File.WriteAllText(blocked,"untouched");
        await Assert.ThrowsAsync<IOException>(()=>Write(new(blocked),document,metadata));Assert.Equal("untouched",File.ReadAllText(blocked));
    }
    [Fact] public async Task HistoryPrunesOnlyAfterPublicationAndGroupsSurviveIndependentStoreRestart()
    {
        var id=Guid.NewGuid();var document=AssDocument.Parse(Source);var store=new SubtitleRecoveryStore(_root,versionsPerDocument:3);
        for(var sequence=1;sequence<=8;sequence++)await Write(store,document,Metadata(id,sequence));
        var restarted=new SubtitleRecoveryStore(_root);var entries=await restarted.ListAsync();Assert.Equal(3,entries.Length);Assert.Equal(new long[]{8,7,6},entries.Select(e=>e.Metadata!.Sequence));
        foreach(var entry in entries)Assert.Equal(Source,(await restarted.ReadAsync(entry.Path)).Document.Serialize());
    }
    [Fact] public async Task CorruptBundlesRemainVisibleAndCannotHideValidVersionsOrEscapeStorage()
    {
        var store=new SubtitleRecoveryStore(_root);var valid=await Write(store,AssDocument.Parse(Source),Metadata(Guid.NewGuid()));
        File.WriteAllText(Path.Combine(_root,"corrupt.yoake-recovery"),"not a ZIP");File.WriteAllText(Path.Combine(_root,"interrupted.tmp"),"half written");
        var entries=await store.ListAsync();Assert.Equal(2,entries.Length);Assert.Single(entries.Where(e=>e.CanRecover));Assert.Single(entries.Where(e=>e.Error is not null));
        await Assert.ThrowsAsync<ArgumentException>(()=>store.ReadAsync(Path.Combine(_root,"..","other.yoake-recovery")));
        Assert.True(File.Exists(valid));await store.DiscardAsync(valid);Assert.Single(await store.ListAsync());
    }
    [Fact] public async Task InvalidDraftIdentityIsNeverPublished()
    {
        var store=new SubtitleRecoveryStore(_root);var document=AssDocument.Parse(Source);var metadata=Metadata(Guid.NewGuid(),draft:new(){Row=999});
        await Assert.ThrowsAsync<InvalidDataException>(()=>Write(store,document,metadata));Assert.Empty(await store.ListAsync());Assert.Empty(Directory.GetFiles(_root,"*.tmp"));
    }
    [Fact] public async Task MetadataTamperingIsRejectedEvenWhenZipAndCommittedAssAreValid()
    {
        var store=new SubtitleRecoveryStore(_root);var path=await Write(store,AssDocument.Parse(Source),Metadata(Guid.NewGuid()));
        using(var archive=ZipFile.Open(path,ZipArchiveMode.Update))
        {
            var old=archive.GetEntry("recovery.json")!;string json;using(var reader=new StreamReader(old.Open()))json=reader.ReadToEnd();old.Delete();
            using var writer=new StreamWriter(archive.CreateEntry("recovery.json").Open());writer.Write(json.Replace("same.ass","changed.ass"));
        }
        var entry=Assert.Single(await store.ListAsync());Assert.False(entry.CanRecover);Assert.Contains("checksum",entry.Error);
        await Assert.ThrowsAsync<InvalidDataException>(()=>store.ReadAsync(path));
    }
    [Fact] public async Task InterruptedTemporaryBundlesAreRemovedOnlyUnderPublicationLock()
    {
        Directory.CreateDirectory(_root);var abandoned=Path.Combine(_root,"abandoned.yoake-recovery.tmp");File.WriteAllText(abandoned,"interrupted");
        var store=new SubtitleRecoveryStore(_root);await Write(store,AssDocument.Parse(Source),Metadata(Guid.NewGuid()));Assert.False(File.Exists(abandoned));Assert.Single(await store.ListAsync());
    }
    [Fact] public async Task WindowsReadOnlyPublicationLockReportsPermissionFailureWithoutLosingRecovery()
    {
        if(!OperatingSystem.IsWindows())return;
        var store=new SubtitleRecoveryStore(_root);var metadata=Metadata(Guid.NewGuid());var path=await Write(store,AssDocument.Parse(Source),metadata);
        var lockPath=Path.Combine(_root,".publication-lock");File.SetAttributes(lockPath,FileAttributes.ReadOnly);
        try{await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Write(store,AssDocument.Parse(Source),metadata with{Sequence=2}));Assert.True(File.Exists(path));}
        finally{File.SetAttributes(lockPath,FileAttributes.Normal);}
    }
    public void Dispose(){if(Directory.Exists(_root))Directory.Delete(_root,true);}
}
