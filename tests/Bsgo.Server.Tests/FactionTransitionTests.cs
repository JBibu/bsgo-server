using Bsgo.Protocol;
using Bsgo.Server.Players;
using Microsoft.Extensions.DependencyInjection;

namespace Bsgo.Server.Tests;

/// <summary>
/// Choosing a faction has to end in a scene transition. The client does not
/// move on by itself: it waits indefinitely if the server confirms the faction
/// but never says where to go.
/// </summary>
public class FactionTransitionTests
{
    private static BgoWriter FactionPayload(Faction faction)
    {
        var w = new BgoWriter(1);
        w.Write((byte)faction);
        return w;
    }

    [Fact]
    public async Task Choosing_a_faction_confirms_it_and_sends_to_avatar_creation()
    {
        await using var server = await TestServer.StartAsync();
        using var client = await server.ConnectAsync();
        await client.ReadAsync();   // Hello

        await client.SendAsync(ProtocolId.Player, (ushort)PlayerRequest.SelectFaction, FactionPayload(Faction.Cylon));

        // Confirms which side they are on...
        var faction = await client.ReadUntilAsync(ProtocolId.Player, (ushort)PlayerReply.Faction);
        Assert.Equal((byte)Faction.Cylon, new BgoReader(faction.Payload).ReadByte());

        // ...and where they go: without this the client sits on "Please wait".
        var scene = await client.ReadUntilAsync(ProtocolId.Scene, (ushort)SceneReply.LoadNextScene);

        var r = new BgoReader(scene.Payload);
        Assert.Equal((byte)TransSceneType.None, r.ReadByte());
        Assert.Equal((byte)GameLocation.Avatar, r.ReadByte());
        Assert.Equal(0, r.ReadUInt16());    // no avatars proposed
        Assert.False(r.ReadBool());         // no faction change
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public async Task The_avatar_arrives_before_the_faction()
    {
        // Reading the faction makes the client inspect the appearance it holds,
        // to fall back to a default look when there is none. A character who has
        // never sent one has that field null, the read throws, and the client
        // swallows the exception: the faction never reaches the rest of the UI.
        await using var server = await TestServer.StartAsync();
        using var client = await server.ConnectAsync();
        await client.ReadAsync();   // Hello

        await client.SendAsync(ProtocolId.Player, (ushort)PlayerRequest.SelectFaction, FactionPayload(Faction.Cylon));

        // Collected up to the scene transition, which closes the exchange, so
        // that a wrong order fails here instead of hanging on a read.
        var replies = new List<(ushort Type, byte[] Payload)>();
        while (true)
        {
            var message = await client.ReadAsync();
            if (message.Protocol == ProtocolId.Scene) break;
            if (message.Protocol == ProtocolId.Player) replies.Add((message.Type, message.Payload));
        }

        var order = replies.ConvertAll(m => (PlayerReply)m.Type);
        Assert.True(
            order.IndexOf(PlayerReply.Avatar) < order.IndexOf(PlayerReply.Faction),
            $"the avatar must precede the faction, got: {string.Join(", ", order)}");

        // Empty, and it still has to be sent: the client fills in its own
        // default from there.
        var r = new BgoReader(replies.Find(m => m.Type == (ushort)PlayerReply.Avatar).Payload);
        Assert.Equal(0, r.ReadLength());    // no pieces
        Assert.Equal(0, r.ReadLength());    // empty extra block
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public async Task The_chosen_faction_is_recorded()
    {
        await using var server = await TestServer.StartAsync();
        using var client = await server.ConnectAsync();
        await client.ReadAsync();

        await client.SendAsync(ProtocolId.Player, (ushort)PlayerRequest.SelectFaction, FactionPayload(Faction.Colonial));
        await client.ReadUntilAsync(ProtocolId.Player, (ushort)PlayerReply.Faction);

        var store = server.Services.GetRequiredService<IPlayerStore>();
        Assert.Equal(Faction.Colonial, (await store.GetOrCreateAsync(0)).Faction);
    }

    [Fact]
    public async Task Creating_the_character_does_not_send_to_the_room_while_there_are_no_ships()
    {
        // The hangar window reaches for the player's active ship. With no ship
        // it throws inside the client's Update, which retries every frame and
        // instantiates the scenery once per attempt until it closes.
        await using var server = await TestServer.StartAsync(o => o.EnableRoomEntry = false);
        using var client = await server.ConnectAsync();
        await client.ReadAsync();

        await client.SendAsync(ProtocolId.Player, (ushort)PlayerRequest.SelectFaction, FactionPayload(Faction.Cylon));
        await client.ReadUntilAsync(ProtocolId.Scene, (ushort)SceneReply.LoadNextScene);

        var avatar = new AvatarDescription
        {
            Items = new Dictionary<AvatarItem, string>
            {
                [AvatarItem.Race] = "cylon",
                [AvatarItem.Sex] = "centurion",
            },
        };
        var payload = new BgoWriter();
        avatar.Write(payload);
        await client.SendAsync(ProtocolId.Player, (ushort)PlayerRequest.CreateAvatar, payload);

        // It replies with the avatar, but sends them to no room.
        await client.ReadUntilAsync(ProtocolId.Player, (ushort)PlayerReply.Avatar);
        await Task.Delay(200);
        Assert.Equal(0, client.Available);
    }

    [Fact]
    public async Task With_room_entry_enabled_it_does_send_to_the_room()
    {
        await using var server = await TestServer.StartAsync(o => o.EnableRoomEntry = true);
        using var client = await server.ConnectAsync();
        await client.ReadAsync();

        await client.SendAsync(ProtocolId.Player, (ushort)PlayerRequest.SelectFaction, FactionPayload(Faction.Cylon));
        await client.ReadUntilAsync(ProtocolId.Scene, (ushort)SceneReply.LoadNextScene);

        var avatar = new AvatarDescription
        {
            Items = new Dictionary<AvatarItem, string> { [AvatarItem.Race] = "cylon" },
        };
        var payload = new BgoWriter();
        avatar.Write(payload);
        await client.SendAsync(ProtocolId.Player, (ushort)PlayerRequest.CreateAvatar, payload);

        var scene = await client.ReadUntilAsync(ProtocolId.Scene, (ushort)SceneReply.LoadNextScene);
        var r = new BgoReader(scene.Payload);
        r.ReadByte();
        Assert.Equal((byte)GameLocation.Room, r.ReadByte());
    }
}
