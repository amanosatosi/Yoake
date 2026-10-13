using System.Text;
using Xunit;
using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class ToshikiTimingTests
{
    [Theory]
    [InlineData("さまよう", "さ/ま/よう")]
    [InlineData("いっせーのーで", "いっ/せー/のー/で")]
    [InlineData("キャット", "キャッ/ト")]
    [InlineData("「スーパー」", "「スー/パー」")]
    [InlineData("(まちょっと)", "(ま/ちょっ/と)")]
    [InlineData("(心##焦がす)", "(心/#/#/焦/が/す)")]
    [InlineData("あ )", "あ/ /)")]
    [InlineData("自分の価値に目を疑って", "自/分/の/価/値/に/目/を/疑っ/て")]
    [InlineData("かな romaji", "か/な/ /romaji")]
    [InlineData("か\u3099き", "か\u3099/き")]
    public void SongSaneKanaCutUsesReferenceGroupsWithoutInventingTime(string text,string expected)
    {
        var plan=new ToshikiTimingPlan(text,1000,2000);
        Assert.Equal(expected,string.Join('/',plan.Slots.Select(s=>s.Text)));
        Assert.Empty(plan.Boundaries);Assert.False(plan.Slots[0].Assigned);
        Assert.All(plan.Slots.Skip(1),s=>Assert.Equal(s.Start,s.End));
    }
    [Fact] public void ManualCutsCanSeparateHeldMoraAndJoinSeveralMoras()
    {
        var plan=new ToshikiTimingPlan("さまよう",0,500);plan.Split(2,3);
        Assert.Equal(new[]{"さ","ま","よ","う"},plan.Slots.Select(s=>s.Text));
        plan.Join(3);plan.Join(1);Assert.Equal(new[]{"さま","よう"},plan.Slots.Select(s=>s.Text));Assert.Empty(plan.Boundaries);
    }
    [Fact] public void ExplicitBoundariesRoundAndDoNotAppendAnEarlierClick()
    {
        var plan=new ToshikiTimingPlan("abc",0,500,false);plan.Split(0,1);plan.Split(1,2);
        Assert.True(plan.Append(203));Assert.False(plan.Append(120));Assert.True(plan.Append(200));
        Assert.Equal(new long[]{200,200},plan.Boundaries);Assert.Equal("{\\k20}a{\\k0}b{\\k30}c",plan.PreviewText);
        plan.MoveBoundary(0,400);Assert.Equal(new long[]{200,200},plan.Boundaries);
        plan.MoveBoundary(1,350);plan.MoveBoundary(0,400);Assert.Equal(new long[]{350,350},plan.Boundaries);
    }
    [Fact] public void PartialCommitGivesOnePendingSlotRemainderAndLaterSlotsZero()
    {
        var plan=new ToshikiTimingPlan("あいう",0,500);Assert.True(plan.Append(120));
        Assert.Equal("{\\k12}あ{\\k38}い{\\k0}う",plan.PreviewText);
        var reopened=new ToshikiTimingPlan(plan.PreviewText,0,500);Assert.Equal(plan.PreviewText,reopened.PreviewText);
        Assert.Equal(new long[]{120,500},reopened.Boundaries);
    }
    [Fact] public void RecutKeepsOldEndAndRetimesOnlyTheFreshLeftPart()
    {
        var plan=new ToshikiTimingPlan("{\\k10}ab{\\k10}c",100,300);plan.Split(0,1);
        Assert.Equal(new long[]{200,200},plan.Boundaries);Assert.Equal(0,plan.RecutAt(150));Assert.Equal(-1,plan.RecutAt(250));
        plan.MoveBoundary(0,150);Assert.Equal(new long[]{150,200},plan.Boundaries);Assert.Equal("{\\k5}a{\\k5}b{\\k10}c",plan.PreviewText);
    }
    [Theory][InlineData("k")][InlineData("K")][InlineData("kf")][InlineData("ko")][InlineData("kO")]
    public void TagsAndPerGroupChoicePreserveZeroAndEmptyGroups(string tag)
    {
        var plan=new ToshikiTimingPlan("{\\k20}a{\\k0}{\\k30}b",0,500);Assert.Equal(3,plan.Count);Assert.Equal("",plan.Slots[1].Text);
        plan.SetTag(tag);Assert.Equal($"{{\\{tag}20}}a{{\\{tag}0}}{{\\{tag}30}}b",plan.PreviewText);
        plan.Select(1);plan.SetTag("ko",true);Assert.Equal("ko",plan.Slots[1].Tag);Assert.Equal(tag,plan.Slots[2].Tag);
    }
    [Fact] public void EmptyAndSingleUntimedLineDoNotAcquireInventedKaraoke()
    {var plan=new ToshikiTimingPlan("romaji",0,500);Assert.Equal("romaji",plan.PreviewText);Assert.Equal("",new ToshikiTimingPlan("",0,0).PreviewText);}
    [Theory]
    [InlineData("<病|やまい>","やまい","<病|{\\k30}や{\\k26}ま{\\k10}い>")]
    [InlineData("{\\i1}<病|{\\b1}やまい{\\b0}>{\\i0}","やまい","{\\i1}<病|{\\k30}{\\b1}や{\\k26}ま{\\k10}い{\\b0}>{\\i0}")]
    [InlineData("<{\\k99}病|やまい>","やまい","<{\\k99}病|{\\k30}や{\\k26}ま{\\k10}い>")]
    public void RendererFuriganaMapsReadingAndKeepsBaseOverridesOpaque(string source,string text,string output)
    {var plan=new ToshikiTimingPlan(source,0,660);Assert.Equal(text,plan.Text);Assert.True(plan.Append(300));Assert.True(plan.Append(560));Assert.Equal(output,plan.PreviewText);}
    [Fact] public void MixedRubyGroupsPreserveIndependentInsertionPoints()
    {
        var plan=new ToshikiTimingPlan("君は<病|やまい>じゃない",0,600,false);plan.Split(0,2);plan.Split(1,5);plan.Append(100);plan.Append(400);
        Assert.Equal("{\\k10}君は<病|{\\k30}やまい>{\\k20}じゃない",plan.PreviewText);
    }
    [Fact] public void GraphemesEscapesDrawingsNestedTransformsAndUnknownTagsSurvive()
    {
        const string source="{\\future(1,2)\\t(0,100,\\k99\\fs30)\\p1}m 0 0 l 5 5{\\p0}é👩‍👩‍👧\\|\\မြန်မာ";
        var plan=new ToshikiTimingPlan(source,0,500,false);Assert.DoesNotContain("m 0",plan.Text);Assert.False(plan.SafeCuts().Contains(1));
        Assert.False(plan.SafeCuts().Contains(3));plan.Split(0,2);Assert.Equal(2,plan.Count);
        Assert.Contains("\\t(0,100,\\k99\\fs30)",plan.PreviewText);Assert.Contains("\\future(1,2)",plan.PreviewText);Assert.Contains("m 0 0 l 5 5",plan.PreviewText);Assert.Contains("\\|\\",plan.PreviewText);
    }
    [Theory][InlineData("<病|や|まい>")][InlineData("<病<患|やまい>")][InlineData("<病|>")]
    public void AmbiguousRubyRemainsAnAtomicRawGroup(string source)
    {var plan=new ToshikiTimingPlan(source,0,500);Assert.Equal(source,plan.Text);Assert.Equal(source,plan.PreviewText);}
    [Theory][InlineData("{\\kt10}text")][InlineData("{\\k?}text")][InlineData("{\\k-1}text")]
    public void UnsupportedOrInvalidTimingCannotMutateSource(string source)=>Assert.Throws<ArgumentException>(()=>new ToshikiTimingPlan(source,0,500));
    [Fact] public void OneTransactionIncludesDraftMetadataTimingTextAndExactSourceUndo()
    {
        var doc=AssDocument.Parse("[Events]\nFormat: Future,Text,End,Start,Layer,Style,Name,MarginL,MarginR,MarginV,Effect\nDialogue: keep,{\\future(x)}あい,0:00:01.00,0:00:00.00,0,Default,,01,02,03,fx\n[X]\nOpaque: value\n");
        var editor=new SubtitleEditor(doc);var before=doc.Serialize();var line=doc.Events[0];var plan=new ToshikiTimingPlan(line.Text,0,1000);plan.Append(400);
        editor.CommitKaraoke(line,new Dictionary<string,string>{{"Text",plan.PreviewText},{"Name","日本語"},{"Start","0:00:00.00"},{"End","0:00:01.00"}});
        var after=doc.Serialize();Assert.True(editor.IsDirty);Assert.Equal("Toshiki K-Timing",editor.Undo.NextUndoName);Assert.Equal("keep",line.Get("Future"));
        editor.Undo.Undo();Assert.Equal(before,doc.Serialize());Assert.False(editor.Undo.CanUndo);editor.Undo.Redo();Assert.Equal(after,doc.Serialize());
        using var cts=new CancellationTokenSource();cts.Cancel();Assert.Throws<OperationCanceledException>(()=>editor.CommitKaraoke(line,new Dictionary<string,string>{{"Text","bad"}},cts.Token));Assert.Equal(after,doc.Serialize());
    }
    [Fact] public async Task RecoveryRoundTripsUncommittedSlotsRecutsAndRawInvalidDraftAlongsideAttachments()
    {
        var folder=Path.Combine(Path.GetTempPath(),"Yoake-karaoke-"+Guid.NewGuid());
        try
        {
            var doc=AssDocument.Parse("[Graphics]\nfilename: fixture.bmp\n?(F[\n\n[Events]\nFormat: "+string.Join(',',AssDocument.EventFormat)+"\nDialogue: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,,<病|やまい>\n");
            var plan=new ToshikiTimingPlan(doc.Events[0].Text,0,1000);plan.Append(300);plan.Split(0,0);plan.Select(1);plan.SetTag("ko",true);
            var saved=plan.Capture(0);var lease=new RecoveryLease();var store=new SubtitleRecoveryStore(folder);
            var draft=new RecoveryDraft{Row=0,Values=new(){{"Start","incomplete"},{"Text",doc.Events[0].Text}},OriginalText=doc.Events[0].Text};
            var metadata=new RecoveryMetadata{DocumentId=Guid.NewGuid(),Sequence=1,Timestamp=DateTimeOffset.UtcNow,Karaoke=[saved],Draft=draft,Title="karaoke"};
            var path=await store.WriteAsync(doc.CaptureSnapshot(),metadata,lease,lease.Renew());var recovered=await store.ReadAsync(path);
            var restored=ToshikiTimingPlan.Restore(Assert.Single(recovered.Metadata.Karaoke!));Assert.Equal(plan.PreviewText,restored.PreviewText);Assert.Equal(plan.Boundaries,restored.Boundaries);Assert.Equal(plan.Current,restored.Current);Assert.Equal(doc.Serialize(),recovered.Document.Serialize());Assert.Equal("incomplete",recovered.Metadata.Draft!.Values["Start"]);Assert.Equal(Encoding.ASCII.GetBytes("xyz"),recovered.Document.GetAttachments()[0].Decode());
            Assert.Throws<InvalidDataException>(()=>ToshikiTimingPlan.Restore(saved with{Cuts=[1]}));
            Assert.Throws<InvalidDataException>(()=>ToshikiTimingPlan.Restore(saved with{Boundaries=[2000]}));
        }
        finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}
    }
}
