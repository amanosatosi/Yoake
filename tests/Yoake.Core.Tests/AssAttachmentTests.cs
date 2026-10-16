using System.Text;
using Xunit;
using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class AssAttachmentTests : IDisposable
{
    private readonly string _root=Path.Combine(Path.GetTempPath(),"Yoake-attachments-"+Guid.NewGuid());
    [Theory][InlineData("x","?!")][InlineData("xy","?(E")][InlineData("xyz","?(F[")]
    public void ExactAegisubShortFixtures(string text,string encoded)
    {var bytes=Encoding.ASCII.GetBytes(text);Assert.Equal(new[]{encoded},AssAttachmentCodec.Encode(bytes));Assert.Equal(bytes,AssAttachmentCodec.Decode([encoded]));}
    [Theory][InlineData(0)][InlineData(1)][InlineData(2)][InlineData(3)][InlineData(59)][InlineData(60)][InlineData(61)][InlineData(257)][InlineData(65537)]
    public void BinaryRoundTripsAndWrapsAtEightyIncludingPartialGroups(int length)
    {
        var bytes=Enumerable.Range(0,length).Select(i=>(byte)(i*73+19)).ToArray();var lines=AssAttachmentCodec.Encode(bytes);
        Assert.All(lines.Take(Math.Max(0,lines.Length-1)),line=>Assert.Equal(80,line.Length));Assert.All(lines,line=>Assert.InRange(line.Length,1,80));
        Assert.Equal(bytes.Length,AssAttachmentCodec.Inspect(lines));Assert.Equal(bytes,AssAttachmentCodec.Decode(lines));
    }
    [Theory][InlineData("!")][InlineData("!\"")][InlineData("!!\"")][InlineData("text with spaces")][InlineData("!!!!é")]
    public void MalformedEncodingIsRejected(string encoded)=>Assert.Throws<InvalidDataException>(()=>AssAttachmentCodec.Decode([encoded]));
    [Theory][InlineData("../x.ttf")][InlineData("C:\\x.ttf")][InlineData("/x.ttf")][InlineData("x/y")][InlineData("x\\y")][InlineData("x:y")]
    [InlineData("CON")][InlineData("con.ttf")][InlineData("COM1.ttf")][InlineData("lpt9.png")][InlineData("CLOCK$")][InlineData("NUL.bmp")]
    [InlineData("trailing.")][InlineData("trailing ")][InlineData(".")][InlineData("..")][InlineData("bad\nname")][InlineData("COM¹.ttf")][InlineData("CONOUT$")]
    public void UnsafeWindowsFilenameRejected(string name)=>Assert.False(AttachmentFileNames.IsSafe(name));
    [Theory][InlineData("日本語.ttf")][InlineData("မြန်မာ.png")][InlineData("normal font.otf")][InlineData("my_font.ttf")]
    public void UnicodeSafeNamesRemainUsable(string name)=>Assert.True(AttachmentFileNames.IsSafe(name));
    [Fact] public void EncodedBracketLineCannotBecomeSectionAndUnknownSectionsRoundTripExactly()
    {
        var source="[Script Info]\nPlayResX: 123\r\n[Fonts]\r\nfontname: bracket.ttf\r\n[EVENTS]\r\n[Unknown]\nOpaque: untouched\n[Events]\nFormat: "+string.Join(',',AssDocument.EventFormat)+"\nDialogue: 0,0:00:00.00,0:00:02.00,Default,,0,0,0,,{\\future(x)\\distort(1,2,3,4,5,6,7,8)}日本語\r";
        var document=AssDocument.Parse(source);Assert.Equal(source,document.Serialize());Assert.Single(document.Events);Assert.Equal("123",document.GetScriptInfo("PlayResX"));
        var attachment=Assert.Single(document.GetAttachments());Assert.Equal(6,attachment.DecodedSize);Assert.Equal(AssAttachmentCodec.Decode(["[EVENTS]"]),attachment.Decode());
        var editor=new SubtitleEditor(document);editor.SetField(document.Events[0],"Text","changed","Change text");editor.Undo.Undo();Assert.Equal(source,document.Serialize());
    }
    [Fact] public void MalformedAndEmptyPayloadsPreserveTheRestOfTheSource()
    {
        const string source="[Fonts]\nfontname: malformed.ttf\nnot encoded data\n[Opaque]\nKeep: exactly\n[Graphics]\nfilename: empty.bmp\n\n";
        var document=AssDocument.Parse(source);var entries=document.GetAttachments();Assert.Equal(2,entries.Length);Assert.NotNull(entries[0].Error);Assert.False(entries[0].CanExtract);Assert.Equal(0,entries[1].DecodedSize);
        Assert.Equal(source,document.Serialize());Assert.Equal("exactly",document.GetSectionValue("[Opaque]","Keep"));
        var editor=new SubtitleEditor(document);editor.RemoveAttachments([entries[0]]);Assert.Contains("not encoded data\n[Opaque]\nKeep: exactly",document.Serialize());editor.Undo.Undo();Assert.Equal(source,document.Serialize());
    }
    [Fact] public void ImportRemoveAndUndoShareUnchangedPayloadAndPreserveExactSourceAndSelectionObjects()
    {
        var document=AssDocument.CreateEmpty();var editor=new SubtitleEditor(document);var line=editor.Insert(null,true);editor.SetField(line,"Text","{\\future(x)}原文","Text");editor.MarkSaved();var original=document.Serialize();
        var bytes=Enumerable.Range(0,257).Select(i=>(byte)i).ToArray();editor.ImportAttachments([PreparedAssAttachment.Create(AssAttachmentKind.Font,"日本語.ttf",bytes)],document.Revision);
        Assert.True(editor.IsDirty);var imported=document.Serialize();var attachment=Assert.Single(document.GetAttachments());Assert.Equal("日本語_0.ttf",attachment.Name);Assert.Equal("日本語.ttf",attachment.ExtractionName);Assert.Equal(bytes,attachment.Decode());
        var otherSnapshot=document.CaptureSnapshot().GetAttachments();Assert.True(otherSnapshot[0].BelongsTo(document));
        editor.RemoveAttachments([attachment,otherSnapshot[0]]);Assert.Empty(document.GetAttachments());Assert.Same(line,document.Events[0]);
        Assert.True(editor.Undo.Undo());Assert.Equal(imported,document.Serialize());Assert.True(editor.Undo.Redo());Assert.Empty(document.GetAttachments());
        editor.Undo.Undo();editor.Undo.Undo();Assert.Equal(original,document.Serialize());Assert.False(editor.IsDirty);Assert.Same(line,document.Events[0]);
    }
    [Fact] public void DuplicateImportForeignSelectionAndObsoleteRevisionAreRejectedBeforeMutation()
    {
        var document=AssDocument.CreateEmpty();var editor=new SubtitleEditor(document);var prepared=PreparedAssAttachment.Create(AssAttachmentKind.Graphic,"same.bmp",[1,2]);
        editor.ImportAttachments([prepared],document.Revision);var source=document.Serialize();
        Assert.Throws<InvalidDataException>(()=>editor.ImportAttachments([PreparedAssAttachment.Create(AssAttachmentKind.Graphic,"SAME.BMP",[3])],document.Revision));Assert.Equal(source,document.Serialize());
        var foreign=AssDocument.Parse("[Graphics]\nfilename: same.bmp\n!!!!\n").GetAttachments();Assert.Throws<InvalidOperationException>(()=>editor.RemoveAttachments(foreign));
        var stale=document.GetAttachments();editor.SetScriptInfo(new Dictionary<string,string>{{"Title","change"}});
        Assert.Throws<InvalidOperationException>(()=>editor.RemoveAttachments(stale));
    }
    [Fact] public async Task AtomicExtractionKeepsPreviousFileOnCancellationStaleLeaseAndNoOverwrite()
    {
        Directory.CreateDirectory(_root);var attachment=AssDocument.Parse("[Graphics]\nfilename: 日本語.bmp\n?(F[\n").GetAttachments()[0];var path=Path.Combine(_root,attachment.ExtractionName);
        File.WriteAllBytes(path,[9,8,7]);var lease=new RecoveryLease();await Assert.ThrowsAsync<IOException>(()=>AssAttachmentExtraction.WriteAsync(attachment,_root,false,lease,lease.Renew()));Assert.Equal(new byte[]{9,8,7},File.ReadAllBytes(path));
        var stale=lease.Renew();lease.Invalidate();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>AssAttachmentExtraction.WriteAsync(attachment,_root,true,lease,stale));
        using var cancel=new CancellationTokenSource();cancel.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>AssAttachmentExtraction.WriteAsync(attachment,_root,true,lease,lease.Renew(),cancel.Token));
        Assert.Equal(new byte[]{9,8,7},File.ReadAllBytes(path));Assert.Empty(Directory.GetFiles(_root,"*.tmp"));
        await AssAttachmentExtraction.WriteAsync(attachment,_root,true,lease,lease.Renew());Assert.Equal(Encoding.ASCII.GetBytes("xyz"),File.ReadAllBytes(path));
    }
    [Fact] public async Task ImportedAttachmentAndRemoveUndoSurviveEncodedRecoveryBundle()
    {
        var document=AssDocument.CreateEmpty();var editor=new SubtitleEditor(document);var bytes=new byte[]{0,255,128,12,17};
        editor.ImportAttachments([PreparedAssAttachment.Create(AssAttachmentKind.Graphic,"fixture.bmp",bytes)],document.Revision);var source=document.Serialize();
        editor.RemoveAttachments(document.GetAttachments());editor.Undo.Undo();Assert.Equal(source,document.Serialize());
        var store=new SubtitleRecoveryStore(_root);var lease=new RecoveryLease();var path=await store.WriteAsync(document.CaptureSnapshot(),new(){DocumentId=Guid.NewGuid(),Sequence=1,Title="Unsaved",Timestamp=DateTimeOffset.UtcNow},lease,lease.Renew());
        var recovered=await store.ReadAsync(path);Assert.Equal(source,recovered.Document.Serialize());Assert.Equal(bytes,Assert.Single(recovered.Document.GetAttachments()).Decode());
    }
    [Fact] public void PrecancelledImportAndDecodeLeaveSourceAndHistoryUnchanged()
    {
        var document=AssDocument.CreateEmpty();var editor=new SubtitleEditor(document);var source=document.Serialize();using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(()=>editor.ImportAttachments([PreparedAssAttachment.Create(AssAttachmentKind.Graphic,"x.bmp",[1])],document.Revision,cancellation.Token));
        Assert.Equal(source,document.Serialize());Assert.False(editor.Undo.CanUndo);Assert.ThrowsAny<OperationCanceledException>(()=>AssAttachmentCodec.Decode(["!!!!"],cancellation.Token));
    }
    public void Dispose(){if(Directory.Exists(_root))Directory.Delete(_root,true);}
}
