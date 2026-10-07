using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Yoake.UI.Controls;

public sealed class AssAlignmentPicker : UserControl
{
    private readonly ToggleButton[] _buttons=new ToggleButton[9];
    private int _value=2;
    public event EventHandler? ValueChanged;
    public int Value {get=>_value;set{if(value is <1 or >9)throw new ArgumentOutOfRangeException(nameof(value));_value=value;for(var i=0;i<9;i++)_buttons[i].IsChecked=i+1==value;}}
    public AssAlignmentPicker()
    {
        var grid=new Grid{ColumnDefinitions=new("32,32,32"),RowDefinitions=new("26,26,26"),ColumnSpacing=2,RowSpacing=2};
        foreach(var value in new[]{7,8,9,4,5,6,1,2,3})
        {
            var button=new ToggleButton{Content=value.ToString(),Padding=new Thickness(1)};_buttons[value-1]=button;Grid.SetColumn(button,(value-1)%3);Grid.SetRow(button,2-(value-1)/3);grid.Children.Add(button);
            ToolTip.SetTip(button,$"ASS alignment {value}");button.Click+=(_,_)=>{Value=value;ValueChanged?.Invoke(this,EventArgs.Empty);};
        }
        Content=grid;Value=2;
    }
}
