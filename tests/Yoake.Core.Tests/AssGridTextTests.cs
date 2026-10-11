using Yoake.Core.Settings;
using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class AssGridTextTests
{
    [Fact] public void ProjectionsPreserveEscapesComplexTextUnknownTagsAndIncompleteBlocks()
    {
        const string source=@"{\future(opaque)\distort(0,0,1,1)}日本語 é 👩‍👩‍👧‍👦 \{literal\} <漢|かん>\N{\b1}မြန်မာ{incomplete";
        Assert.Equal(source,AssGridText.Project(source,GridTagDisplayMode.Full));
        Assert.Equal(@"☀日本語 é 👩‍👩‍👧‍👦 \{literal\} <漢|かん>\N☀မြန်မာ{incomplete",AssGridText.Project(source,GridTagDisplayMode.Simplified));
        Assert.Equal(@"日本語 é 👩‍👩‍👧‍👦 \{literal\} <漢|かん>\Nမြန်မာ{incomplete",AssGridText.Project(source,GridTagDisplayMode.Hidden));
        Assert.Equal("☀☀end",AssGridText.Project("{}{comment}end",GridTagDisplayMode.Simplified));
        Assert.Throws<ArgumentException>(()=>AssGridText.Project(source,(GridTagDisplayMode)77));
    }
    [Fact] public void ModePersistenceRecoversDamagedMemberWithoutLosingOtherPreferences()
    {
        var root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());Directory.CreateDirectory(root);
        try
        {
            var store=new SettingsStore(Path.Combine(root,"settings.json"));
            Assert.Equal(GridTagDisplayMode.Simplified,new AppSettings().Normalize().GridTagDisplay);
            store.Save(new AppSettings{GridTagDisplay=GridTagDisplayMode.Hidden});Assert.Equal(GridTagDisplayMode.Hidden,store.Load().GridTagDisplay);
            foreach(var value in new[]{"99","null","\"bad\"","{}"})
            {
                File.WriteAllText(store.Path,"{\"gridTagDisplay\":"+value+",\"toolbarVisible\":false,\"viewMode\":2}");
                var settings=store.Load();Assert.Equal(GridTagDisplayMode.Simplified,settings.GridTagDisplay);
                Assert.False(settings.ToolbarVisible);Assert.Equal(WorkspaceViewMode.Video,settings.ViewMode);
            }
        }
        finally{Directory.Delete(root,true);}
    }
}
