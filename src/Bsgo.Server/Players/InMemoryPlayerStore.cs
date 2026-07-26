using System.Collections.Concurrent;

namespace Bsgo.Server.Players;

/// <summary>
/// In-memory store. Characters vanish on restart, so it is what the tests run
/// against; the server uses <see cref="PostgresPlayerStore"/>.
/// </summary>
public sealed class InMemoryPlayerStore : IPlayerStore
{
    private readonly ConcurrentDictionary<uint, PlayerRecord> _players = new();

    /// <summary>Incremented before use, so the first one handed out is <see cref="PlayerId.First"/>.</summary>
    private uint _nextId = PlayerId.First - 1;

    public Task<PlayerRecord> GetOrCreateAsync(uint playerId, CancellationToken ct = default) =>
        Task.FromResult(_players.GetOrAdd(playerId, id => new PlayerRecord { Id = id }));

    public Task<uint> AllocatePlayerIdAsync(CancellationToken ct = default)
    {
        uint id;
        do
        {
            id = Interlocked.Increment(ref _nextId);
        }
        while (_players.ContainsKey(id));

        return Task.FromResult(id);
    }

    public Task<bool> IsNameTakenAsync(string name, uint requestingPlayerId, CancellationToken ct = default)
    {
        // Enumerating the dictionary directly avoids the snapshot that
        // `.Values` takes, which locks every bucket and copies the whole set.
        foreach (var (id, player) in _players)
            if (id != requestingPlayerId &&
                string.Equals(player.Name, name, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(true);

        return Task.FromResult(false);
    }

    public Task SaveAsync(PlayerRecord player, CancellationToken ct = default)
    {
        _players[player.Id] = player;
        return Task.CompletedTask;
    }
}
