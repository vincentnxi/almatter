using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Almatter.App.Interop;
using Almatter.App.Localization;

namespace Almatter.App.ViewModels;

/// <summary>
/// "A newer version is out": a banner above the conversation, and a switch
/// plus a "check now" button in the settings. The asking is done by the core
/// (see almatter-core's updates.rs), through the same guarded client as link
/// previews, so the app loads no second network stack for it.
///
/// On Windows, in an installed copy, the core then downloads the new
/// installer in the background and checks its Ed25519 signature; once that
/// passes, the banner offers "Restart to update", which checks the file once
/// more, runs it silently and quits — the installer starts Almatter again.
/// Nothing is installed without that click. Anywhere else, or when any step
/// fails, the banner's button opens the release's page on GitHub as before.
/// </summary>
public partial class MainViewModel
{
    /// <summary>Asks the window to close, which quits the app, so the installer can replace its files.</summary>
    public event EventHandler? QuitForUpdateRequested;

    /// <summary>
    /// Only a copy installed by the installer updates itself: one run from a
    /// build folder (a developer's) would install over the real one. The
    /// installer always puts Almatter in %LOCALAPPDATA%\Programs\Almatter.
    /// </summary>
    private static readonly bool CanInstallUpdates = OperatingSystem.IsWindows() && IsInstalledCopy();

