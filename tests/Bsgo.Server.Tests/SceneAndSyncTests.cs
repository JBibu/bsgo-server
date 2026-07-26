using Bsgo.Protocol;
using Bsgo.Server.Players;
using Microsoft.Extensions.DependencyInjection;

namespace Bsgo.Server.Tests;

/// <summary>
/// Picks up where login ends: clock synchronisation and the transition into
/// the character creation screen.
/// </summary>
public class SceneAndSyncTests
{
    [Fact]
    public async Task Sync_replies_with_the_server_time_in_unix_milliseconds()
    {
        await using var server = await TestServer.StartAsync();
        using var client = await server.ConnectAsync();
        await client.ReadAsync();   // Hello

        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await client.SendAsync(ProtocolId.Sync, (ushort)SyncRequest.SyncRequest);

        var reply = await client.ReadUntilAsync(ProtocolId.Sync, (ushort)SyncReply.SyncReply);
        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        long serverTime = new BgoReader(reply.Payload).ReadInt64();

        // It must be the real time, not an arbitrary counter: the client turns
        // it into a date by adding it to the Unix epoch.
        Assert.InRange(serverTime, before, after);
    }

    [Fact]
    public async Task Leaving_the_login_sends_the_player_to_character_creation()
    {
        await using var server = await TestServer.StartAsync();
        using var client = await server.ConnectAsync();
        await client.ReadAsync();   // Hello

        await client.SendAsync(ProtocolId.Scene, (ushort)SceneRequest.QuitLogin);

        var scene = await client.ReadUntilAsync(ProtocolId.Scene, (ushort)SceneReply.LoadNextScene);

        var r = new BgoReader(scene.Payload);
        Assert.Equal((byte)TransSceneType.None, r.ReadByte());
        Assert.Equal((byte)GameLocation.Starter, r.ReadByte());

        // Starter carries the two bonus ships; without them the client would
        // read garbage from the next message.
        Assert.Equal(0u, r.ReadUInt32());
        Assert.Equal(0u, r.ReadUInt32());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public async Task The_full_startup_sequence_leaves_no_stray_bytes()
    {
        await using var server = await TestServer.StartAsync();
        using var client = await server.ConnectAsync();

        await client.ReadAsync();   // Hello
        await client.SendAsync(ProtocolId.Login, (ushort)LoginRequest.Init);
        await client.ReadUntilAsync(ProtocolId.Login, (ushort)LoginReply.Init);

        var credentials = new BgoWriter();
        credentials.Write((byte)ConnectType.Web);
        credentials.Write(5085935u);
        credentials.Write("Starbuck");
        credentials.Write(new string('0', 64));
        await client.SendAsync(ProtocolId.Login, (ushort)LoginRequest.Player, credentials);
        await client.ReadUntilAsync(ProtocolId.Login, (ushort)LoginReply.Player);

        // Just as the real client does after authenticating.
        await client.SendAsync(ProtocolId.Sync, (ushort)SyncRequest.SyncRequest);
        await client.ReadUntilAsync(ProtocolId.Sync, (ushort)SyncReply.SyncReply);

        await client.SendAsync(ProtocolId.Scene, (ushort)SceneRequest.QuitLogin);
        await client.ReadUntilAsync(ProtocolId.Scene, (ushort)SceneReply.LoadNextScene);

        // The client confirms the load; the server must not reply.
        await client.SendAsync(ProtocolId.Scene, (ushort)SceneRequest.SceneLoaded);
        await Task.Delay(150);
        Assert.Equal(0, client.Available);
    }

    /// <summary>Gives the server a character that already exists.</summary>
    private static async Task<PlayerRecord> ExistingCharacterAsync(TestServer server)
    {
        var store = server.Services.GetRequiredService<IPlayerStore>();

        var player = await store.GetOrCreateAsync(4242);
        player.Faction = Faction.Colonial;
        player.Name = "Starbuck";
        await store.SaveAsync(player);

        return player;
    }

    private static async Task<GameLocation> WhereTheyAreSentAsync(TestServer server, PlayerRecord player)
    {
        using var client = await server.ConnectAsync();
        await client.LogInAsync(player.Id);

        await client.SendAsync(ProtocolId.Scene, (ushort)SceneRequest.QuitLogin);
        var scene = await client.ReadUntilAsync(ProtocolId.Scene, (ushort)SceneReply.LoadNextScene);

        var r = new BgoReader(scene.Payload);
        r.ReadByte();   // transition animation
        return (GameLocation)r.ReadByte();
    }

    [Fact]
    public async Task A_player_who_already_has_a_character_resumes_instead_of_making_another()
    {
        await using var server = await TestServer.StartAsync(o => o.EnableRoomEntry = true);
        var player = await ExistingCharacterAsync(server);

        Assert.Equal(GameLocation.Room, await WhereTheyAreSentAsync(server, player));
    }

    [Fact]
    public async Task A_player_with_a_character_and_nowhere_to_go_is_still_sent_somewhere()
    {
        // Room entry stays shut until there are ships. A character that exists
        // but cannot be resumed must not leave the client on "Please wait",
        // which is what sending nothing would do — the client never moves on by
        // itself. Character creation is a poor answer, and it is an answer.
        await using var server = await TestServer.StartAsync(o => o.EnableRoomEntry = false);
        var player = await ExistingCharacterAsync(server);

        Assert.Equal(GameLocation.Starter, await WhereTheyAreSentAsync(server, player));
    }

    [Fact]
    public async Task A_half_created_character_still_goes_to_character_creation()
    {
        // A faction on its own is not a character: the player got as far as
        // picking a side and stopped. Resuming them would skip the name.
        await using var server = await TestServer.StartAsync(o => o.EnableRoomEntry = true);

        var store = server.Services.GetRequiredService<IPlayerStore>();
        var player = await store.GetOrCreateAsync(4243);
        player.Faction = Faction.Cylon;
        await store.SaveAsync(player);

        Assert.Equal(GameLocation.Starter, await WhereTheyAreSentAsync(server, player));
    }
}
