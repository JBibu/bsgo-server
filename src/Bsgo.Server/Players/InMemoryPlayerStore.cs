using System.Collections.Concurrent;

namespace Bsgo.Server.Players;

/// <summary>
/// In-memory store. Enough to develop the protocol, but characters vanish on
/// restart: replace with Postgres before anyone plays for real.
/// </summary>
public sealed class InMemoryPlayerStore : IPlayerStore
{
    private readonly ConcurrentDictionary<uint, PlayerRecord> _players = new();

    /// <summary>
    /// Identifiers start high to stay clear of 0, which the client sends when
    /// it does not have one yet.
    /// </summary>
    private uint _nextId = 1000;

    public PlayerRecord GetOrCreate(uint playerId) =>
        _players.GetOrAdd(playerId, id => new PlayerRecord { Id = id });

    public uint AllocatePlayerId()
    {
        uint id;
        do
        {
            id = Interlocked.Increment(ref _nextId);
        }
        while (_players.ContainsKey(id));

        return id;
    }

    public bool IsNameAvailable(string name, uint requestingPlayerId)
    {
        if (!PlayerName.IsValid(name)) return false;

        // Enumerating the dictionary directly avoids the snapshot that
        // `.Values` takes, which locks every bucket and copies the whole set.
        foreach (var (id, player) in _players)
            if (id != requestingPlayerId &&
                string.Equals(player.Name, name, StringComparison.OrdinalIgnoreCase))
                return false;

        return true;
    }

    public void Save(PlayerRecord player) => _players[player.Id] = player;
}
