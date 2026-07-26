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

/// <summary>Faction a character belongs to.</summary>
public enum Faction : byte
{
    Neutral = 0,
    Colonial = 1,
    Cylon = 2,
    Ancient = 3,
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
/// The current implementation is in-memory: characters are lost when the
/// server restarts. This interface exists so that swapping it for Postgres
/// does not force any change to the protocol handlers.
/// </remarks>
public interface IPlayerStore
{
    /// <summary>Fetches the character, creating an empty one on first use.</summary>
    PlayerRecord GetOrCreate(uint playerId);

    /// <summary>
    /// Reserves a fresh identifier, unused by any character.
    /// </summary>
    /// <remarks>
    /// Needed because the client may arrive without one: it remembers the id
    /// from a previous session and has none the first time. The server assigns
    /// one and the client keeps it from then on.
    /// </remarks>
    uint AllocatePlayerId();

    /// <summary>Whether the name is free (not already taken by another character).</summary>
    bool IsNameAvailable(string name, uint requestingPlayerId);

    void Save(PlayerRecord player);
}
