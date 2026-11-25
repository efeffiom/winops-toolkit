using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using WinOps.App.Services;
using WinOps.Core.Abstractions;
using WinOps.Core.Models;

namespace WinOps.App.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    protected readonly IConfirmationService Confirmation;
    protected CancellationTokenSource? _cts;

    private bool _isBusy;
    private string _statusMessage = "Ready";
    private int _progressPercent;
    private bool _progressVisible;
    private string? _errorMessage;

    protected ViewModelBase(IConfirmationService confirmation) => Confirmation = confirmation;

    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }
    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }
    public int ProgressPercent { get => _progressPercent; set => SetProperty(ref _progressPercent, value); }
    public bool ProgressVisible { get => _progressVisible; set => SetProperty(ref _progressVisible, value); }
    public string? ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }

    protected Func<ActionDefinition, Task<bool>> Confirm => action => Confirmation.ConfirmationHandler(action);

    protected IProgress<OperationProgress> CreateProgress() => new Progress<OperationProgress>(p =>
    {
        StatusMessage = string.IsNullOrWhiteSpace(p.Detail) ? p.Activity : $"{p.Activity} - {p.Detail}";
        ProgressPercent = Math.Clamp(p.Percent, 0, 100);
    });

    protected async Task<T?> RunAsync<T>(Func<CancellationToken, Task<T>> work, string activity)
    {
        if (IsBusy) return default;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        IsBusy = true;
        ProgressVisible = true;
        ProgressPercent = 0;
        StatusMessage = activity;
        ErrorMessage = null;
        try
        {
            return await work(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Cancelled";
            return default;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            StatusMessage = $"Failed: {ex.Message}";
            return default;
        }
        finally
        {
            IsBusy = false;
            ProgressPercent = 100;
        }
    }

    protected async Task RunAsync(Func<CancellationToken, Task> work, string activity)
    {
        if (IsBusy) return;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        IsBusy = true;
        ProgressVisible = true;
        ProgressPercent = 0;
        StatusMessage = activity;
        ErrorMessage = null;
        try
        {
            await work(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Cancelled";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            StatusMessage = $"Failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            ProgressPercent = 100;
        }
    }

    protected void Cancel() => _cts?.Cancel();

    /// <summary>
    /// Invokes the module's private or public RefreshAsync() method when one exists.
    /// This lets the shell implement a global F5 refresh without forcing every module
    /// to expose a public refresh command.
    /// </summary>
    public Task RefreshCurrentAsync()
    {
        var method = GetType().GetMethod("RefreshAsync",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, types: Type.EmptyTypes, modifiers: null);
        return method?.Invoke(this, null) as Task ?? Task.CompletedTask;
    }

    protected Task NotifyAsync(ActionExecutionResult result)
    {
        App.Services.GetRequiredService<MainViewModel>().NotifyCommand.Execute(result);
        return Task.CompletedTask;
    }
}

public sealed class StatusBridge
{
    public static readonly StatusBridge Instance = new();
    public event Action<string, int>? StatusChanged;
    public void Report(string message, int percent) => StatusChanged?.Invoke(message, percent);
}
