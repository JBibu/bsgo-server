using Npgsql;

namespace Bsgo.Server.Tests;

/// <summary>
/// A throwaway Postgres schema, created for one test and dropped after it.
/// </summary>
/// <remarks>
/// Isolation by schema rather than by cleaning the tables: tests stay
/// independent without having to run one at a time, and nothing is left behind
/// in the developer's database when a test fails halfway.
/// </remarks>
public sealed class TestDatabase : IAsyncDisposable
{
    /// <summary>
    /// Points at the <c>db</c> service by name, which is how it resolves from
    /// inside the compose network. Override to run against another one.
    /// </summary>
    private static string ServerConnectionString =>
        Environment.GetEnvironmentVariable("BSGO_TEST_DB")
        ?? "Host=db;Database=bsgo;Username=bsgo;Password=bsgo_dev";

    private readonly string _schema;

    /// <summary>Connection string scoped to this test's schema.</summary>
    public string ConnectionString { get; }

    /// <summary>
    /// What this test's connections call themselves in <c>pg_stat_activity</c>.
    /// </summary>
    /// <remarks>
    /// Lets a test act on its own connections and no one else's — they all
    /// share a database, so "every connection" would reach into whatever is
    /// running alongside.
    /// </remarks>
    public string ApplicationName => _schema;

    private TestDatabase(string schema)
    {
        _schema = schema;
        ConnectionString = new NpgsqlConnectionStringBuilder(ServerConnectionString)
        {
            SearchPath = schema,
            ApplicationName = schema,
        }.ConnectionString;
    }

    public static async Task<TestDatabase> CreateAsync()
    {
        var database = new TestDatabase("test_" + Guid.NewGuid().ToString("n"));

        await using var connection = new NpgsqlConnection(ServerConnectionString);
        try
        {
            await connection.OpenAsync();
        }
        catch (NpgsqlException e)
        {
            throw new InvalidOperationException(
                $"no database at \"{ServerConnectionString}\". These tests need one: "
                + "`docker compose up -d db`, or point BSGO_TEST_DB elsewhere.", e);
        }

        await using var create = new NpgsqlCommand($"create schema \"{database._schema}\"", connection);
        await create.ExecuteNonQueryAsync();
        return database;
    }

    public async ValueTask DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(ServerConnectionString);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand(
            $"drop schema if exists \"{_schema}\" cascade", connection);
        await drop.ExecuteNonQueryAsync();
    }
}
