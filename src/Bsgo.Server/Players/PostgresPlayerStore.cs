using Bsgo.Protocol;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Bsgo.Server.Players;

/// <summary>
/// Character store backed by Postgres. Characters survive a restart.
/// </summary>
/// <remarks>
/// Written against Npgsql directly rather than through an ORM: there is one
/// table and five columns of it are opaque blobs, so there is nothing to map.
/// <para>
/// Identifiers are <c>uint</c> on the wire and <c>bigint</c> in the table.
/// Postgres has no unsigned integers, and <c>int</c> would overflow on the
/// upper half of the range — the client is free to send any of it.
/// </para>
/// </remarks>
public sealed class PostgresPlayerStore(
    NpgsqlDataSource source,
    ILogger<PostgresPlayerStore> logger) : IPlayerStore
{
    /// <summary>
    /// The columns a <see cref="PlayerRecord"/> is built from, named once.
    /// </summary>
    /// <remarks>
    /// Spelling the list out in each statement invites the same failure the
    /// wire format has: nothing complains when the order shifts, the fields
    /// just quietly land in the wrong properties. <see cref="Read"/> looks them
    /// up by name for the same reason.
    /// </remarks>
    private const string Columns = "id, name, faction, avatar_description, settings, key_bindings, ship_card_guid";

    /// <summary>How many times a momentary database failure is retried.</summary>
    /// <remarks>
    /// Small on purpose. This is not a substitute for the database being up: it
    /// covers the blink of a restart or a dropped pooled connection. What is
    /// behind it is that the store is reached from the loop serving the
    /// player's socket, so an exception escaping here does not fail one query,
    /// it disconnects the player — and from their side a database hiccup and a
    /// crash look identical.
    /// </remarks>
    private const int Attempts = 3;

    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Runs a query, giving a database that is merely stumbling a second
    /// chance.
    /// </summary>
    /// <remarks>
    /// Only failures Npgsql itself calls transient are retried — a broken
    /// connection or a timeout. A constraint violation or a syntax error is a
    /// bug and repeating it would only hide it.
    /// </remarks>
    private async Task<T> WithRetryAsync<T>(Func<CancellationToken, Task<T>> query, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await query(ct);
            }
            catch (NpgsqlException e) when (e.IsTransient && attempt < Attempts)
            {
                logger.LogWarning(
                    "Database unavailable ({Message}); retrying, attempt {Attempt} of {Attempts}",
                    e.Message, attempt + 1, Attempts);
                await Task.Delay(RetryDelay, ct);
            }
        }
    }

    /// <summary>For queries with nothing to return.</summary>
    private Task WithRetryAsync(Func<CancellationToken, Task> query, CancellationToken ct) =>
        WithRetryAsync<object?>(async token => { await query(token); return null; }, ct);

    /// <summary>
    /// Reads the character, inserting a blank one the first time.
    /// </summary>
    /// <remarks>
    /// One statement rather than a select followed by an insert: two
    /// connections racing on the same new identifier would otherwise have one
    /// of them fail on the primary key. Here the loser inserts nothing and
    /// reads what the winner wrote.
    /// </remarks>
    public Task<PlayerRecord> GetOrCreateAsync(uint playerId, CancellationToken ct = default)
    {
        const string sql = $"""
            with created as (
                insert into players (id) values ($1)
                on conflict (id) do nothing
                returning {Columns}
            )
            select * from created
            union all
            select {Columns} from players where id = $1
            limit 1
            """;

        return WithRetryAsync(async token =>
        {
            await using var command = source.CreateCommand(sql);
            command.Parameters.AddWithValue((long)playerId);

            await using var reader = await command.ExecuteReaderAsync(token);
            await reader.ReadAsync(token);
            return Read(reader);
        }, ct);
    }

    /// <summary>
    /// Takes the next identifier and claims it in the same breath.
    /// </summary>
    /// <remarks>
    /// The sequence alone is not enough: the client may introduce itself with
    /// any identifier it likes, so one it invented can already be sitting in
    /// the table when the sequence reaches it. Inserting the row is what
    /// reserves it — if the row is already there, nothing is inserted and the
    /// loop moves on to the next number rather than handing out somebody
    /// else's character.
    /// </remarks>
    public Task<uint> AllocatePlayerIdAsync(CancellationToken ct = default)
    {
        const string sql = """
            insert into players (id) values (nextval('player_ids'))
            on conflict (id) do nothing
            returning id
            """;

        return WithRetryAsync(async token =>
        {
            await using var command = source.CreateCommand(sql);

            while (true)
            {
                token.ThrowIfCancellationRequested();

                if (await command.ExecuteScalarAsync(token) is long id)
                    return (uint)id;
            }
        }, ct);
    }

    public Task<bool> IsNameTakenAsync(
        string name, uint requestingPlayerId, CancellationToken ct = default)
    {
        const string sql = """
            select exists (
                select 1 from players where lower(name) = lower($1) and id <> $2
            )
            """;

        return WithRetryAsync(async token =>
        {
            await using var command = source.CreateCommand(sql);
            command.Parameters.AddWithValue(name);
            command.Parameters.AddWithValue((long)requestingPlayerId);

            return (bool)(await command.ExecuteScalarAsync(token))!;
        }, ct);
    }

    public Task SaveAsync(PlayerRecord player, CancellationToken ct = default)
    {
        const string sql = $"""
            insert into players ({Columns})
            values ($1, $2, $3, $4, $5, $6, $7)
            on conflict (id) do update set
                name               = excluded.name,
                faction            = excluded.faction,
                avatar_description = excluded.avatar_description,
                settings           = excluded.settings,
                key_bindings       = excluded.key_bindings,
                ship_card_guid     = excluded.ship_card_guid,
                updated_at         = now()
            """;

        return WithRetryAsync(async token =>
        {
            await using var command = source.CreateCommand(sql);
            command.Parameters.AddWithValue((long)player.Id);
            command.Parameters.AddWithValue(player.Name);
            command.Parameters.AddWithValue((short)player.Faction);
            command.Parameters.AddWithValue(player.AvatarDescription);
            command.Parameters.AddWithValue(player.Settings);
            command.Parameters.AddWithValue(player.KeyBindings);
            command.Parameters.AddWithValue((long)player.ShipCardGuid);

            await command.ExecuteNonQueryAsync(token);
        }, ct);
    }

    private static PlayerRecord Read(NpgsqlDataReader reader) => new()
    {
        Id = (uint)reader.GetInt64(reader.GetOrdinal("id")),
        Name = reader.GetString(reader.GetOrdinal("name")),
        Faction = (Faction)reader.GetInt16(reader.GetOrdinal("faction")),
        AvatarDescription = reader.GetFieldValue<byte[]>(reader.GetOrdinal("avatar_description")),
        Settings = reader.GetFieldValue<byte[]>(reader.GetOrdinal("settings")),
        KeyBindings = reader.GetFieldValue<byte[]>(reader.GetOrdinal("key_bindings")),
        ShipCardGuid = (uint)reader.GetInt64(reader.GetOrdinal("ship_card_guid")),
    };
}
