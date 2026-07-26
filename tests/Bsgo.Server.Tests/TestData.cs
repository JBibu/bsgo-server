using Bsgo.Server.Catalogue;

namespace Bsgo.Server.Tests;

/// <summary>
/// The generated data files, loaded once and shared.
/// </summary>
/// <remarks>
/// Both catalogues are immutable, so parsing them per test only burned time —
/// the avatar catalogue alone is 20 KB of JSON that was being deserialised
/// seven times over.
/// </remarks>
public static class TestData
{
    private static readonly Lazy<AvatarCatalogue> LazyAvatars =
        new(() => AvatarCatalogue.LoadFrom(ServerServices.DataFile("avatar-catalogue.json")));

    private static readonly Lazy<RoomCatalogue> LazyRooms =
        new(() => RoomCatalogue.LoadFrom(ServerServices.DataFile("rooms.json")));

    public static AvatarCatalogue Avatars => LazyAvatars.Value;
    public static RoomCatalogue Rooms => LazyRooms.Value;
}
