using Bsgo.Protocol;
namespace Bsgo.Server.Players;

/// <summary>Rules for character names.</summary>
public static class PlayerName
{
    public const int MinLength = 3;
    public const int MaxLength = 16;

    /// <summary>
    /// Checks the name is acceptable: sane length, and only letters, digits,
    /// hyphen and underscore.
    /// </summary>
    /// <remarks>
    /// Validated on the server and not just in the UI: the client can send
    /// anything, and the name ends up shown to other players.
    /// </remarks>
    public static bool IsValid(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (name.Length is < MinLength or > MaxLength) return false;

        foreach (var c in name)
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
                return false;

        return true;
    }
}

/// <summary>Rules for character identifiers.</summary>
public static class PlayerId
{
    /// <summary>
    /// The first identifier ever handed out.
    /// </summary>
    /// <remarks>
    /// Starts high to stay clear of 0, which is what the client sends when it
    /// has none yet. Handing that 0 back made every player share one character,
    /// and the mistake fed itself: the client stored the 0 and sent it again on
    /// the next start.
    /// </remarks>
    public const uint First = 1001;
}

/// <summary>Persistent state of a character.</summary>
public sealed class PlayerRecord
{
    public uint Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Faction Faction { get; set; } = Faction.Neutral;

    /// <summary>
    /// The avatar description exactly as the client sends it. An opaque blob:
    /// only the client interprets its contents, so the server stores and
    /// returns it untouched.
    /// </summary>
    public byte[] AvatarDescription { get; set; } = [];

    /// <summary>Game settings, also opaque (see <see cref="AvatarDescription"/>).</summary>
    public byte[] Settings { get; set; } = [];

    /// <summary>Key bindings, also opaque.</summary>
    public byte[] KeyBindings { get; set; } = [];

    /// <summary>A character without faction or name has not been created yet.</summary>
    public bool IsCreated => Faction != Faction.Neutral && Name.Length > 0;
}

/// <summary>
/// Character store.
/// </summary>
/// <remarks>
/// Two implementations: <see cref="InMemoryPlayerStore"/>, which the tests use
/// and which loses everything on restart, and <see cref="PostgresPlayerStore"/>,
/// which the server uses. No protocol handler knows which one it holds.
/// <para>
/// Asynchronous because the real one talks to a database over a socket, and it
/// is reached from the loop that serves the client's connection. A blocking
/// call there stalls a thread pool thread on every login.
/// </para>
/// </remarks>
public interface IPlayerStore
{
    /// <summary>Fetches the character, creating an empty one on first use.</summary>
    Task<PlayerRecord> GetOrCreateAsync(uint playerId, CancellationToken ct = default);

    /// <summary>
    /// Reserves a fresh identifier, unused by any character.
    /// </summary>
    /// <remarks>
    /// Needed because the client may arrive without one: it remembers the id
    /// from a previous session and has none the first time. The server assigns
    /// one and the client keeps it from then on.
    /// </remarks>
    Task<uint> AllocatePlayerIdAsync(CancellationToken ct = default);

    /// <summary>Whether another character already goes by this name.</summary>
    Task<bool> IsNameTakenAsync(string name, uint requestingPlayerId, CancellationToken ct = default);

    Task SaveAsync(PlayerRecord player, CancellationToken ct = default);
}

public static class PlayerStoreExtensions
{
    /// <summary>Whether a character may be given this name.</summary>
    /// <remarks>
    /// Two separate questions, joined here rather than inside each store:
    /// whether the name is allowed at all, which is no business of storage, and
    /// whether somebody already holds it, which is. Left to the implementations
    /// it had to be written twice, and a third one would have to remember it
    /// unprompted — with the failure staying quiet, because an unusable name
    /// would simply be reported as free.
    /// </remarks>
    public static async Task<bool> IsNameAvailableAsync(
        this IPlayerStore store, string name, uint requestingPlayerId, CancellationToken ct = default) =>
        PlayerName.IsValid(name)
        && !await store.IsNameTakenAsync(name, requestingPlayerId, ct);
}
