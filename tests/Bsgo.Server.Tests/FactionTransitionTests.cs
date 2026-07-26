using Bsgo.Protocol;
using Bsgo.Server.Players;
using Bsgo.Server.Protocols;
using Bsgo.Server.Scenes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsgo.Server.Tests;

/// <summary>
/// Choosing a faction has to end in a scene transition. The client does not
/// move on by itself: it waits indefinitely if the server confirms the faction
/// but never says where to go.
/// </summary>
public class FactionTransitionTests
{
    private static BgoWriter Faction(Faction faction)
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

        await client.SendAsync(ProtocolId.Player, (ushort)PlayerRequest.SelectFaction, Faction(Players.Faction.Cylon));

        // Confirms which side they are on...
        var faction = await client.ReadUntilAsync(ProtocolId.Player, (ushort)PlayerReply.Faction);
        Assert.Equal((byte)Players.Faction.Cylon, new BgoReader(faction.Payload).ReadByte());

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
    public async Task The_chosen_faction_is_recorded()
    {
        await using var server = await TestServer.StartAsync();
        using var client = await server.ConnectAsync();
        await client.ReadAsync();

        await client.SendAsync(ProtocolId.Player, (ushort)PlayerRequest.SelectFaction, Faction(Players.Faction.Colonial));
        await client.ReadUntilAsync(ProtocolId.Player, (ushort)PlayerReply.Faction);

        var store = server.Services.GetRequiredService<IPlayerStore>();
        Assert.Equal(Players.Faction.Colonial, store.GetOrCreate(0).Faction);
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

        await client.SendAsync(ProtocolId.Player, (ushort)PlayerRequest.SelectFaction, Faction(Players.Faction.Cylon));
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

        await client.SendAsync(ProtocolId.Player, (ushort)PlayerRequest.SelectFaction, Faction(Players.Faction.Cylon));
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