    private static bool IsInstalledCopy()
    {
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        var installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Almatter");
        return exeDir is not null && string.Equals(Path.GetFullPath(exeDir).TrimEnd('\\'), Path.GetFullPath(installDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The version whose installer is downloaded and checked. Null while there is none.</summary>
    private string? _readyUpdateVersion;

    /// <summary>The version being downloaded, so two checks don't fetch it twice.</summary>
    private string? _downloadingUpdateVersion;

    private bool _isRestartingForUpdate;

    /// <summary>Set when the installer could not be run after all: the banner goes back to the GitHub page.</summary>
    private bool _updateInstallFailed;

    /// <summary>The banner offers "Restart to update" rather than "View on GitHub".</summary>
    public bool IsUpdateReady => AvailableUpdate is { } update && update.Version == _readyUpdateVersion && !_updateInstallFailed;

    public bool IsUpdateNotReady => !IsUpdateReady;

    public string UpdateRestartText => _isRestartingForUpdate ? Loc.S.UpdateRestarting : Loc.S.UpdateRestart;

    public bool CanRestartToUpdate => !_isRestartingForUpdate;

    private void RefreshUpdateReadiness()
    {
        OnPropertyChanged(nameof(IsUpdateReady));
        OnPropertyChanged(nameof(IsUpdateNotReady));
        OnPropertyChanged(nameof(UpdateBannerTitle));
        OnPropertyChanged(nameof(UpdateBannerDetail));
        OnPropertyChanged(nameof(UpdateRestartText));
        OnPropertyChanged(nameof(CanRestartToUpdate));
    }

    partial void OnAvailableUpdateChanged(NewerReleaseDto? value)
    {
        RefreshUpdateReadiness();
        if (value is not null)
        {
            _ = DownloadUpdateInBackgroundAsync(value.Version);
        }
    }

    /// <summary>
    /// Fetches and checks the installer while the banner says "available";
    /// once it passes, the banner says "ready". A failure is only logged:
    /// the banner keeps offering the GitHub page, and the next check tries
    /// again.
    /// </summary>
    private async Task DownloadUpdateInBackgroundAsync(string version)
    {
        if (!CanInstallUpdates || version == _readyUpdateVersion || version == _downloadingUpdateVersion)
        {
            return;
        }
        _downloadingUpdateVersion = version;
        try
        {
            await _service.DownloadUpdateAsync(version);
            _readyUpdateVersion = version;
            _updateInstallFailed = false;
        }
        catch (Exception ex)
        {
            Diagnostics.CrashLogger.Write($"update: download of {version} failed", ex.Message);
        }
        finally
        {
            _downloadingUpdateVersion = null;
            RefreshUpdateReadiness();
        }
    }

    [RelayCommand]
    private async Task RestartToUpdate()
    {
        if (AvailableUpdate is not { } update || !IsUpdateReady || _isRestartingForUpdate)
        {
            return;
        }
        _isRestartingForUpdate = true;
        RefreshUpdateReadiness();
        try
        {
            // Checked again now rather than trusted from the download: this
            // path is about to be run.
            var installer = await _service.DownloadUpdateAsync(update.Version);
            var start = new ProcessStartInfo(installer) { UseShellExecute = false };
            // Progress window only, no questions; /AUTOUPDATE=1 has the
            // installer start Almatter again at the end (Almatter.iss).
            foreach (var argument in new[] { "/SILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/AUTOUPDATE=1" })
            {
                start.ArgumentList.Add(argument);
            }
            using (Process.Start(start) ?? throw new InvalidOperationException("the installer did not start"))
            {
            }
            QuitForUpdateRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Diagnostics.CrashLogger.Write($"update: installing {update.Version} failed", ex.Message);
            _updateInstallFailed = true;
            _isRestartingForUpdate = false;
            RefreshUpdateReadiness();
        }
    }
    /// <summary>Long enough after startup not to compete with loading the channels.</summary>
    private static readonly TimeSpan FirstUpdateCheckDelay = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(24);

    /// <summary>"0.14.0" — the version the project file sets, without .NET's fourth number.</summary>
    public string AppVersion { get; } = typeof(MainViewModel).Assembly.GetName().Version is { } v
        ? $"{v.Major}.{v.Minor}.{v.Build}"
        : "0.0.0";

    /// <summary>What the banner offers. Null: no banner.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUpdateBanner), nameof(UpdateBannerTitle),
        nameof(UpdateCheckStatusText), nameof(HasUpdateCheckStatus), nameof(ShowUpdateCheckLink))]
    public partial NewerReleaseDto? AvailableUpdate { get; set; }

    public bool ShowUpdateBanner => AvailableUpdate is not null;

    public string UpdateBannerTitle => AvailableUpdate is not { } update ? ""
        : IsUpdateReady ? Loc.S.UpdateReady(update.Version)
        : Loc.S.UpdateAvailable(update.Version);

    public string UpdateBannerDetail => _updateInstallFailed ? Loc.S.UpdateInstallFailed : Loc.S.UpdateYouHave(AppVersion);

    public string InstalledVersionText => Loc.S.InstalledVersion(AppVersion);

    private enum UpdateCheckOutcome { None, Checking, UpToDate, Failed, Found }

    private UpdateCheckOutcome _updateCheckOutcome;

    /// <summary>The line under "Vérifier maintenant", for a check the user asked for. Empty otherwise.</summary>
    public string UpdateCheckStatusText => _updateCheckOutcome switch
    {
        UpdateCheckOutcome.Checking => Loc.S.UpdateChecking,
        UpdateCheckOutcome.UpToDate => Loc.S.UpdateUpToDate,
        UpdateCheckOutcome.Failed => Loc.S.UpdateCheckFailed,
        UpdateCheckOutcome.Found when AvailableUpdate is { } update => Loc.S.UpdateAvailable(update.Version),
        _ => "",
    };

    public bool HasUpdateCheckStatus => UpdateCheckStatusText.Length > 0;

    /// <summary>
    /// The "View on GitHub" link next to that line, once a check the user
    /// asked for has found a newer version. Goes with the banner: skipping or
    /// closing it there takes the link away here too.
    /// </summary>
    public bool ShowUpdateCheckLink => _updateCheckOutcome == UpdateCheckOutcome.Found && AvailableUpdate is not null;

    /// <summary>The version whose banner was closed with the cross: hidden until the next launch, or until a later version.</summary>
    private string? _updateDismissedVersion;

    private bool _isCheckingForUpdate;

    /// <summary>Started with the polling loop, stopped with it on sign-out.</summary>
    private void StartUpdateCheckLoop(CancellationToken cancellationToken) =>
        Dispatcher.UIThread.Post(() => _ = RunUpdateCheckLoopAsync(cancellationToken));

    /// <summary>Runs on the UI thread; the core call itself goes off it (MattermostService).</summary>
    private async Task RunUpdateCheckLoopAsync(CancellationToken cancellationToken)
    {
        var delay = FirstUpdateCheckDelay;
        while (true)
        {
            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            delay = UpdateCheckInterval;
            // Read each time round, so switching it off stops the next check
            // without having to stop the loop.
            if (Settings.CheckForUpdates)
            {
                await CheckForUpdateAsync(askedByUser: false);
            }
        }
    }

    private async Task CheckForUpdateAsync(bool askedByUser)
    {
        if (_isCheckingForUpdate)
        {
            return;
        }
        _isCheckingForUpdate = true;
        if (askedByUser)
        {
            SetUpdateCheckOutcome(UpdateCheckOutcome.Checking);
        }

        try
        {
            var newer = await _service.CheckForUpdateAsync(AppVersion);
            if (newer is null)
            {
                AvailableUpdate = null;
                if (askedByUser)
                {
                    SetUpdateCheckOutcome(UpdateCheckOutcome.UpToDate);
                }
            }
            else if (askedByUser)
            {
                // Asked for in so many words: shown even if skipped or closed before.
                AvailableUpdate = newer;
                SetUpdateCheckOutcome(UpdateCheckOutcome.Found);
            }
            else if (newer.Version != Settings.SkippedUpdateVersion && newer.Version != _updateDismissedVersion)
            {
                AvailableUpdate = newer;
            }
        }
        catch
        {
            // GitHub unreachable or answering oddly. Unasked, that is not
            // worth interrupting anyone for: tomorrow's check will tell.
            if (askedByUser)
            {
                SetUpdateCheckOutcome(UpdateCheckOutcome.Failed);
            }
        }
        finally
        {
            _isCheckingForUpdate = false;
        }
    }

    private void SetUpdateCheckOutcome(UpdateCheckOutcome outcome)
    {
        _updateCheckOutcome = outcome;
        OnPropertyChanged(nameof(UpdateCheckStatusText));
        OnPropertyChanged(nameof(HasUpdateCheckStatus));
        OnPropertyChanged(nameof(ShowUpdateCheckLink));
    }

    /// <summary>Switched on: check straight away rather than tomorrow. Switched off: the banner goes too.</summary>
    private void OnCheckForUpdatesSettingChanged()
    {
        if (Settings.CheckForUpdates)
        {
            _ = CheckForUpdateAsync(askedByUser: false);
        }
        else
        {
            AvailableUpdate = null;
        }
    }

    /// <summary>Rewords what is on screen in the new interface language.</summary>
    private void RefreshUpdateTexts()
    {
        OnPropertyChanged(nameof(UpdateBannerTitle));
        OnPropertyChanged(nameof(UpdateBannerDetail));
        OnPropertyChanged(nameof(InstalledVersionText));
        OnPropertyChanged(nameof(UpdateCheckStatusText));
        OnPropertyChanged(nameof(UpdateRestartText));
    }

    [RelayCommand]
    private void ToggleCheckForUpdates() => Settings.CheckForUpdates = !Settings.CheckForUpdates;

    [RelayCommand]
    private Task CheckForUpdatesNow() => CheckForUpdateAsync(askedByUser: true);

    [RelayCommand]
    private void OpenUpdatePage()
    {
        if (AvailableUpdate is { } update)
        {
            OpenInBrowser(update.Url);
        }
    }

    [RelayCommand]
    private void SkipUpdate()
    {
        if (AvailableUpdate is { } update)
        {
            Settings.SkippedUpdateVersion = update.Version;
        }
        AvailableUpdate = null;
    }

    [RelayCommand]
    private void DismissUpdate()
    {
        _updateDismissedVersion = AvailableUpdate?.Version;
        AvailableUpdate = null;
    }
}
