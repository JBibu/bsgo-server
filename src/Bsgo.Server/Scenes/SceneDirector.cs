using Bsgo.Protocol;
using Bsgo.Server.Catalogue;
using Bsgo.Server.Net;
using Bsgo.Server.Players;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsgo.Server.Scenes;

/// <summary>
/// Decides and sends which scene each client goes to.
/// </summary>
/// <remarks>
/// The client never changes scene on its own: when a step finishes it waits on
/// "Please wait" until the server sends the transition. Forgetting to send it
/// produces no error at all, just a stuck client, so every action that
/// completes a screen must end up here.
/// <para>
/// The payload varies by destination, and the client reads exactly the fields
/// belonging to that destination: sending another's desynchronises it.
/// </para>
/// </remarks>
public sealed class SceneDirector(
    RoomCatalogue rooms,
    IOptions<ServerOptions> options,
    ILogger<SceneDirector> logger)
{
    /// <summary>
    /// Sends the player wherever their character stands: into the game if they
    /// have one, to character creation if they do not.
    /// </summary>
    /// <remarks>
    /// Asked on login, when the client has left the login screen and is waiting
    /// to be told what to load. It waits indefinitely, so this must always end
    /// in a transition — including when the answer is unsatisfying.
    /// </remarks>
    public async Task SendAfterLoginAsync(BgoConnection connection, PlayerRecord player, CancellationToken ct)
    {
        if (player.IsCreated)
        {
            if (await TrySendIntoTheGameAsync(connection, player, ct))
                return;

            // Their character exists and there is still nowhere to put it. They
            // get the creation screen again, which at least does something,
            // rather than the "Please wait" they would sit on forever.
            logger.LogWarning(
                "Player {PlayerId} has a character but no playable destination; "
                + "sending them back to character creation",
                player.Id);
        }

        await SendToFactionSelectionAsync(connection, ct);
    }

    /// <summary>
    /// Sends the player into their faction's room, if they can go there at all.
    /// </summary>
    /// <returns>Whether a transition was sent.</returns>
    /// <remarks>
    /// The caller has to know: leaving the client without a destination is not
    /// an error it can see, it just stops.
    /// </remarks>
    public async Task<bool> TrySendIntoTheGameAsync(
        BgoConnection connection, PlayerRecord player, CancellationToken ct)
    {
        // Deliberately shut: the hangar window reaches for the player's active
        // ship, and there are none. Being null it throws inside the client's
        // Update, which retries every frame and instantiates the scenery once
        // per attempt until it runs out of memory.
        if (!options.Value.EnableRoomEntry)
        {
            logger.LogWarning(
                "Room entry disabled: player {PlayerId} stays out of the game. "
                + "They need a ship before the hangar can be entered.",
                player.Id);
            return false;
        }

        var room = rooms.ForFaction(player.Faction);
        if (room is null)
        {
            logger.LogError("No room defined for faction {Faction}", player.Faction);
            return false;
        }

        await SendToRoomAsync(connection, room.CardGuid, room.SectorId, ct);
        return true;
    }

    /// <summary>Faction selection screen (new character).</summary>
    public Task SendToFactionSelectionAsync(BgoConnection connection, CancellationToken ct) =>
        SendAsync(connection, GameLocation.Starter, ct, w =>
        {
            w.Write(0u);   // colonial bonus ship
            w.Write(0u);   // cylon bonus ship
        });

    /// <summary>
    /// Avatar creation screen: the character's appearance and name.
    /// </summary>
    /// <remarks>
    /// The identifier list goes empty: the client builds this scene from its
    /// faction's assets and needs no suggestions from the server.
    /// </remarks>
    public Task SendToAvatarCreationAsync(BgoConnection connection, CancellationToken ct) =>
        SendAsync(connection, GameLocation.Avatar, ct, w =>
        {
            w.Write((ushort)0);   // no avatars proposed
            w.Write(false);       // no faction change in progress
        });

    /// <summary>
    /// Playable room (the faction's CIC). The client will ask the catalogue for
    /// the <c>Room</c> and <c>World</c> cards of this identifier.
    /// </summary>
    public Task SendToRoomAsync(BgoConnection connection, uint cardGuid, uint sectorId, CancellationToken ct) =>
        SendAsync(connection, GameLocation.Room, ct, w =>
        {
            w.Write(cardGuid);
            w.Write(sectorId);
        });

    /// <summary>
    /// Writes the header every transition shares and appends the
    /// destination-specific tail.
    /// </summary>
    /// <remarks>
    /// The destination is stated once, here: when each caller wrote it into the
    /// payload *and* passed it for logging, the two could disagree with nothing
    /// to catch it.
    /// </remarks>
    private Task SendAsync(
        BgoConnection connection,
        GameLocation location,
        CancellationToken ct,
        Action<BgoWriter> writeTail)
    {
        var w = new BgoWriter();
        w.Write((byte)TransSceneType.None);
        w.Write((byte)location);
        writeTail(w);

        logger.LogInformation(
            "Sending {Who} to the {Location} scene",
            connection.State.PlayerName is { Length: > 0 } name ? name : connection.RemoteEndPoint,
            location);
        return connection.SendAsync(ProtocolId.Scene, (ushort)SceneReply.LoadNextScene, w, ct);
    }
}
