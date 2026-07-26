using Bsgo.Protocol;
using Bsgo.Server.Catalogue;
using Bsgo.Server.Net;
using Microsoft.Extensions.Logging;

namespace Bsgo.Server.Players;

/// <summary>
/// The ship the player flies, and telling the client about it.
/// </summary>
/// <remarks>
/// The client keeps a hangar with an active ship and reaches for it from the
/// moment it enters a room. Finding none there is what has kept room entry shut:
/// the hangar window dereferences it while building the UI, inside
/// <c>Update</c>, so it fails once per frame and takes the client down with it.
/// <para>
/// One ship per player for now. The client models a list, and this fills it with
/// a single entry rather than pretending to a hangar the server cannot yet keep.
/// </para>
/// </remarks>
public sealed class Hangar(
    ShipCatalogue ships,
    IPlayerStore store,
    ILogger<Hangar> logger) : IPlayerEnteredHook
{
    /// <summary>
    /// The player's ship, as the client indexes its hangar.
    /// </summary>
    /// <remarks>
    /// Scoped to one player: every player's single ship is number one, and the
    /// client never sees anybody else's hangar.
    /// </remarks>
    public const ushort ShipId = 1;

    /// <summary>
    /// After the catalogue, which the client needs in order to resolve the card
    /// this announces, and before anything that assumes a ship exists.
    /// </summary>
    public int Order => 50;

    public async Task OnPlayerEnteredAsync(BgoConnection connection, PlayerRecord player, CancellationToken ct)
    {
        if (await EnsureShipAsync(player, ct) is null) return;
        await SendAsync(connection, player, ct);
    }

    /// <summary>
    /// Gives the player their faction's starter ship if they have none.
    /// </summary>
    /// <returns>The ship they now have, or <c>null</c> if they can have none.</returns>
    /// <remarks>
    /// A character with no faction yet is mid-creation and gets nothing; there
    /// is no neutral ship to give.
    /// <para>
    /// A stored ship that no longer answers counts as no ship. Card identifiers
    /// are derived from the ship's name, so renaming one in the table orphans
    /// everybody flying it — and a player who reached the room with nothing in
    /// their hangar is the exact failure this class exists to prevent, arriving
    /// silently by another door.
    /// </para>
    /// </remarks>
    public async Task<ShipDefinition?> EnsureShipAsync(PlayerRecord player, CancellationToken ct = default)
    {
        if (player.ShipCardGuid != 0)
        {
            if (ships.Find(player.ShipCardGuid) is { } owned) return owned;

            logger.LogWarning(
                "Player {PlayerId} holds ship {Card}, which the table no longer describes; "
                + "replacing it", player.Id, player.ShipCardGuid);
        }

        if (ships.StarterFor(player.Faction) is not { } starter)
            return null;

        player.ShipCardGuid = starter.CardGuid;
        await store.SaveAsync(player, ct);

        logger.LogInformation(
            "Player {PlayerId} was given a {Ship}", player.Id, starter.Name);
        return starter;
    }

    /// <summary>
    /// Announces the ship: which one it is, that it is the active one, and that
    /// its slots are settled.
    /// </summary>
    /// <remarks>
    /// The slot message is sent even with nothing in it. It is what sets the
    /// client's <c>SlotsCreated</c> flag, and the ship is not finished loading
    /// until that is set — an empty one says "there is nothing to install here",
    /// which is true, where sending none says nothing at all.
    /// </remarks>
    public async Task SendAsync(BgoConnection connection, PlayerRecord player, CancellationToken ct)
    {
        var add = new BgoWriter(6);
        add.Write(ShipId);
        add.Write(player.ShipCardGuid);
        await connection.SendAsync(ProtocolId.Player, (ushort)PlayerReply.AddShip, add, ct);

        var active = new BgoWriter(2);
        active.Write(ShipId);
        await connection.SendAsync(ProtocolId.Player, (ushort)PlayerReply.ActiveShip, active, ct);

        var slots = new BgoWriter(4);
        slots.Write(ShipId);
        slots.Write((ushort)0);   // nothing installed
        await connection.SendAsync(ProtocolId.Player, (ushort)PlayerReply.Slots, slots, ct);
    }
}
