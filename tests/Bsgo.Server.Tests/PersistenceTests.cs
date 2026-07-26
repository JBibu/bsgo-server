using Bsgo.Protocol;
using Bsgo.Server.Players;
using Microsoft.Extensions.DependencyInjection;

namespace Bsgo.Server.Tests;

/// <summary>
/// A character created against a real database has to still be there after the
/// server is restarted.
/// </summary>
/// <remarks>
/// The store is checked on its own in <see cref="PlayerStoreContract"/>. What is
/// checked here is the whole path: a client's messages arriving over a socket,
/// reaching Postgres through the handlers, and being found again by a server
/// process that shares nothing with the first one but the database. A handler
/// that reads and mutates the character but forgets to save it passes every
/// other test in the suite and fails this one.
/// </remarks>
public class PersistenceTests
{
    private static BgoWriter Text(string value)
    {
        var w = new BgoWriter();
        w.Write(value);
        return w;
    }

    [Fact]
    public async Task A_character_survives_a_restart()
    {
        await using var database = await TestDatabase.CreateAsync();

        uint playerId;
        var avatar = new AvatarDescription
        {
            Items = new Dictionary<AvatarItem, string>
            {
                [AvatarItem.Race] = "cylon",
                [AvatarItem.Sex] = "centurion",
            },
        };

        // First run: create the character the way the client does.
        await using (var server = await TestServer.StartAsync(connectionString: database.ConnectionString))
        {
            using var client = await server.ConnectAsync();
            playerId = await client.LogInAsync();

            var faction = new BgoWriter(1);
            faction.Write((byte)Faction.Cylon);
            await client.SendAsync(ProtocolId.Player, (ushort)PlayerRequest.SelectFaction, faction);
            await client.ReadUntilAsync(ProtocolId.Scene, (ushort)SceneReply.LoadNextScene);

            await client.SendAsync(ProtocolId.Player, (ushort)PlayerRequest.ChooseName, Text("Boomer"));
            await client.ReadUntilAsync(ProtocolId.Player, (ushort)PlayerReply.Name);

            var payload = new BgoWriter();
            avatar.Write(payload);
            await client.SendAsync(ProtocolId.Player, (ushort)PlayerRequest.CreateAvatar, payload);
            await client.ReadUntilAsync(ProtocolId.Player, (ushort)PlayerReply.Avatar);
        }

        // Second run: a different process, only the database in common.
        await using (var server = await TestServer.StartAsync(connectionString: database.ConnectionString))
        {
            var store = server.Services.GetRequiredService<IPlayerStore>();
            var player = await store.GetOrCreateAsync(playerId);

            Assert.True(player.IsCreated);
            Assert.Equal("Boomer", player.Name);
            Assert.Equal(Faction.Cylon, player.Faction);
            Assert.Equal(avatar.ToBytes(), player.AvatarDescription);
        }
    }

    [Fact]
    public async Task A_name_stays_taken_after_a_restart()
    {
        await using var database = await TestDatabase.CreateAsync();

        await using (var server = await TestServer.StartAsync(connectionString: database.ConnectionString))
        {
            using var client = await server.ConnectAsync();
            await client.LogInAsync();

            await client.SendAsync(ProtocolId.Player, (ushort)PlayerRequest.ChooseName, Text("Boomer"));
            await client.ReadUntilAsync(ProtocolId.Player, (ushort)PlayerReply.Name);
        }

        await using (var server = await TestServer.StartAsync(connectionString: database.ConnectionString))
        {
            using var client = await server.ConnectAsync();
            await client.LogInAsync();

            // Someone else asking for the same name must be turned away.
            await client.SendAsync(
                ProtocolId.Player, (ushort)PlayerRequest.CheckNameAvailability, Text("Boomer"));

            var reply = await client.ReadUntilAsync(
                ProtocolId.Player, (ushort)PlayerReply.NameNotAvailable);
            Assert.Empty(reply.Payload);
        }
    }

    [Fact]
    public async Task Identifiers_carry_on_where_they_left_off_after_a_restart()
    {
        // Restarting used to hand the next arrival an identifier that was
        // already taken, which is the same failure as handing back the 0: two
        // players on one character.
        await using var database = await TestDatabase.CreateAsync();

        uint first, second;

        await using (var server = await TestServer.StartAsync(connectionString: database.ConnectionString))
        {
            using var client = await server.ConnectAsync();
            first = await client.LogInAsync();
        }

        await using (var server = await TestServer.StartAsync(connectionString: database.ConnectionString))
        {
            using var client = await server.ConnectAsync();
            second = await client.LogInAsync();
        }

        Assert.Equal(PlayerId.First, first);
        Assert.NotEqual(first, second);
    }
}
