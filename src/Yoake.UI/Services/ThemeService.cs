using Avalonia;
using Avalonia.Styling;
using Avalonia.Controls;
using Avalonia.Media;
using Yoake.Core.Settings;

namespace Yoake.UI.Services;

public interface IThemeService
{
    ThemePreference Current { get; }
    void Apply(ThemePreference preference);
    ThemePreference Next();
}

public sealed class ThemeService : IThemeService
{
    public ThemePreference Current { get; private set; } = ThemePreference.System;

    public void LoadVisualPalette(string profileDirectory,Action<string>? warning=null)
    {
        if(Application.Current is not {} app)return;
        var palettes=VisualPaletteFile.Load(profileDirectory,warning);
        ApplyPalette(ThemeVariant.Light,palettes.Light);
        ApplyPalette(ThemeVariant.Dark,palettes.Dark);
        void ApplyPalette(ThemeVariant variant,VisualPalette palette)
        {
            if(!app.Resources.ThemeDictionaries.TryGetValue(variant,out var provider)||provider is not IResourceDictionary resources)return;
            resources["VisualToolPrimaryBrush"]=new SolidColorBrush(Color.Parse(palette.Primary));
            resources["VisualToolDetailBrush"]=new SolidColorBrush(Color.Parse(palette.Detail));
            resources["VisualToolWarningBrush"]=new SolidColorBrush(Color.Parse(palette.Warning));
        }
    }

    public void Apply(ThemePreference preference)
    {
        Current = preference;
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = preference switch
            {
                ThemePreference.Light => ThemeVariant.Light,
                ThemePreference.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
        }
    }

    public ThemePreference Next()
    {
        var next = Current switch
        {
            ThemePreference.System => ThemePreference.Dark,
            ThemePreference.Dark => ThemePreference.Light,
            _ => ThemePreference.System,
        };
        Apply(next);
        return next;
    }
}
