using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Almatter.App.Diagnostics;
using Almatter.App.Models;
using Almatter.App.Services;
using Almatter.App.ViewModels;
using Almatter.App.Views;

namespace Almatter.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var stored = SessionStore.Load();
            if (stored is not null)
            {
                ShowMainWindow(desktop, windowToClose: null, stored);
            }
            else
            {
                ShowLoginWindow(desktop);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Shows a fresh login screen — used at startup with no remembered session, and again after logging out.</summary>
    public static void ShowLoginWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var loginViewModel = new LoginViewModel();
        var loginWindow = new LoginWindow(loginViewModel);

        loginViewModel.LoggedIn += (_, session) =>
        {
            SessionStore.Save(session);
            ShowMainWindow(desktop, loginWindow, session);
        };

        desktop.MainWindow = loginWindow;
        loginWindow.Show();
    }

    /// <summary>Opens the main window for an authenticated session — a fresh login or one just restored from disk — closing whatever window (if any) preceded it.</summary>
    public static void ShowMainWindow(IClassicDesktopStyleApplicationLifetime desktop, Window? windowToClose, Session session)
    {
        try
        {
            var mainViewModel = new MainViewModel(session);
            var mainWindow = new MainWindow { DataContext = mainViewModel };

            desktop.MainWindow = mainWindow;
            mainWindow.Show();
            windowToClose?.Close();

            _ = mainViewModel.LoadAsync();
        }
        catch (Exception ex)
        {
            // A failure here would otherwise close the only window and
            // silently exit the whole process with no explanation.
            CrashLogger.Write("Opening the main window", ex);
            if (windowToClose is null)
            {
                // This was a restored session at startup, with no login
                // window to fall back to — clear it and show a normal
                // login instead of leaving the app with no window at all.
                SessionStore.Clear();
                ShowLoginWindow(desktop);
            }
        }
    }
}
