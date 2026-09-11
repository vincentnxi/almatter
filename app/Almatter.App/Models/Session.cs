using Almatter.App.Interop;

namespace Almatter.App.Models;

/// <summary>An authenticated connection to one Mattermost server.</summary>
public sealed record Session(string BaseUrl, string Token, UserDto User);
