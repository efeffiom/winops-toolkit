using System.Windows;
using WinOps.App.Views;
using WinOps.Core.Abstractions;
using WinOps.Core.Models;

namespace WinOps.App.Services;

public sealed class ConfirmationDialogService
{
    public Task<bool> ConfirmAsync(ActionDefinition action) => ConfirmAsync(action, string.Empty);

    public Task<bool> ConfirmAsync(ActionDefinition action, string details) =>
        Task.Run(() => Application.Current.Dispatcher.Invoke(() =>
        {
            var window = new ConfirmWindow(action, details)
            {
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            return window.ShowDialog() == true;
        }));
}
