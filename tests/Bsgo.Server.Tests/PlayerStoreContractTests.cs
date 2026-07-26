using Bsgo.Protocol;
using Bsgo.Server.Players;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Bsgo.Server.Tests;

/// <summary>
/// What a character store has to do, whichever one it is.
/// </summary>
/// <remarks>
/// Written once and run against both implementations. The in-memory one is what
/// every other test runs on, so if only it were checked, the store the real
/// server uses would be the only untested part of the path.
/// </remarks>
public abstract class PlayerStoreContract : IAsyncLifetime
{
    protected IPlayerStore Store = null!;

    public abstract Task InitializeAsync();
    public virtual Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_new_player_starts_with_no_faction_and_no_name()
    {
        var player = await Store.GetOrCreateAsync(42);

        Assert.Equal(42u, player.Id);
        Assert.Equal(Faction.Neutral, player.Faction);
        Assert.Empty(player.Name);
        Assert.False(player.IsCreated);
    }

    [Fact]
    public async Task A_character_counts_as_created_only_with_faction_and_name()
    {
        var player = await Store.GetOrCreateAsync(1);

        player.Faction = Faction.Colonial;
        Assert.False(player.IsCreated);   // still unnamed

        player.Name = "Starbuck";
        Assert.True(player.IsCreated);
    }

    [Fact]
    public async Task What_was_saved_is_what_comes_back()
    {
        var player = await Store.GetOrCreateAsync(1);
        player.Name = "Starbuck";
        player.Faction = Faction.Colonial;
        await Store.SaveAsync(player);

        var reread = await Store.GetOrCreateAsync(1);
        Assert.Equal("Starbuck", reread.Name);
        Assert.Equal(Faction.Colonial, reread.Faction);
    }

    [Fact]
    public async Task A_name_taken_by_someone_else_is_not_available()
    {
        var first = await Store.GetOrCreateAsync(1);
        first.Name = "Starbuck";
        await Store.SaveAsync(first);

        Assert.False(await Store.IsNameAvailableAsync("Starbuck", requestingPlayerId: 2));
        Assert.False(await Store.IsNameAvailableAsync("starbuck", requestingPlayerId: 2));   // case-insensitive
        Assert.True(await Store.IsNameAvailableAsync("Apollo", requestingPlayerId: 2));
    }

