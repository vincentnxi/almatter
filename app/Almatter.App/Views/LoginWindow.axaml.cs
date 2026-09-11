using Avalonia.Controls;
using Almatter.App.Models;
using Almatter.App.Services;
using Almatter.App.ViewModels;

namespace Almatter.App.Views;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();

        // The login screen has no MainViewModel to hand it a palette, so it
        // reads the saved preference itself. Same store, same resolution —
        // logging out and back in never flashes the wrong theme.
        var settings = SettingsStore.Load();
        ColorTokens.ApplyTheme(ThemeDefinition.Resolve(settings.ThemeMode, SystemTheme.PrefersDark));
        ThemeResources.Apply(this, settings);
    }

    public LoginWindow(LoginViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}
