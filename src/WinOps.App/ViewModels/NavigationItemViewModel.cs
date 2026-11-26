using CommunityToolkit.Mvvm.ComponentModel;
using WinOps.App.Services;

namespace WinOps.App.ViewModels;

/// <summary>
/// Wraps a navigation entry so the shell can bind and update the selected
/// state of each sidebar item without rebuilding the whole collection.
/// </summary>
public sealed partial class NavigationItemViewModel : ObservableObject
{
    public NavigationItem Item { get; }

    public string Key => Item.Key;
    public string Title => Item.Title;
    public string Glyph => Item.Glyph;
    public string Category => Item.Category;

    [ObservableProperty] private bool _isSelected;

    public NavigationItemViewModel(NavigationItem item) => Item = item;
}
