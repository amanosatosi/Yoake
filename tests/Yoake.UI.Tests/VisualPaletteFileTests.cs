using Xunit;
using Yoake.UI.Services;

namespace Yoake.UI.Tests;

public sealed class VisualPaletteFileTests
{
    [Fact] public void DefaultFileMatchesBuiltInPalettes()
    {
        var colors=VisualPaletteFile.Parse(VisualPaletteFile.DefaultJson);
        Assert.Equal(VisualPaletteFile.LightDefault,colors.Light);
        Assert.Equal(VisualPaletteFile.DarkDefault,colors.Dark);
    }
    [Fact] public void PartialInvalidThemeDoesNotDiscardValidColors()
    {
        var warnings=new List<string>();
        var colors=VisualPaletteFile.Parse("""{"dark":{"primary":"#AA5522","detail":"invalid"}}""",warnings.Add);
        Assert.Equal("#AA5522",colors.Dark.Primary);
        Assert.Equal(VisualPaletteFile.DarkDefault.Detail,colors.Dark.Detail);
        Assert.Equal(VisualPaletteFile.LightDefault,colors.Light);
        Assert.Single(warnings);
        Assert.Equal(VisualPaletteFile.DarkDefault,VisualPaletteFile.Parse("{").Dark);
    }
    [Fact] public void ProfileFileIsCreatedOnceAndNeverOverwritten()
    {
        var root=Path.Combine(Path.GetTempPath(),"yoake-theme-"+Guid.NewGuid().ToString("N"));
        try
        {
            VisualPaletteFile.Load(root);
            var path=Path.Combine(root,"themes","visual-tools.json");
            Assert.True(File.Exists(path));
            const string custom="""{"light":{"warning":"#112233"}}""";
            File.WriteAllText(path,custom);
            Assert.Equal("#112233",VisualPaletteFile.Load(root).Light.Warning);
            Assert.Equal(custom,File.ReadAllText(path));
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
