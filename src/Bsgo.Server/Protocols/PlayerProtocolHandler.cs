using Bsgo.Protocol;
using Bsgo.Server.Net;
using Bsgo.Server.Players;
using Bsgo.Server.Scenes;
using Microsoft.Extensions.Logging;

namespace Bsgo.Server.Protocols;

/// <summary>
/// The player's character. Only the creation part lives here — faction, name
/// and avatar; inventory, skills and missions come later.
/// </summary>
public sealed class PlayerProtocolHandler(
    IPlayerStore store,
    SceneDirector scenes,
    ILogger<PlayerProtocolHandler> logger) : IProtocolHandler, IPlayerEnteredHook
{
    public ProtocolId Protocol => ProtocolId.Player;

    /// <summary>
    /// Sends the player their identifier on entry, so the client keeps it and
    /// identifies with it next time.
    /// </summary>
    public Task OnPlayerEnteredAsync(BgoConnection connection, PlayerRecord player, CancellationToken ct) =>
        SendIdAsync(connection, player, ct);

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

        var player = await store.GetOrCreateAsync(connection.State.PlayerId, ct);
        player.Faction = faction;
        await store.SaveAsync(player, ct);

        logger.LogInformation("Player {PlayerId} chose {Faction}", player.Id, faction);

        // The client needs all three to build the next screen: who they are,
        // what they are called, and which side they are on.
        await SendIdAsync(connection, player, ct);
        await SendAvatarAsync(connection, player, ct);
        await SendFactionAsync(connection, player, ct);
        await SendNameAsync(connection, player, ct);

        // And the transition: after picking a faction the client hides the
        // window and sits on "Please wait" until the server says where to go.
        await scenes.SendToAvatarCreationAsync(connection, ct);
    }

    private async Task CheckNameAsync(BgoConnection connection, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        var r = new BgoReader(payload.Span);
        string name = r.ReadString();

        bool available = await store.IsNameAvailableAsync(name, connection.State.PlayerId, ct);
        logger.LogInformation("Name \"{Name}\": {Result}", name, available ? "free" : "taken");

        // Both replies carry no payload.
        var reply = available ? PlayerReply.NameAvailable : PlayerReply.NameNotAvailable;
        await connection.SendAsync(ProtocolId.Player, (ushort)reply, ct);
    }

    private async Task ChooseNameAsync(BgoConnection connection, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        var r = new BgoReader(payload.Span);
        string name = r.ReadString();

        if (!await store.IsNameAvailableAsync(name, connection.State.PlayerId, ct))
        {
            logger.LogWarning("Name \"{Name}\" rejected: already in use", name);
            await connection.SendAsync(ProtocolId.Player, (ushort)PlayerReply.NameNotAvailable, ct);
            return;
        }

        var player = await store.GetOrCreateAsync(connection.State.PlayerId, ct);
        player.Name = name;
        await store.SaveAsync(player, ct);
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

        var player = await store.GetOrCreateAsync(connection.State.PlayerId, ct);
        player.AvatarDescription = avatar.ToBytes();
        await store.SaveAsync(player, ct);

        logger.LogInformation(
            "Avatar created for {PlayerId}: {Race}/{Sex}, {Slots} slots",
            player.Id,
            avatar[AvatarItem.Race] ?? "?",
            avatar[AvatarItem.Sex] ?? "?",
            avatar.Items.Count);

        await SendAvatarAsync(connection, avatar, ct);

        // The character is finished, so this is where they walk into the game.
        // If they cannot yet, they stay on the creation screen they are already
        // looking at — which is a scene, so nothing hangs.
        await scenes.TrySendIntoTheGameAsync(connection, player, ct);
    }

    private static Task SendAvatarAsync(BgoConnection connection, AvatarDescription avatar, CancellationToken ct)
    {
        var w = new BgoWriter();
        avatar.Write(w);
        return connection.SendAsync(ProtocolId.Player, (ushort)PlayerReply.Avatar, w, ct);
    }

    /// <summary>
    /// Sends whatever appearance the character has, even when that is nothing.
    /// </summary>
    /// <remarks>
    /// This <b>must</b> reach the client before <see cref="PlayerReply.Faction"/>.
    /// The faction reply asks the description whether it is empty in order to
    /// fall back to a default look, and a character who has never sent one
    /// leaves that field null: the read throws inside the client, which swallows
    /// the exception and never dispatches the faction to the rest of the UI.
    /// An empty description is fine — the client then picks its own default.
    /// </remarks>
    private static Task SendAvatarAsync(BgoConnection connection, PlayerRecord player, CancellationToken ct) =>
        SendAvatarAsync(connection, AvatarDescription.FromBytes(player.AvatarDescription), ct);

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
