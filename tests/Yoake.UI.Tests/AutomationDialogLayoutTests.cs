using Avalonia.Controls;
using Xunit;
using Yoake.Core.Automation;
using Yoake.UI.Controls;

namespace Yoake.UI.Tests;

public sealed class AutomationDialogLayoutTests
{
    [Fact]
    public void DropdownKeepsItsWidthWhenSelectionTextChanges()
    {
        var definition=new AutomationLine();
        definition["class"]="dropdown";definition["name"]="choice";
        definition["items"]=new Dictionary<string,object?>{{"1","Short"},{"2","A much longer DependencyControl selection"}};
        definition["value"]="Short";
        var panel=new AutomationDialogPanel([definition]);
        var root=Assert.IsType<DockPanel>(panel.Content);
        var grid=Assert.IsType<Grid>(root.Children[1]);
        var dropdown=Assert.IsType<ComboBox>(Assert.Single(grid.Children));
        Assert.Equal(240,dropdown.Width);
        dropdown.SelectedItem="A much longer DependencyControl selection";
        Assert.Equal(240,dropdown.Width);
        Assert.True(panel.TryRead("OK",false,out var result));
        Assert.Equal("A much longer DependencyControl selection",result.Values["choice"]);
    }
}
