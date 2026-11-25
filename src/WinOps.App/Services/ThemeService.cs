using System.Windows;
using WinOps.Persistence;

namespace WinOps.App.Services;

public sealed class ThemeService
{
    private readonly SettingsRepository _settings;

    public ThemeService(SettingsRepository settings) => _settings = settings;

    public string CurrentTheme { get; private set; } = "Light";

    public event Action? ThemeChanged;

    public void ApplySavedTheme()
    {
        var theme = _settings.GetAsync("Theme", "Light").GetAwaiter().GetResult();
        Apply(theme);
    }

    public async Task SetThemeAsync(string theme)
    {
        CurrentTheme = theme;
        Apply(theme);
        await _settings.SetAsync("Theme", theme);
    }

    public void Apply(string theme)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var source = new Uri(theme.Equals("Dark", StringComparison.OrdinalIgnoreCase)
            ? "Themes/Dark.xaml"
            : "Themes/Light.xaml", UriKind.Relative);
        var dictionary = new ResourceDictionary { Source = source };
        if (dictionaries.Count > 0) dictionaries[0] = dictionary;
        else dictionaries.Add(dictionary);
        CurrentTheme = theme;
        ThemeChanged?.Invoke();
    }

    public void Toggle()
    {
        var next = CurrentTheme.Equals("Dark", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark";
        _ = SetThemeAsync(next);
    }
}
