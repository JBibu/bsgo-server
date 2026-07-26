using Bsgo.Protocol;
using Bsgo.Server.Catalogue;
using Bsgo.Server.Players;
using Bsgo.Server.Protocols;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsgo.Server.Tests;

/// <summary>
/// Exercises the full handshake against a real server, speaking the same byte
/// format as the game client.
/// </summary>
public class LoginHandshakeTests
{
    private const uint ProtocolRevision = 4578;

    private static Task<TestServer> StartAsync() =>
        TestServer.StartAsync(o =>
        {
            o.ProtocolRevision = ProtocolRevision;
            o.AllowAnyCredentials = true;
        });

    private static BgoWriter Credentials(uint playerId, string name = "Starbuck")
    {
        var w = new BgoWriter();
        w.Write((byte)ConnectType.Web);
        w.Write(playerId);
        w.Write(name);
        w.Write("c7faac2379e35f6404eced5f484210ba");
        return w;
    }

    [Fact]
    public async Task The_server_greets_on_connect()
    {
        await using var server = await StartAsync();
        using var client = await server.ConnectAsync();

        var (protocol, type, payload) = await client.ReadAsync();

        Assert.Equal(ProtocolId.Login, protocol);
        Assert.Equal((ushort)LoginReply.Hello, type);
        Assert.Empty(payload);
    }

    [Fact]
    public async Task The_full_handshake_authenticates_the_player()
    {
        await using var server = await StartAsync();
        using var client = await server.ConnectAsync();

        // 1. server -> Hello
        var hello = await client.ReadAsync();
        Assert.Equal((ushort)LoginReply.Hello, hello.Type);

        // 2. client -> Init
        await client.SendAsync(ProtocolId.Login, (ushort)LoginRequest.Init);

        // 3. server -> Init with the protocol revision
        var init = await client.ReadAsync();
        Assert.Equal((ushort)LoginReply.Init, init.Type);
        Assert.Equal(ProtocolRevision, new BgoReader(init.Payload).ReadUInt32());

        // 4. client -> Player with the session credentials
        await client.SendAsync(ProtocolId.Login, (ushort)LoginRequest.Player, Credentials(5085935));

        // 5. server -> Player: split date + clock reference + roles
        var player = await client.ReadUntilAsync(ProtocolId.Login, (ushort)LoginReply.Player);

        var r = new BgoReader(player.Payload);
        int year = r.ReadInt32();
        int month = r.ReadInt32();
        int day = r.ReadInt32();
        int hour = r.ReadInt32();
        int minute = r.ReadInt32();
        int second = r.ReadInt32();
        long connectionTime = r.ReadInt64();
        uint roles = r.ReadUInt32();

        Assert.InRange(year, 2020, 2100);
        Assert.InRange(month, 1, 12);
        Assert.InRange(day, 1, 31);
        Assert.InRange(hour, 0, 23);
        Assert.InRange(minute, 0, 59);
        Assert.InRange(second, 0, 59);
        Assert.True(connectionTime > 0);
        Assert.Equal(0u, roles);
        Assert.Equal(0, r.Remaining);   // the client consumes the whole message
    }

    [Fact]
    public async Task Entering_returns_the_player_their_identifier()
    {
        await using var server = await StartAsync();
        using var client = await server.ConnectAsync();

        await client.ReadAsync();   // Hello
        await client.SendAsync(ProtocolId.Login, (ushort)LoginRequest.Player, Credentials(5085935));

        // The client keeps it and identifies with it next time.
        var id = await client.ReadUntilAsync(ProtocolId.Player, (ushort)PlayerReply.ID);
        Assert.Equal(5085935u, new BgoReader(id.Payload).ReadUInt32());
    }

