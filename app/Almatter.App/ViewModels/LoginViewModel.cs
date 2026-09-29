using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Almatter.App.Interop;
using Almatter.App.Localization;
using Almatter.App.Models;
using Almatter.App.Services;

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

    public string ButtonLabel => IsBusy ? Loc.S.LoginBusy : Loc.S.LoginButton;

    /// <summary>
    /// For someone sent back to sign in because their session ran out: the
    /// server and the account are already known, so only the password is left
    /// to type, and the reason is said in the box where errors go.
    /// </summary>
    public void ShowSessionNotice(string notice, string serverUrl, string loginId)
    {
        ServerUrl = serverUrl;
        LoginId = loginId;
        ErrorMessage = notice;
    }

    /// <summary>
    /// The login screen comes before the settings panel, so it carries its
    /// own small language switch: someone handed the app on a Windows in the
    /// other language shouldn't have to sign in blind first. Saved like any
    /// other preference, so the main window opens in the same language.
    /// </summary>
    [RelayCommand]
    private void SetLanguage(AppLanguage language)
    {
        var settings = SettingsStore.Load();
        settings.Language = language;
        SettingsStore.Save(settings);
        Loc.Instance.SetLanguage(language);
        OnPropertyChanged(nameof(ButtonLabel));
    }

    private bool CanLogIn => !IsBusy && ServerUrl.Trim().Length > 0 && LoginId.Trim().Length > 0 && Password.Length > 0;

    [RelayCommand(CanExecute = nameof(CanLogIn))]
    private async System.Threading.Tasks.Task LogInAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var baseUrl = ServerUrl.Trim().TrimEnd('/');
            // "chat.example.com" is how people write a server's address; without a
            // scheme the core cannot even build the request, and what came back was
            // a builder error about "relative URL without a base".
            if (!baseUrl.Contains("://", StringComparison.Ordinal))
            {
                baseUrl = "https://" + baseUrl;
            }
            var (token, user) = await _service.LoginAsync(baseUrl, LoginId.Trim(), Password);
            LoggedIn?.Invoke(this, new Session(baseUrl, token, user));
        }
        catch (MattermostServiceException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            ErrorMessage = Loc.S.LoginFailed(ex.Message);
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
