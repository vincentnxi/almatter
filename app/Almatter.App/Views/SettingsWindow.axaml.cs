using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Almatter.App.ViewModels;

namespace Almatter.App.Views;

/// <summary>
/// The settings, in a window of their own: it can be moved aside to watch
/// a change land on the conversation behind it, and stays open until it is
/// closed. Owned by the main window, so it stays above it, minimises with
/// it and goes when it goes.
/// </summary>
public partial class SettingsWindow : Window
{
    // Remembered for the session only: reopening the window lands on the
    // page that was last looked at.
    private static int _lastPage;

    private readonly Control[] _pages;
    private readonly Button[] _navButtons;
    private readonly MainViewModel? _vm;

    /// <summary>Only for the XAML previewer.</summary>
    public SettingsWindow()
    {
        InitializeComponent();
        _pages = [PageAppearance, PageText, PageMessages, PageGeneral];
        _navButtons = [NavAppearance, NavText, NavMessages, NavGeneral];
        ShowPage(_lastPage);
    }

    public SettingsWindow(MainViewModel vm) : this()
    {
        _vm = vm;
        DataContext = vm;
        ThemeResources.Apply(this, vm.Settings);

        // Unhooked on Closed: the view model outlives this window, and a
        // handler left on it would keep every closed settings window alive.
        vm.ThemeResourcesChanged += OnThemeResourcesChanged;
        Opened += (_, _) => MainWindow.ApplyTitleBarTheme(this);
        Closed += (_, _) => vm.ThemeResourcesChanged -= OnThemeResourcesChanged;
    }

    private void OnThemeResourcesChanged(object? sender, EventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        ThemeResources.Apply(this, _vm.Settings);
        MainWindow.ApplyTitleBarTheme(this);
    }

    private void OnNavClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out var page))
        {
            ShowPage(page);
        }
    }

    private void ShowPage(int page)
    {
        page = Math.Clamp(page, 0, _pages.Length - 1);
        _lastPage = page;
        for (var i = 0; i < _pages.Length; i++)
        {
            _pages[i].IsVisible = i == page;
            _navButtons[i].Classes.Set("selected", i == page);
        }

        // The theme menu belongs to the Apparence page.
        if (_vm is not null && page != 0)
        {
            _vm.IsThemeMenuOpen = false;
        }

        PageScroller.Offset = default;
    }
}
