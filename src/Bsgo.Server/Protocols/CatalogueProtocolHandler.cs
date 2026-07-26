using Bsgo.Protocol;
using Bsgo.Server.Catalogue;
using Bsgo.Server.Net;
using Microsoft.Extensions.Logging;

namespace Bsgo.Server.Protocols;

/// <summary>
/// Catalogue card type. Determines how its contents are decoded: the client
/// picks the reader from this value, so the payload format depends entirely
/// on it.
/// </summary>
public enum CardView : ushort
{
    GUI = 1,
    ShipSystem = 2,
    ShipConsumable = 3,
    World = 4,
    Global = 5,
    ShipAbility = 6,
    Counter = 7,
    Skill = 8,
    Ship = 10,
    Sector = 11,
    Starter = 13,
    Room = 14,
    Mission = 16,
    Reward = 18,
    Title = 19,
    Duty = 20,
    AvatarCatalogue = 21,
    Module = 22,
    Price = 23,
    Missile = 24,
    ShipList = 25,
    StickerList = 26,
    Movement = 28,
    Owner = 29,
    GalaxyMap = 30,
    Camera = 31,
    MailTemplate = 32,
    StarterKit = 34,
    ShipPaint = 35,
    Regulation = 36,
    ShipSale = 37,
    SectorEvent = 38,
    Tournament = 39,
    MapPart = 40,
    MapPartSet = 41,
    ShipLight = 42,
    EventShop = 43,
    GlobalBonusEvent = 44,
    Banner = 45,
    ConversionCampaign = 46,
    Zone = 47,
}

/// <summary>
/// Catalogue of static game data: ships, items, sectors, missions.
/// </summary>
/// <remarks>
/// The client requests cards by identifier and expects their data. Each card
/// type has its own format, and the contents are the original server's game
/// tables — which do not ship with the client — so rebuilding them is work
/// separate from the protocol itself.
/// <para>
/// This handler only batches, frames and sends: the contents come from the
/// registered <see cref="ICardProvider"/> instances. Cards nobody owns are
/// logged, which doubles as the to-do list.
/// </para>
/// </remarks>
public sealed class CatalogueProtocolHandler(
    IEnumerable<ICardProvider> providers,
    ILogger<CatalogueProtocolHandler> logger) : IProtocolHandler, IPlayerEnteredHook
{
    private readonly ICardProvider[] _providers = [.. providers];

    public ProtocolId Protocol => ProtocolId.Catalogue;

    /// <summary>
    /// The avatar catalogue is pushed on entry: the client builds a default
    /// avatar from it when the faction reply arrives, and without it fails with
    /// a null reference.
    /// </summary>
    public int Order => 0;

    public Task OnPlayerEnteredAsync(BgoConnection connection, CancellationToken ct) =>
        SendCardAsync(connection, AvatarCatalogue.CardGuid, CardView.AvatarCatalogue, ct);

    public Task HandleAsync(BgoConnection connection, ushort messageType, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        if ((CatalogueRequest)messageType != CatalogueRequest.Card)
            return ProtocolLog.Unhandled<CatalogueRequest>(logger, connection, messageType);

        // The client batches several cards into a single request. They are
        // collected up front because BgoReader is a ref struct and cannot
        // survive the await below.
        var r = new BgoReader(payload.Span);
        int count = r.ReadUInt16();

        // Each entry is 6 bytes, so the declared count is capped by what is
        // actually there: a short message claiming 65535 entries would
        // otherwise reserve half a megabyte before failing.
        var cards = new List<(uint Guid, CardView View)>(Math.Min(count, r.Remaining / 6));
        for (int i = 0; i < count; i++)
            cards.Add((r.ReadGuid(), (CardView)r.ReadUInt16()));

        return SendCardsAsync(connection, cards, ct);
    }

    private async Task SendCardsAsync(
        BgoConnection connection,
        List<(uint Guid, CardView View)> cards,
        CancellationToken ct)
    {
        foreach (var (guid, view) in cards)
            await SendCardAsync(connection, guid, view, ct);
    }

    private async Task SendCardAsync(BgoConnection connection, uint guid, CardView view, CancellationToken ct)
    {
        var w = new BgoWriter();
        // Identifier and view come first in every card: the client uses them to
        // pick which reader to apply to the rest of the message.
        w.Write(guid);
        w.Write((ushort)view);

        foreach (var provider in _providers)
        {
            if (!provider.TryWriteCard(guid, view, w)) continue;

            logger.LogInformation("Sending card {Guid} ({View})", guid, Describe(view));
            await connection.SendAsync(ProtocolId.Catalogue, (ushort)CatalogueReply.Card, w, ct);
            return;
        }

        logger.LogWarning(
            "Unimplemented catalogue card: {Guid} ({View}) for {Endpoint}",
            guid, Describe(view), connection.RemoteEndPoint);
    }

    private static string Describe(CardView view) =>
        Enum.IsDefined(view) ? view.ToString() : $"view {(ushort)view}";
}
