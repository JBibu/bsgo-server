using Bsgo.Server.Players;

namespace Bsgo.Server.Net;

/// <summary>
/// Pushes data to a client right after it authenticates.
/// </summary>
/// <remarks>
/// The client expects several things to be waiting for it on entry (its
/// identifier, the avatar catalogue, saved settings) without asking for any of
/// them. Without this, the login handler would have to depend on each concrete
/// handler that owns one of those messages, and every new one would add a
/// constructor parameter plus a second registration in the container.
/// </remarks>
public interface IPlayerEnteredHook
{
    /// <summary>
    /// Relative order, lowest first. It matters: the client builds a default
    /// avatar from the catalogue when the faction reply arrives, so the
    /// catalogue has to be on the wire before the player can pick one.
    /// </summary>
    int Order => 100;

    /// <param name="player">
    /// The character entering, read once by the login handler and handed to
    /// every hook. Fetching it here rather than in each hook keeps a login from
    /// asking the database for the same row once per hook that happens to need
    /// it.
    /// </param>
    Task OnPlayerEnteredAsync(BgoConnection connection, PlayerRecord player, CancellationToken ct);
}
