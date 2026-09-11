using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Almatter.App.Interop;
using Almatter.App.Models;

namespace Almatter.App.ViewModels;

public partial class LoginViewModel : ViewModelBase
{
    private readonly MattermostService _service = new();

    [ObservableProperty]
    public partial string ServerUrl { get; set; } = "";

    [ObservableProperty]
    public partial string LoginId { get; set; } = "";

    [ObservableProperty]
    public partial string Password { get; set; } = "";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>Raised once login succeeds; the view closes itself and hands the session off.</summary>
    public event EventHandler<Session>? LoggedIn;

    public string ButtonLabel => IsBusy ? "Connexion…" : "Se connecter";

    private bool CanLogIn => !IsBusy && ServerUrl.Trim().Length > 0 && LoginId.Trim().Length > 0 && Password.Length > 0;

    [RelayCommand(CanExecute = nameof(CanLogIn))]
    private async System.Threading.Tasks.Task LogInAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var baseUrl = ServerUrl.Trim().TrimEnd('/');
            var (token, user) = await _service.LoginAsync(baseUrl, LoginId.Trim(), Password);
            LoggedIn?.Invoke(this, new Session(baseUrl, token, user));
        }
        catch (MattermostServiceException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Connexion impossible : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnServerUrlChanged(string value) => LogInCommand.NotifyCanExecuteChanged();
    partial void OnLoginIdChanged(string value) => LogInCommand.NotifyCanExecuteChanged();
    partial void OnPasswordChanged(string value) => LogInCommand.NotifyCanExecuteChanged();
    partial void OnIsBusyChanged(bool value)
    {
        LogInCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ButtonLabel));
    }
}
