using Bsgo.Protocol;
using Bsgo.Server.Protocols;
using Bsgo.Server.Scenes;
using Xunit;

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
}
