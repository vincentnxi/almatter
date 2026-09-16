namespace Almatter.App.Models;

public enum PresenceStatus
{
    Online,
    Away,
    DoNotDisturb,
    Offline,
}

/// <summary>
/// The single place a presence value is put into words. The status picker
/// at the bottom of the sidebar and the profile card behind a message
/// avatar say the same thing about the same state, so they read it from
/// here rather than each keeping their own copy of the wording.
/// </summary>
public static class PresenceText
{
    public static string Label(PresenceStatus status) => status switch
    {
        PresenceStatus.Online => "Disponible",
        PresenceStatus.Away => "Absent",
        PresenceStatus.DoNotDisturb => "Ne pas déranger",
        _ => "Hors ligne",
    };
}
