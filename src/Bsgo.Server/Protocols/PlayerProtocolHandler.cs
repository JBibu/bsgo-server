using Bsgo.Protocol;
using Bsgo.Server.Net;
using Bsgo.Server.Catalogue;
using Bsgo.Server.Players;
using Bsgo.Server.Scenes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsgo.Server.Protocols;

/// <summary>
/// The player's character. Only the creation part lives here — faction, name
/// and avatar; inventory, skills and missions come later.
/// </summary>
public sealed class PlayerProtocolHandler(
    IPlayerStore store,
    IOptions<ServerOptions> options,
    RoomCatalogue rooms,
    SceneDirector scenes,
    ILogger<PlayerProtocolHandler> logger) : IProtocolHandler, IPlayerEnteredHook
{
    public ProtocolId Protocol => ProtocolId.Player;

    /// <summary>
    /// Sends the player their identifier on entry, so the client keeps it and
    /// identifies with it next time.
    /// </summary>
    public Task OnPlayerEnteredAsync(BgoConnection connection, CancellationToken ct) =>
        SendIdAsync(connection, store.GetOrCreate(connection.State.PlayerId), ct);

    public Task HandleAsync(BgoConnection connection, ushort messageType, ReadOnlyMemory<byte> payload, CancellationToken ct)
        => (PlayerRequest)messageType switch
        {
            PlayerRequest.SelectFaction => SelectFactionAsync(connection, payload, ct),
            PlayerRequest.CheckNameAvailability => CheckNameAsync(connection, payload, ct),
            PlayerRequest.ChooseName => ChooseNameAsync(connection, payload, ct),
            PlayerRequest.CreateAvatar => CreateAvatarAsync(connection, payload, ct),
            _ => ProtocolLog.Unhandled<PlayerRequest>(logger, connection, messageType),
        };

    private async Task SelectFactionAsync(BgoConnection connection, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        var r = new BgoReader(payload.Span);
        var faction = (Faction)r.ReadByte();

        var player = store.GetOrCreate(connection.State.PlayerId);
        player.Faction = faction;
        store.Save(player);

        logger.LogInformation("Player {PlayerId} chose {Faction}", player.Id, faction);

        // The client needs all three to build the next screen: who they are,
        // what they are called, and which side they are on.
        await SendIdAsync(connection, player, ct);
        await SendFactionAsync(connection, player, ct);
        await SendNameAsync(connection, player, ct);

        // And the transition: after picking a faction the client hides the
        // window and sits on "Please wait" until the server says where to go.
        await scenes.SendToAvatarCreationAsync(connection, ct);
    }

    private Task CheckNameAsync(BgoConnection connection, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        var r = new BgoReader(payload.Span);
        string name = r.ReadString();

        bool available = store.IsNameAvailable(name, connection.State.PlayerId);
        logger.LogInformation("Name \"{Name}\": {Result}", name, available ? "free" : "taken");

        // Both replies carry no payload.
        var reply = available ? PlayerReply.NameAvailable : PlayerReply.NameNotAvailable;
        return connection.SendAsync(ProtocolId.Player, (ushort)reply, ct);
    }

    private async Task ChooseNameAsync(BgoConnection connection, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        var r = new BgoReader(payload.Span);
        string name = r.ReadString();

        if (!store.IsNameAvailable(name, connection.State.PlayerId))
        {
            logger.LogWarning("Name \"{Name}\" rejected: already in use", name);
            await connection.SendAsync(ProtocolId.Player, (ushort)PlayerReply.NameNotAvailable, ct);
            return;
        }

        var player = store.GetOrCreate(connection.State.PlayerId);
        player.Name = name;
        store.Save(player);
        connection.State.PlayerName = name;

        logger.LogInformation("Player {PlayerId} is now named \"{Name}\"", player.Id, name);
        await SendNameAsync(connection, player, ct);
    }

    /// <summary>
    /// Stores the chosen appearance and returns the confirmed avatar.
    /// </summary>
    private async Task CreateAvatarAsync(BgoConnection connection, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        var r = new BgoReader(payload.Span);
        var avatar = AvatarDescription.Read(ref r);

        var player = store.GetOrCreate(connection.State.PlayerId);
        player.AvatarDescription = avatar.ToBytes();
        store.Save(player);

        logger.LogInformation(
            "Avatar created for {PlayerId}: {Race}/{Sex}, {Slots} slots",
            player.Id,
            avatar[AvatarItem.Race] ?? "?",
            avatar[AvatarItem.Sex] ?? "?",
            avatar.Items.Count);

        await SendAvatarAsync(connection, avatar, ct);

        // Character finished: this is where they would go to their faction's
        // room, but with no ship the client hangs instantiating the scenery in
        // a loop (see ServerOptions.EnableRoomEntry).
        if (!options.Value.EnableRoomEntry)
        {
            logger.LogWarning(
                "Room entry disabled: player {PlayerId} stays in character creation. "
                + "They need a ship before the hangar can be entered.",
                player.Id);
            return;
        }

        var room = rooms.ForFaction(player.Faction);
        if (room is null)
        {
            logger.LogError(
                "No room defined for faction {Faction}: the client will be left waiting",
                player.Faction);
            return;
        }

        await scenes.SendToRoomAsync(connection, room.CardGuid, room.SectorId, ct);
    }

    private static Task SendAvatarAsync(BgoConnection connection, AvatarDescription avatar, CancellationToken ct)
    {
        var w = new BgoWriter();
        avatar.Write(w);
        return connection.SendAsync(ProtocolId.Player, (ushort)PlayerReply.Avatar, w, ct);
    }

    private static Task SendIdAsync(BgoConnection connection, PlayerRecord player, CancellationToken ct)
    {
        var w = new BgoWriter(4);
        w.Write(player.Id);
        return connection.SendAsync(ProtocolId.Player, (ushort)PlayerReply.ID, w, ct);
    }

    private static Task SendFactionAsync(BgoConnection connection, PlayerRecord player, CancellationToken ct)
    {
        var w = new BgoWriter(1);
        w.Write((byte)player.Faction);
        return connection.SendAsync(ProtocolId.Player, (ushort)PlayerReply.Faction, w, ct);
    }

    private static Task SendNameAsync(BgoConnection connection, PlayerRecord player, CancellationToken ct)
    {
        var w = new BgoWriter();
        w.Write(player.Name);
        return connection.SendAsync(ProtocolId.Player, (ushort)PlayerReply.Name, w, ct);
    }

}
