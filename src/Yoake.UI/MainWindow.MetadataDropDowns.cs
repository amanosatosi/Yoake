using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Yoake.UI;

public partial class MainWindow
{
    private void ConfigureMetadataDropDown(AutoCompleteBox input,Button arrow,bool actor)
    {
        // Refresh only on entry/open, not on every text edit or playback frame.
        input.AddHandler(GotFocusEvent,(_,_)=>_model?.RefreshMetadataChoices(actor),RoutingStrategies.Bubble);
        arrow.Click+=(_,_)=>ToggleMetadataDropDown(input);
        input.DropDownClosed+=(_,_)=>
        {
            input.FilterMode=AutoCompleteFilterMode.StartsWith;
            input.MinimumPrefixLength=actor&&_model?.FastActorEnabled==true?int.MaxValue:1;
        };
    }

    private void ToggleMetadataDropDown(AutoCompleteBox input)
    {
        var actor=ReferenceEquals(input,LineActor);
        if(_model is null||actor&&_model.FastActorEnabled)return;
        if(input.IsDropDownOpen){input.IsDropDownOpen=false;return;}
        _model.RefreshMetadataChoices(actor);
        // Use the native autocomplete popup, with an unfiltered view when
        // explicitly opened. Never clear or replace the user's current text
        // just to list the available choices.
        input.GetVisualDescendants().OfType<TextBox>().FirstOrDefault()?.Focus();
        input.FilterMode=AutoCompleteFilterMode.None;
        input.MinimumPrefixLength=0;
        input.IsDropDownOpen=true;
        if(!input.IsDropDownOpen)
        {
            input.FilterMode=AutoCompleteFilterMode.StartsWith;
            input.MinimumPrefixLength=1;
        }
    }
}
