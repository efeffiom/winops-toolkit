using System.Windows;
using WinOps.App.ViewModels;

namespace WinOps.App.Views;

public partial class UsersView
{
    private void ResetPassword_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not UsersViewModel viewModel) return;
        viewModel.ResetPassword = ResetPasswordBox.Password;
        viewModel.ResetPasswordCommand.Execute(null);
        ResetPasswordBox.Clear();
    }

    private void CreateUser_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not UsersViewModel viewModel) return;
        viewModel.NewPassword = NewPasswordBox.Password;
        viewModel.CreateUserCommand.Execute(null);
        NewPasswordBox.Clear();
    }
}
