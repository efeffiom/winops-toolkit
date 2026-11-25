using System.Windows;
using WinOps.App.ViewModels;

namespace WinOps.App.Views;

public partial class ActiveDirectoryView
{
    private void ResetAdPassword_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ActiveDirectoryViewModel viewModel) return;
        viewModel.NewPassword = AdPasswordBox.Password;
        viewModel.ResetPasswordCommand.Execute(null);
        AdPasswordBox.Clear();
    }
}
