using Yoake.Core.Audio;
using Yoake.Core.Media;
using Yoake.Core.Settings;
using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class UxCorrectionTests
{
    [Fact] public void RenameAndDeleteReplaceEventAndResetReferencesLosslessly()
    {
        var doc=AssDocument.CreateEmpty();var editor=new SubtitleEditor(doc);var style=editor.AddStyle();editor.EditStyle(style,new Dictionary<string,string>{{"Name","kanji"}});
        var line=editor.Insert(null,false);line.Style="kanji";
        const string source=@"{\rkanji\future(opaque)\1grd(0,&HFF0000&)}日本語 {\rkanji }{\r}{\rkanji-other}literal \rkanji {\t(0,1,\rkanji)}";
        line.Text=source;var before=doc.Serialize();
        editor.EditStyle(style,new Dictionary<string,string>{{"Name","kanji-main"}});
        Assert.Equal("kanji-main",line.Style);Assert.StartsWith(@"{\rkanji-main\future(opaque)",line.Text);
        Assert.Contains(@"{\rkanji-main }{\r}{\rkanji-other}literal \rkanji {\t(0,1,\rkanji)}",line.Text);
        editor.Undo.Undo();Assert.Equal(before,doc.Serialize());editor.Undo.Redo();
        editor.DeleteStyle(style,"Default");Assert.Equal("Default",line.Style);Assert.Contains(@"{\rDefault\future(opaque)",line.Text);
        editor.Undo.Undo();Assert.Contains(style,doc.Styles);Assert.Equal("kanji-main",line.Style);
    }
    [Fact] public void ResetOnlyReferencesPreventDeletionWithoutReplacement()
    {
        var doc=AssDocument.CreateEmpty();var editor=new SubtitleEditor(doc);var style=editor.AddStyle();var line=editor.Insert(null,false);line.Style="Default";line.Text="{\\r"+style.Name+"}Text";
        Assert.Throws<ArgumentException>(()=>editor.DeleteStyle(style));Assert.Contains(style,doc.Styles);
        editor.DeleteStyles([style],"Default");Assert.Equal(@"{\rDefault}Text",line.Text);
    }
    [Theory]
    [InlineData(@"\rName")]
    [InlineData(@"{\rName")]
    [InlineData(@"\{\rName}")]
    [InlineData(@"{\t(\rName)}")]
    public void UnrelatedOrIncompleteResetSyntaxIsUntouched(string text)=>Assert.Equal(text,AssStyleReferences.Replace(text,"Name","New"));
    [Fact] public void ClipboardCopiesMultipleStylesAndUnknownFormatsWithOneUndo()
    {
        var source=AssDocument.Parse("[V4+ Styles]\nFormat: Name, Fontname, Future\nStyle: 日本,Missing 日本,opaque\nStyle: Second,Arial,custom\n");var original=new SubtitleEditor(source);
        var destination=new SubtitleEditor(AssDocument.CreateEmpty());var before=destination.Document.Serialize();
        var pasted=destination.PasteStyles(original.CopyStyleText(source.Styles));Assert.Equal(2,pasted.Count);Assert.Equal("opaque",pasted[0].Get("Future"));
        var again=destination.PasteStyles(original.CopyStyleText(source.Styles));Assert.Equal("日本 1",again[0].Name);
        destination.Undo.Undo();Assert.Equal(3,destination.Document.Styles.Count);destination.Undo.Undo();Assert.Equal(before,destination.Document.Serialize());
        Assert.Throws<ArgumentException>(()=>destination.PasteStyles("Style: incomplete"));Assert.Equal(before,destination.Document.Serialize());
    }
    [Fact] public void MultiStyleMovementSortAndUndoPreserveUnknownSourceSlots()
    {
        var doc=AssDocument.Parse("[V4+ Styles]\nFormat: Name,Fontname\nStyle: D,Arial\n; keep this comment\nStyle: B,Arial\nStyle: C,Arial\nStyle: A,Arial\n[Future]\nopaque\n");var editor=new SubtitleEditor(doc);var before=doc.Serialize();var b=doc.Styles[1];var c=doc.Styles[2];
        editor.ReorderStyles([b,c],StyleOrder.Top);Assert.Equal(new[]{"B","C","D","A"},doc.Styles.Select(s=>s.Name));editor.Undo.Undo();Assert.Equal(before,doc.Serialize());
        editor.ReorderStyles([b,c],StyleOrder.Bottom);Assert.Equal(new[]{"D","A","B","C"},doc.Styles.Select(s=>s.Name));editor.Undo.Undo();
        editor.ReorderStyles([b,c],StyleOrder.Up);Assert.Equal(new[]{"B","C","D","A"},doc.Styles.Select(s=>s.Name));editor.Undo.Undo();
        editor.ReorderStyles([b,c],StyleOrder.Down);Assert.Equal(new[]{"D","A","B","C"},doc.Styles.Select(s=>s.Name));editor.Undo.Undo();
        editor.ReorderStyles([],StyleOrder.Sort);Assert.Equal(new[]{"A","B","C","D"},doc.Styles.Select(s=>s.Name));Assert.Contains("; keep this comment",doc.Serialize());editor.Undo.Undo();Assert.Equal(before,doc.Serialize());
    }
    [Theory]
    [InlineData(255,0,0)] [InlineData(0,255,0)] [InlineData(0,0,255)] [InlineData(0,0,0)] [InlineData(255,255,255)] [InlineData(23,117,208)]
    public void HsvHslRoundTrip(int r,int g,int b)
    {
        var color=new AssColor((byte)r,(byte)g,(byte)b,128);var hsv=ColorSpace.Hsv(color);var hsl=ColorSpace.Hsl(color);
        Assert.Equal(color,ColorSpace.FromHsv(hsv.Hue,hsv.Saturation,hsv.Component,128));Assert.Equal(color,ColorSpace.FromHsl(hsl.Hue,hsl.Saturation,hsl.Component,128));
    }
    [Fact] public void HexAndSpectrumKeepRgbBgrAndInvertedAlphaCorrect()
    {
        var color=new AssColor(0x12,0x34,0x56,0x80);Assert.Equal("&H80563412",color.StyleValue);Assert.Equal(127,color.Opacity);Assert.Equal("#123456",ColorSpace.Html(color));
        Assert.True(ColorSpace.TryHtml("#123456",0x80,out var parsed));Assert.Equal(color,parsed);Assert.True(AssColor.TryParse(color.StyleValue,out parsed));Assert.Equal(color,parsed);
        Assert.Equal(new AssColor(255,0,0,0),ColorSpace.Spectrum(0,1,0));Assert.Equal(new AssColor(0,0,0,255),ColorSpace.Spectrum(0,1,1,255));Assert.Equal(new AssColor(255,255,255,0),ColorSpace.Spectrum(240,0,0));
    }
    [Fact] public void RecentColorsPersistDeduplicateAndRetainAlphaIdentity()
    {
        var folder=Path.Combine(Path.GetTempPath(),"Yoake-colors-"+Guid.NewGuid());var path=Path.Combine(folder,"colors.txt");
        try{var store=new RecentColorStore(path);var pink=new AssColor(255,128,192,0);store.Remember(pink);store.Remember(pink with{Transparency=128});store.Remember(pink);var reloaded=new RecentColorStore(path).Load();Assert.Equal(2,reloaded.Count);Assert.Equal(pink,reloaded[0]);Assert.Equal(128,reloaded[1].Transparency);for(var i=0;i<40;i++)store.Remember(new((byte)i,0,0,0));Assert.Equal(32,store.Load().Count);}
        finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}
    }
    [Fact] public void SignedWaveformsRetainAsymmetryAtEveryResolution()
    {
        var envelope=WaveformEnvelope.FromSamples([0.1f,-0.2f,0.8f,-0.4f]);Assert.Equal(-0.4f,envelope.Minimum);Assert.Equal(0.8f,envelope.Maximum);
        var data=new WaveformData([new(-0.2f,0.8f),new(-0.7f,0.3f),new(0.1f,0.4f)],0.001);
        Assert.Equal(new WaveformEnvelope(-0.7f,0.8f),data.Range(0,0.002));Assert.Equal(new WaveformEnvelope(0.1f,0.4f),data.Range(0.002,0.003));
        Assert.Equal(new WaveformEnvelope(-0.7f,0.8f),data.Range(0,1));Assert.Equal(0.001,data.StepSeconds);
    }
    [Fact] public void GainIsLinearBoundedMutedAndFinite()
    {
        Assert.Equal((short)16384,PlaybackGain.Pcm16(1,0.5,false));Assert.Equal(short.MaxValue,PlaybackGain.Pcm16(2,1,false));Assert.Equal((short)-short.MaxValue,PlaybackGain.Pcm16(-2,1,false));Assert.Equal(0,PlaybackGain.Pcm16(1,1,true));Assert.Equal(0,PlaybackGain.Pcm16(float.NaN,1,false));
    }
    [Fact] public void PreviewDoesNotMutateSaveOrReparseUnknownSource()
    {
        var doc=AssDocument.CreateEmpty();var editor=new SubtitleEditor(doc);var line=editor.Insert(null,false);line.Text=@"{\future(opaque)}old";var before=doc.Serialize();var revision=doc.Revision;
        var values=new Dictionary<string,string>{{"Text",@"{\future(opaque)}မြန်မာ"}};line.ShowDraft(values);
        Assert.Equal(@"{\future(opaque)}မြန်မာ",line.DisplayText);Assert.Equal(before,doc.Serialize());Assert.Equal(revision,doc.Revision);
        var preview=doc.SerializePreview(line,values);Assert.Contains("မြန်မာ",preview);Assert.DoesNotContain("}old",preview);Assert.Equal(before,doc.Serialize());line.ShowDraft(null);Assert.Equal(line.Text,line.DisplayText);
    }
    [Fact] public void FrameNavigationUsesVfrTimesNearestKeysAndSignedOffsets()
    {
        double[] times=[0,0.033,0.074,0.120];Assert.Equal(2,FrameNavigation.AtTime(times,0.08));Assert.Equal(3,FrameNavigation.AtTime(times,10));
        int[] keys=[0,10,30];Assert.Equal(10,FrameNavigation.NearestKeyframe(keys,19));Assert.Equal(30,FrameNavigation.NearestKeyframe(keys,26));Assert.Equal(10,FrameNavigation.NearestKeyframe(keys,20));Assert.Equal(30,FrameNavigation.StepKeyframe(keys,10,1));Assert.Equal(0,FrameNavigation.StepKeyframe(keys,10,-1));Assert.Equal("+7482ms; +4772ms",FrameNavigation.Relative(8.482,1000,3710));
    }
    [Fact] public void VisualStandbyTracksStaticAlignmentAndMovingAnchors()
    {
        Assert.Equal(7,AssVisualTags.Alignment(@"{\an7}text",2));Assert.Equal(8,AssVisualTags.Alignment(@"{\a6}text",2));
        Assert.Equal(2,AssVisualTags.Alignment(@"{\t(0,1,\an7)}text",2));
        Assert.Equal(new AssPoint(50,100),AssVisualTags.PositionAtTime(@"{\move(0,0,100,200)}text",500,1000));
        Assert.Equal(new AssPoint(100,200),AssVisualTags.PositionAtTime(@"{\move(0,0,100,200,100,400)}text",500,1000));
    }
    [Fact] public void LargeTrackPreviewUsesCachedBaseWithoutChangingOtherRows()
    {
        var source=new System.Text.StringBuilder(AssDocument.CreateEmpty().Serialize());
        for(var i=0;i<20000;i++)source.Append("Dialogue: 0,0:00:00.00,0:00:02.00,Default,,0,0,0,,{\\future(opaque)}日本 မြန်မာ ").Append(i).Append("\r\n");
        var doc=AssDocument.Parse(source.ToString());var before=doc.Serialize();var revision=doc.Revision;
        var line=doc.Events[10000];var preview=doc.SerializePreview(line,new Dictionary<string,string>{{"Text","live draft"}});
        Assert.Equal(20000,doc.Events.Count);Assert.Contains("live draft",preview);Assert.Contains("日本 မြန်မာ 19999",preview);
        Assert.Same(before,doc.Serialize());Assert.Equal(revision,doc.Revision);Assert.Contains("future(opaque)",line.Text);
    }
    [Fact] public void AudioDefaultsAndPersistencePreserveIntentionalSilence()
    {
        var defaults=new AppSettings().Normalize();Assert.Equal(0.8,defaults.PlaybackVolume);Assert.Equal(1,defaults.AudioIntensity);
        var muted=new AppSettings{PlaybackVolume=0,PlaybackMuted=true,AudioDisplayHeight=220,AudioIntensity=2}.Normalize();Assert.Equal(0,muted.PlaybackVolume);Assert.True(muted.PlaybackMuted);
    }
}
