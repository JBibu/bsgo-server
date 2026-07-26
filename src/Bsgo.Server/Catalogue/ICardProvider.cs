using Bsgo.Protocol;

namespace Bsgo.Server.Catalogue;

/// <summary>
/// Supplies the contents of one kind of catalogue card.
/// </summary>
/// <remarks>
/// The client asks for cards by identifier and view, and there are 41 views in
/// total. Rather than growing a branch per view inside the catalogue handler,
/// each source of game data registers a provider: adding ships or sectors later
/// is a new class plus a registration, with no edits to the dispatcher.
/// </remarks>
public interface ICardProvider
{
    /// <summary>
    /// Writes the card body if this provider owns it.
    /// </summary>
    /// <remarks>
    /// Only the body: the identifier and view prefix is written by the caller,
    /// which is the same for every card.
    /// </remarks>
    /// <returns><c>false</c> if the card belongs to another provider.</returns>
    bool TryWriteCard(uint cardGuid, CardView view, BgoWriter w);
}

/// <summary>Serves the avatar catalogue, the card shown at character creation.</summary>
/// <remarks>
/// The body is serialised once and then copied. It is the largest message the
/// server sends (~12 KB for three avatars, some 550 strings) and it goes out on
/// every login, so rebuilding it per connection meant re-encoding every string
/// and growing the buffer through six reallocations to produce constant bytes.
/// </remarks>
public sealed class AvatarCardProvider(AvatarCatalogue catalogue) : ICardProvider
{
    private readonly Lazy<byte[]> _body = new(() =>
    {
        var w = new BgoWriter(16 * 1024);
        catalogue.Write(w);
        return w.ToArray();
    });

    public bool TryWriteCard(uint cardGuid, CardView view, BgoWriter w)
    {
        if (view != CardView.AvatarCatalogue || cardGuid != AvatarCatalogue.CardGuid)
            return false;

        w.WriteRaw(_body.Value);
        return true;
    }
}

/// <summary>
/// Serves both cards that describe a room.
/// </summary>
/// <remarks>
/// <c>Room</c> and <c>World</c> share an identifier and describe the same room
/// from two angles; the client needs both before it loads the scene.
/// </remarks>
public sealed class RoomCardProvider(RoomCatalogue catalogue) : ICardProvider
{
    public bool TryWriteCard(uint cardGuid, CardView view, BgoWriter w)
    {
        if (view is not (CardView.Room or CardView.World)) return false;
        if (catalogue.Find(cardGuid) is not { } room) return false;

        if (view == CardView.Room)
            room.WriteRoomCard(w);
        else
            room.WriteWorldCard(w);

        return true;
    }
}
