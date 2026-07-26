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
/// This creates what is missing; it does not change what is there. Against a
/// database that already holds the table, adding a column here does nothing at
/// all — no error, and the server then reads a column that is not present.
/// Accounts, the next thing the project needs, most likely means a new column
/// on <c>players</c> rather than a new table, and that is the point at which
/// this file stops being enough and wants versioned migrations.
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
            created_at         timestamptz not null default now(),
            updated_at         timestamptz not null default now()
        );

        -- Names are compared without case, and unnamed characters are exempt:
        -- every character starts with an empty name and there may be many.
        create unique index if not exists players_name_key
            on players (lower(name)) where name <> '';

        create sequence if not exists player_ids as bigint start with {PlayerId.First};
        """;

    public async Task StartAsync(CancellationToken ct)
    {
        await using var command = source.CreateCommand(Ddl);
        await command.ExecuteNonQueryAsync(ct);
        logger.LogInformation("Database schema ready");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
