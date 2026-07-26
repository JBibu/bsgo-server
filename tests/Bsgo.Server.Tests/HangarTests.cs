using Bsgo.Protocol;
using Bsgo.Server.Catalogue;
using Bsgo.Server.Players;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bsgo.Server.Tests;

/// <summary>
/// What the player ends up with in their hangar.
/// </summary>
/// <remarks>
/// Every path through here has the same failure at the end of it: a player who
/// reaches the room with no active ship puts the client in a throw-per-frame
/// loop that instantiates the scenery until it runs out of memory.
/// </remarks>
public class HangarTests
{
    private static readonly ShipCatalogue Ships =
        ShipCatalogue.LoadFrom(ServerServices.DataFile("ships.json"));

    private static (Hangar Hangar, IPlayerStore Store) Fresh()
    {
        var store = new InMemoryPlayerStore();
        return (new Hangar(Ships, store, NullLogger<Hangar>.Instance), store);
    }

    [Theory]
    [InlineData(Faction.Colonial, "Viper Mark II")]
    [InlineData(Faction.Cylon, "Cylon Raider")]
    public async Task A_character_with_a_faction_is_given_its_starter(Faction faction, string expected)
    {
        var (hangar, store) = Fresh();
        var player = await store.GetOrCreateAsync(1);
        player.Faction = faction;

        Assert.Equal(expected, (await hangar.EnsureShipAsync(player))?.Name);
        Assert.Equal(Ships.Find(expected)!.CardGuid, player.ShipCardGuid);

        // And it is saved, not just set on the instance in hand.
        Assert.Equal(player.ShipCardGuid, (await store.GetOrCreateAsync(1)).ShipCardGuid);
    }

    [Fact]
    public async Task A_character_still_being_made_gets_nothing()
    {
        // No faction, no side to give a ship from.
        var (hangar, store) = Fresh();
        var player = await store.GetOrCreateAsync(1);

        Assert.Null(await hangar.EnsureShipAsync(player));
        Assert.Equal(0u, player.ShipCardGuid);
    }

    [Fact]
    public async Task A_ship_already_owned_is_kept()
    {
        var (hangar, store) = Fresh();
        var player = await store.GetOrCreateAsync(1);
        player.Faction = Faction.Cylon;
        player.ShipCardGuid = Ships.Find("Banshee")!.CardGuid;

        Assert.Equal("Banshee", (await hangar.EnsureShipAsync(player))?.Name);
    }

    [Fact]
    public async Task A_ship_the_table_no_longer_describes_is_replaced()
    {
        // Card identifiers come from the ship's name, so renaming one in the
        // table leaves every player holding it pointing at nothing. Handing
        // them their starter is worse than keeping what they had and better
        // than sending them to the room empty-handed, which is what happened
        // before: no ship, and no complaint either.
        var (hangar, store) = Fresh();
        var player = await store.GetOrCreateAsync(1);
        player.Faction = Faction.Colonial;
        player.ShipCardGuid = 0xDEAD;

        Assert.Equal("Viper Mark II", (await hangar.EnsureShipAsync(player))?.Name);
        Assert.NotEqual(0xDEADu, player.ShipCardGuid);
    }
}
