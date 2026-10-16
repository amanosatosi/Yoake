using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Yoake.UI.Services;

namespace Yoake.UI.Controls;

public sealed class AutomationPathsWindow : Window
{
    public AutomationPathsWindow(IReadOnlyList<string> autoload, IReadOnlyList<string> includes)
    {
        Name = "AutomationSearchPaths"; Title = "Automation search paths"; Width = 620; Height = 450; MinWidth = 460; MinHeight = 340; FontSize = 12;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,*,Auto,Auto"), RowSpacing = 5, Margin = new Thickness(10) };
        root.Children.Add(new TextBlock { Text = "One directory per line, in search order. Use absolute paths or ?data / ?user tokens.", TextWrapping = TextWrapping.Wrap });
        var autoloadLabel = new TextBlock { Text = "Autoload directories" }; Grid.SetRow(autoloadLabel, 1); root.Children.Add(autoloadLabel);
        var auto = new TextBox { Text = string.Join('\n', autoload), AcceptsReturn = true }; Grid.SetRow(auto, 2); root.Children.Add(auto);
        var includesLabel = new TextBlock { Text = "Include/module directories (master directory is searched first)" }; Grid.SetRow(includesLabel, 3); root.Children.Add(includesLabel);
        var inc = new TextBox { Text = string.Join('\n', includes), AcceptsReturn = true }; Grid.SetRow(inc, 4); root.Children.Add(inc);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap }; Grid.SetRow(error, 5); root.Children.Add(error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 5 };
        var defaults = new Button { Content = "Use defaults" }; var save = new Button { Content = "Apply and reload", IsDefault = true }; var cancel = new Button { Content = "Cancel", IsCancel = true };
        foreach (var button in new[] { defaults, save, cancel }) actions.Children.Add(button);
        defaults.Click += (_, _) => { auto.Text = "?data/automation/autoload\n?user/automation/autoload"; inc.Text = "?user/automation/include\n?data/automation/include"; };
        cancel.Click += (_, _) => Close();
        save.Click += (_, _) =>
        {
            string[] Lines(TextBox box) => (box.Text ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var a = Lines(auto); var i = Lines(inc);
            if (a.Concat(i).Any(p => !Path.IsPathFullyQualified(p) && !p.StartsWith("?data/", StringComparison.Ordinal) && !p.StartsWith("?user/", StringComparison.Ordinal)))
            { error.Text = "Use absolute directories or ?data/ / ?user/ paths."; return; }
            Close(new AutomationSearchPaths(a, i));
        };
        Grid.SetRow(actions, 6); root.Children.Add(actions); Content = root;
    }
}
