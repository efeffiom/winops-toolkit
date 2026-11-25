using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using WinOps.App.ViewModels;

namespace WinOps.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<MainViewModel>();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var model = DataContext as MainViewModel;

        if (control && e.Key == Key.K)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (control && e.Key == Key.D)
        {
            model?.ToggleThemeCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            model?.RefreshCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && SearchBox.IsFocused)
        {
            model?.ClearSearchCommand.Execute(null);
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }
}
