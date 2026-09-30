using Avalonia;
using Avalonia.Styling;
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
