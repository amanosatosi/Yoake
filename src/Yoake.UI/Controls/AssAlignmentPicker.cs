using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Automation;

namespace Yoake.UI.Controls;

public sealed class AssAlignmentPicker : UserControl
{
    private readonly ToggleButton[] _buttons=new ToggleButton[9];
    private int _value=2;
    public event EventHandler? ValueChanged;
    public int Value {get=>_value;set{if(value is <1 or >9)throw new ArgumentOutOfRangeException(nameof(value));_value=value;for(var i=0;i<9;i++)_buttons[i].IsChecked=i+1==value;}}
    public AssAlignmentPicker()
    {
        var grid=new Grid{ColumnDefinitions=new("36,36,36"),RowDefinitions=new("28,28,28"),ColumnSpacing=2,RowSpacing=2};
        foreach(var value in new[]{7,8,9,4,5,6,1,2,3})
        {
            var column=(value-1)%3;var row=2-(value-1)/3;
            var button=new ToggleButton{Content=value.ToString(),Padding=new Thickness(5,1),HorizontalContentAlignment=column==0?HorizontalAlignment.Left:column==1?HorizontalAlignment.Center:HorizontalAlignment.Right,VerticalContentAlignment=row==0?VerticalAlignment.Top:row==1?VerticalAlignment.Center:VerticalAlignment.Bottom};
            button.Classes.Add("authoring");_buttons[value-1]=button;Grid.SetColumn(button,column);Grid.SetRow(button,row);grid.Children.Add(button);
            var description=$"{new[]{"Top","Middle","Bottom"}[row]} {new[]{"left","center","right"}[column]} · ASS {value}";
            ToolTip.SetTip(button,description);AutomationProperties.SetName(button,description);
            button.Click+=(_,_)=>{Value=value;ValueChanged?.Invoke(this,EventArgs.Empty);};
        }
        Content=grid;Value=2;
        KeyDown+=(_,e)=>
        {
            var next=e.Key switch
            {
                Key.Left=>Value-(Value%3==1?0:1),Key.Right=>Value+(Value%3==0?0:1),
                Key.Up=>Value<=6?Value+3:Value,Key.Down=>Value>=4?Value-3:Value,_=>0
            };
            if(next==0)return;Value=next;_buttons[next-1].Focus();ValueChanged?.Invoke(this,EventArgs.Empty);e.Handled=true;
        };
    }
}
