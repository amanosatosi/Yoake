using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Yoake.Core.Commands;
using Yoake.UI.Commands;
using Yoake.UI.ViewModels;

namespace Yoake.UI.Controls;

public sealed class AutomationManagerWindow : Window
{
    public AutomationManagerWindow(MainWindowViewModel model)
    {
        Name = "AutomationManager"; Title = "Automation Manager"; Width = 720; Height = 500; MinWidth = 500; MinHeight = 380; FontSize = 12;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,2*,Auto,*,Auto"), Margin = new Thickness(10), RowSpacing = 6 };
        grid.Children.Add(new TextBlock { Text = "Autoload scripts and scripts attached to this document" });
        var list = new ListBox { Name = "AutomationScriptList", ItemsSource = model.AutomationScripts };
        Grid.SetRow(list, 1); grid.Children.Add(list);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        var add = new Button { Content = "Add…", Command = model.Actions[CommandIds.AutomationLoad] };
        var reload = new Button { Content = "Reload", Command = model.Actions[CommandIds.AutomationReload] };
        var remove = new Button { Content = "Remove", Command = model.Actions[CommandIds.AutomationRemove] };
        var rescan = new Button { Content = "Rescan autoload", Command = model.Actions[CommandIds.AutomationRescan] };
        var paths = new Button { Content = "Search paths…", Command = model.Actions[CommandIds.AutomationPaths] };
        actions.Children.Add(add); actions.Children.Add(reload); actions.Children.Add(remove); actions.Children.Add(rescan); actions.Children.Add(paths);
        Grid.SetRow(actions, 2); grid.Children.Add(actions);
        var details = new TextBox { Name = "AutomationScriptDetails", IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        Grid.SetRow(details, 3); grid.Children.Add(details);
        void ShowSelection()
        {
            var item = list.SelectedItem as AutomationScriptItem;
            reload.CommandParameter = item; remove.CommandParameter = item;
            reload.IsEnabled = item is not null; remove.IsEnabled = item?.DocumentId is not null;
            details.Text = item is null ? $"Application: {model.AutomationBaseDirectory}\nUser: {model.AutomationUserDirectory}"
                : $"{item.Name} {item.Version}\n{item.Author}\n{item.Path}\n{item.Scope} · {item.Status}\n\n{item.Description}";
        }
        list.SelectionChanged += (_, _) => ShowSelection();
        void Update(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName != nameof(MainWindowViewModel.AutomationScripts)) return;
            var selected = list.SelectedItem as AutomationScriptItem;
            list.ItemsSource = model.AutomationScripts;
            if (selected is not null && model.AutomationScripts.Contains(selected)) list.SelectedItem = selected;
            ShowSelection();
        }
        model.PropertyChanged += Update; Closed += (_, _) => model.PropertyChanged -= Update;
        var close = new Button { Content = "Close", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 80 };
        close.Click += (_, _) => Close(); Grid.SetRow(close, 4); grid.Children.Add(close);
        Content = grid; ShowSelection();
    }
}