    [Fact]
    public async Task A_client_without_an_identifier_is_assigned_one()
    {
        await using var server = await StartAsync();
        using var client = await server.ConnectAsync();

        await client.ReadAsync();   // Hello
        // With 0 the client is saying "I have no identifier yet".
        await client.SendAsync(ProtocolId.Login, (ushort)LoginRequest.Player, Credentials(0, name: ""));

        var id = await client.ReadUntilAsync(ProtocolId.Player, (ushort)PlayerReply.ID);
        uint assigned = new BgoReader(id.Payload).ReadUInt32();

        // Handing the 0 back made every player share one character.
        Assert.NotEqual(0u, assigned);
    }

    [Fact]
    public async Task Two_clients_without_identifiers_receive_different_ones()
    {
        await using var server = await StartAsync();

        using var first = await server.ConnectAsync();
        await first.ReadAsync();
        await first.SendAsync(ProtocolId.Login, (ushort)LoginRequest.Player, Credentials(0, name: ""));
        uint firstId = new BgoReader(
            (await first.ReadUntilAsync(ProtocolId.Player, (ushort)PlayerReply.ID)).Payload).ReadUInt32();

        using var second = await server.ConnectAsync();
        await second.ReadAsync();
        await second.SendAsync(ProtocolId.Login, (ushort)LoginRequest.Player, Credentials(0, name: ""));
        uint secondId = new BgoReader(
            (await second.ReadUntilAsync(ProtocolId.Player, (ushort)PlayerReply.ID)).Payload).ReadUInt32();

        Assert.NotEqual(firstId, secondId);
    }

    [Fact]
    public async Task The_avatar_catalogue_arrives_before_the_faction_choice()
    {
        await using var server = await StartAsync();
        using var client = await server.ConnectAsync();

        await client.ReadAsync();   // Hello
        await client.SendAsync(ProtocolId.Login, (ushort)LoginRequest.Player, Credentials(5085935));

        // The client builds the default avatar from the catalogue when the
        // faction arrives; without it, it fails with a null reference.
        var card = await client.ReadUntilAsync(ProtocolId.Catalogue, (ushort)CatalogueReply.Card);

        var r = new BgoReader(card.Payload);
        Assert.Equal(AvatarCatalogue.CardGuid, r.ReadUInt32());
        Assert.Equal((ushort)CardView.AvatarCatalogue, r.ReadUInt16());
    }


    [Fact]
    public async Task A_protocol_without_a_handler_does_not_kill_the_connection()
    {
        await using var server = await StartAsync();
        using var client = await server.ConnectAsync();

        await client.ReadAsync();   // Hello

        // Zone has no handler yet: it should be logged and stay alive.
        await client.SendAsync(ProtocolId.Zone, 1);

        await client.SendAsync(ProtocolId.Login, (ushort)LoginRequest.Init);
        var init = await client.ReadAsync();
        Assert.Equal((ushort)LoginReply.Init, init.Type);
    }

    [Fact]
    public async Task A_message_split_across_packets_is_reassembled()
    {
        await using var server = await StartAsync();
        using var client = await server.ConnectAsync();

        await client.ReadAsync();   // Hello

        var frame = BgoFraming.Frame(ProtocolId.Login, (ushort)LoginRequest.Init, new BgoWriter(0));

        // Sent byte by byte: forces the reader to wait for the full message.
        foreach (var b in frame)
        {
            await client.SendRawAsync([b]);
            await Task.Delay(5);
        }

        var init = await client.ReadAsync();
        Assert.Equal((ushort)LoginReply.Init, init.Type);
    }

    [Fact]
    public async Task The_player_is_registered_under_their_identifier()
    {
        await using var server = await StartAsync();
        using var client = await server.ConnectAsync();

        await client.ReadAsync();
        await client.SendAsync(ProtocolId.Login, (ushort)LoginRequest.Player, Credentials(5085935));
        await client.ReadUntilAsync(ProtocolId.Player, (ushort)PlayerReply.ID);

        var store = server.Services.GetRequiredService<IPlayerStore>();
        Assert.Equal(5085935u, store.GetOrCreate(5085935).Id);
    }
}
