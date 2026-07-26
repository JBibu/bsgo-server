using Bsgo.Protocol;
using Bsgo.Server.Catalogue;

namespace Bsgo.Server.Tests;

/// <summary>
/// Entering a room requires two cards with the same identifier: the client will
/// not load the scene until both arrive, and never says which one is missing.
/// </summary>
public class RoomCatalogueTests
{
    private static RoomCatalogue Load() => TestData.Rooms;

    [Fact]
    public void Every_playable_faction_has_its_room()
    {
        var catalogue = Load();

        var colonial = catalogue.ForFaction(Faction.Colonial);
        var cylon = catalogue.ForFaction(Faction.Cylon);

        Assert.NotNull(colonial);
        Assert.NotNull(cylon);

        // With no room, the player finishes creation and is left waiting.
        Assert.NotEqual(colonial!.CardGuid, cylon!.CardGuid);
    }

    [Fact]
    public void The_prefabs_are_the_clients_own()
    {
        var catalogue = Load();

        // No extension: the client appends ".prefab" when requesting the asset.
        Assert.Equal("cic_human", catalogue.ForFaction(Faction.Colonial)!.PrefabName);
        Assert.Equal("cic_cylon", catalogue.ForFaction(Faction.Cylon)!.PrefabName);
    }

    [Fact]
    public void A_room_is_found_by_its_identifier()
    {
        var catalogue = Load();
        var room = catalogue.ForFaction(Faction.Cylon)!;

        Assert.Same(room, catalogue.Find(room.CardGuid));
        Assert.Null(catalogue.Find(999999));
    }

    [Fact]
    public void The_Room_card_is_re_read_in_order()
    {
        var room = Load().ForFaction(Faction.Cylon)!;

        var w = new BgoWriter();
        room.WriteRoomCard(w);

        var r = new BgoReader(w.WrittenSpan);
        Assert.Equal(0, r.ReadLength());          // doors
        Assert.Equal(0, r.ReadLength());          // NPCs
        Assert.Equal(room.Music, r.ReadString());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void The_World_card_is_re_read_in_order()
    {
        var room = Load().ForFaction(Faction.Cylon)!;

        var w = new BgoWriter();
        room.WriteWorldCard(w);

        var r = new BgoReader(w.WrittenSpan);
        Assert.Equal(room.PrefabName, r.ReadString());
        Assert.Equal(1, r.ReadByte());            // levels of detail
        Assert.Equal(room.Radius, r.ReadSingle());
        Assert.Equal(0, r.ReadLength());          // attachment spots
        Assert.Equal("", r.ReadString());         // map texture
        Assert.Equal(-1, r.ReadSByte());          // no frame
        Assert.Equal(0, r.ReadSByte());
        Assert.False(r.ReadBool());
        Assert.False(r.ReadBool());
        Assert.False(r.ReadBool());
        Assert.Equal(0, r.Remaining);             // the client reads to the end
    }
}