    [Fact]
    public async Task Your_own_name_stays_available_to_you()
    {
        // Otherwise re-sending the same name on confirmation would be rejected.
        var player = await Store.GetOrCreateAsync(1);
        player.Name = "Starbuck";
        await Store.SaveAsync(player);

        Assert.True(await Store.IsNameAvailableAsync("Starbuck", requestingPlayerId: 1));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_name_is_never_available(string name) =>
        Assert.False(await Store.IsNameAvailableAsync(name, requestingPlayerId: 1));

    [Fact]
    public async Task An_invalid_name_is_not_available_either()
    {
        // Without this, validation could be bypassed by going straight to naming.
        Assert.False(await Store.IsNameAvailableAsync("ab", requestingPlayerId: 1));
        Assert.False(await Store.IsNameAvailableAsync("Star buck", requestingPlayerId: 1));
        Assert.True(await Store.IsNameAvailableAsync("Starbuck", requestingPlayerId: 1));
    }

    [Fact]
    public async Task Opaque_blobs_are_stored_unmodified()
    {
        // Only the client interprets settings and avatar: they must come back identical.
        var player = await Store.GetOrCreateAsync(1);
        var blob = new byte[] { 0x00, 0xFF, 0x42, 0x00 };

        player.Settings = blob;
        player.AvatarDescription = blob;
        player.KeyBindings = [];
        await Store.SaveAsync(player);

        var reread = await Store.GetOrCreateAsync(1);
        Assert.Equal(blob, reread.Settings);
        Assert.Equal(blob, reread.AvatarDescription);
        Assert.Empty(reread.KeyBindings);
    }

    [Fact]
    public async Task Assigned_identifiers_never_repeat()
    {
        var ids = new List<uint>();
        for (int i = 0; i < 100; i++)
            ids.Add(await Store.AllocatePlayerIdAsync());

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public async Task Zero_is_never_assigned()
    {
        // 0 is the "I have no identifier yet" signal: assigning it would make
        // every player share one character, and the client would store the 0
        // and send it back on the next start.
        for (int i = 0; i < 50; i++)
            Assert.NotEqual(0u, await Store.AllocatePlayerIdAsync());
    }

    [Fact]
    public async Task An_assigned_identifier_never_clobbers_an_existing_character()
    {
        var first = await Store.AllocatePlayerIdAsync();

        var player = await Store.GetOrCreateAsync(first);
        player.Name = "Starbuck";
        await Store.SaveAsync(player);

        var second = await Store.AllocatePlayerIdAsync();

        Assert.NotEqual(first, second);
        Assert.Equal("Starbuck", (await Store.GetOrCreateAsync(first)).Name);
        Assert.Empty((await Store.GetOrCreateAsync(second)).Name);
    }

    [Fact]
    public async Task An_identifier_the_client_invented_is_never_handed_out_again()
    {
        // The client introduces itself with whatever identifier it remembers, so
        // one it made up can already be in the store when the counter reaches
        // it. Handing it out would give the newcomer somebody else's character.
        var taken = await Store.GetOrCreateAsync(PlayerId.First);
        taken.Name = "Starbuck";
        await Store.SaveAsync(taken);

        var assigned = await Store.AllocatePlayerIdAsync();

        Assert.NotEqual(taken.Id, assigned);
        Assert.Empty((await Store.GetOrCreateAsync(assigned)).Name);
        Assert.Equal("Starbuck", (await Store.GetOrCreateAsync(taken.Id)).Name);
    }

    [Fact]
    public async Task Two_players_keep_separate_characters()
    {
        var a = await Store.GetOrCreateAsync(await Store.AllocatePlayerIdAsync());
        a.Name = "Starbuck";
        a.Faction = Faction.Colonial;
        await Store.SaveAsync(a);

        var b = await Store.GetOrCreateAsync(await Store.AllocatePlayerIdAsync());
        b.Name = "Apollo";
        b.Faction = Faction.Cylon;
        await Store.SaveAsync(b);

        Assert.Equal("Starbuck", (await Store.GetOrCreateAsync(a.Id)).Name);
        Assert.Equal(Faction.Colonial, (await Store.GetOrCreateAsync(a.Id)).Faction);
        Assert.Equal("Apollo", (await Store.GetOrCreateAsync(b.Id)).Name);
        Assert.Equal(Faction.Cylon, (await Store.GetOrCreateAsync(b.Id)).Faction);
    }
}

public sealed class InMemoryPlayerStoreTests : PlayerStoreContract
{
    public override Task InitializeAsync()
    {
        Store = new InMemoryPlayerStore();
        return Task.CompletedTask;
    }
}

/// <summary>
/// The same contract against a real Postgres.
/// </summary>
/// <remarks>
/// Each test gets its own schema, created and dropped around it, so they stay
/// independent and can run in parallel against one database. The tables come
/// from <see cref="PlayerSchema"/> itself rather than a copy of the DDL: a copy
/// would drift, and these tests would then be passing against a schema the
/// server never creates.
/// </remarks>
public sealed class PostgresPlayerStoreTests : PlayerStoreContract
{
    private TestDatabase? _database;
    private NpgsqlDataSource? _source;

    public override async Task InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        _source = new NpgsqlDataSourceBuilder(_database.ConnectionString).Build();

        await new PlayerSchema(_source, NullLogger<PlayerSchema>.Instance)
            .StartAsync(CancellationToken.None);

        Store = new PostgresPlayerStore(_source, NullLogger<PostgresPlayerStore>.Instance);
    }

    public override async Task DisposeAsync()
    {
        if (_source is not null) await _source.DisposeAsync();
        if (_database is not null) await _database.DisposeAsync();
    }

    [Fact]
    public async Task A_connection_dropped_underneath_is_retried_instead_of_thrown()
    {
        // The store is reached from the loop serving the player's socket, so an
        // exception escaping it does not fail one query — it disconnects the
        // player, who cannot tell a database hiccup from a crash. A pooled
        // connection closed from the other end is the everyday version of that:
        // it is what a database restart leaves behind.
        await Store.GetOrCreateAsync(1);   // opens and pools a connection

        await using (var admin = new NpgsqlConnection(_database!.ConnectionString))
        {
            await admin.OpenAsync();
            await using var kill = new NpgsqlCommand(
                """
                select pg_terminate_backend(pid) from pg_stat_activity
                 where application_name = $1 and pid <> pg_backend_pid()
                """, admin);
            kill.Parameters.AddWithValue(_database.ApplicationName);
            await kill.ExecuteNonQueryAsync();
        }

        var player = await Store.GetOrCreateAsync(1);
        Assert.Equal(1u, player.Id);
    }
}
