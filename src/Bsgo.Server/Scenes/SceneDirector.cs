using Bsgo.Protocol;
using Bsgo.Server.Net;
using Microsoft.Extensions.Logging;

namespace Bsgo.Server.Scenes;

/// <summary>Destination of a scene transition.</summary>
public enum GameLocation : byte
{
    Unknown = 0,
    Space = 1,
    Room = 2,
    Story = 3,
    Disconnect = 4,
    Arena = 5,
    BattleSpace = 6,
    Tournament = 7,
    Tutorial = 8,
    Teaser = 9,
    Avatar = 10,
    Starter = 11,
    Zone = 12,
}

/// <summary>Animation the client uses when entering the scene.</summary>
public enum TransSceneType : byte
{
    None = 0,
    Die = 1,
    Undock = 2,
    Ftl = 3,
    Hangar = 4,
    CIC = 5,
    Recroom = 6,
    Outpost = 7,
    Minigfacility = 8,
    FirstStory = 9,
    Dock = 10,
    Arena = 11,
    Teaser = 12,
    Battlespace = 13,
    Tournament = 14,
}

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
public sealed class SceneDirector(ILogger<SceneDirector> logger)
{
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
