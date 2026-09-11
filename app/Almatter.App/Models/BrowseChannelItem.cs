using CommunityToolkit.Mvvm.ComponentModel;

namespace Almatter.App.Models;

/// <summary>One public channel offered in the "browse channels" panel — a channel this user has not joined.</summary>
public sealed partial class BrowseChannelItem : ObservableObject
{
    public required string Id { get; init; }

    /// <summary>The human name, e.g. "Veille alertes sujets".</summary>
    public required string DisplayName { get; init; }

    /// <summary>The URL slug, e.g. "veille-alertes". Shown as a secondary line so two similarly-named channels can be told apart.</summary>
    public required string Name { get; init; }

    /// <summary>The channel's own description. Frequently empty, hence HasPurpose.</summary>
    public required string Purpose { get; init; }

    public bool HasPurpose => !string.IsNullOrWhiteSpace(Purpose);

    /// <summary>True while the join request is in flight — the row's button shows "…" and stops accepting clicks, so an impatient double-click can't fire two joins.</summary>
    [ObservableProperty]
    public partial bool IsJoining { get; set; }

    public string JoinLabel => IsJoining ? "…" : "Rejoindre";

    partial void OnIsJoiningChanged(bool value) => OnPropertyChanged(nameof(JoinLabel));
}
