using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Bsgo.Server.Players;

/// <summary>
/// Creates the tables the server needs, if they are not there yet.
/// </summary>
/// <remarks>
/// Registered as a hosted service <b>before</b> the listener, so no client can
/// connect to a server whose schema does not exist yet. Every statement is
/// written to be safe to re-run: the server applies this on every start.
/// <para>
/// This creates what is missing; it does not change what is there. A column
/// added to an existing table has to say so itself, in the <c>alter table</c>
/// block below — <c>create table if not exists</c> does nothing at all to a
/// table that is already there, with no error to show for it.
/// </para>
/// <para>
/// That block is the whole migration story, and it is one statement long. Every
/// column added this way costs an edit in four places: the create list, the
/// alter block, <c>PostgresPlayerStore.Columns</c>, and the insert. That is
/// affordable for one. When the second or third arrives — accounts will bring
/// some — this is where a versioned migration mechanism belongs instead.
/// </para>
/// </remarks>
public sealed class PlayerSchema(
    NpgsqlDataSource source,
    ILogger<PlayerSchema> logger) : IHostedService
{
    private static readonly string Ddl = $"""
        create table if not exists players (
            id                 bigint      primary key,
            name               text        not null default '',
            faction            smallint    not null default 0,
            avatar_description bytea       not null default '',
            settings           bytea       not null default '',
            key_bindings       bytea       not null default '',
            ship_card_guid     bigint      not null default 0,
            created_at         timestamptz not null default now(),
            updated_at         timestamptz not null default now()
        );

        -- Names are compared without case, and unnamed characters are exempt:
        -- every character starts with an empty name and there may be many.
        create unique index if not exists players_name_key
            on players (lower(name)) where name <> '';

        create sequence if not exists player_ids as bigint start with {PlayerId.First};

        -- Columns added after the table first existed. `create table if not
        -- exists` does nothing to a table that is already there, so a column
        -- introduced later has to say so on its own or it simply never appears
        -- on anyone's database but a fresh one.
        alter table players add column if not exists ship_card_guid bigint not null default 0;
        """;

    public async Task StartAsync(CancellationToken ct)
    {
        await using var command = source.CreateCommand(Ddl);
        await command.ExecuteNonQueryAsync(ct);
        logger.LogInformation("Database schema ready");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
