using Bsgo.Protocol;
using Bsgo.Server.Catalogue;
using Bsgo.Server.Players;
using Xunit;

namespace Bsgo.Server.Tests;

/// <summary>
/// The four cards a ship is made of, read back field by field.
/// </summary>
/// <remarks>
/// The client reads these with no field tags, so a field too many or too few
/// shifts everything after it and produces no error — only a hangar that never
/// finishes loading. Each test re-reads the card the way the client does.
/// </remarks>
public class ShipCardTests
{
    private static readonly ShipCatalogue Catalogue =
        ShipCatalogue.LoadFrom(ServerServices.DataFile("ships.json"));

    private static readonly ShipCardProvider Provider = new(Catalogue);

    private static ShipDefinition Viper => Catalogue.Find("Viper Mark II")!;

    private static BgoReader Card(uint guid, CardView view)
    {
        var w = new BgoWriter();
        Assert.True(Provider.TryWriteCard(guid, view, w), $"no provider wrote {view} for {guid}");
        return new BgoReader(w.ToArray());
    }

    [Fact]
    public void The_ship_card_reads_back_exactly_as_the_client_reads_it()
    {
        var ship = Viper;
        var r = Card(ship.CardGuid, CardView.Ship);

        Assert.Equal(ship.CardGuid, r.ReadUInt32());   // object key
        Assert.Equal(1, r.ReadByte());                 // level
        Assert.Equal(1, r.ReadByte());                 // max level
        Assert.Equal(1, r.ReadByte());                 // level requirement
        Assert.Equal(1, r.ReadByte());                 // hangar id
        Assert.Equal(0u, r.ReadUInt32());              // next card
        Assert.Equal(1f, r.ReadSingle());              // durability
        Assert.Equal(1, r.ReadByte());                 // tier

        Assert.Equal(0, r.ReadLength());               // roles
        Assert.Equal(0, r.ReadByte());                 // deprecated role
        Assert.Equal(ship.Paperdoll, r.ReadString());  // paperdoll layout

        // Slots: the Viper has 3 weapon, 2 hull, 4 engine and 2 computer.
        Assert.Equal(11, r.ReadLength());
        for (int i = 1; i <= 11; i++)
        {
            Assert.Equal(i, r.ReadUInt16());           // slot id
            Assert.NotEmpty(r.ReadString());           // object point
            Assert.Equal(i, r.ReadUInt16());           // its hash
            r.ReadByte();                              // system type
            Assert.Equal(1, r.ReadByte());             // slot level
        }

        Assert.False(r.ReadBool());                    // cubit-only repair
        Assert.Equal(0, r.ReadLength());               // variants
        Assert.Equal(-1, r.ReadInt32());               // parent hangar

        Assert.Equal(ship.Stats.Count, r.ReadLength());
        for (int i = 0; i < ship.Stats.Count; i++)
        {
            r.ReadUInt16();                            // stat
            r.ReadSingle();                            // value
        }

        Assert.Equal((byte)Faction.Colonial, r.ReadByte());
        Assert.Equal(0, r.ReadLength());               // immutable slots
        Assert.Equal(0u, r.ReadUInt32());              // trailing key

        // Nothing left over: a card the client would read past the end of.
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void Slots_are_grouped_by_type_and_numbered_from_one()
    {
        var ship = Viper;
        var r = Card(ship.CardGuid, CardView.Ship);

        // Skip to the slot list.
        r.ReadUInt32(); r.ReadByte(); r.ReadByte(); r.ReadByte(); r.ReadByte();
        r.ReadUInt32(); r.ReadSingle(); r.ReadByte();
        r.ReadLength(); r.ReadByte(); r.ReadString();

        var types = new List<ShipSlotType>();
        int count = r.ReadLength();
        for (int i = 0; i < count; i++)
        {
            r.ReadUInt16(); r.ReadString(); r.ReadUInt16();
            types.Add((ShipSlotType)r.ReadByte());
            r.ReadByte();
        }

        Assert.Equal(ship.Slots.Weapon, types.Count(t => t == ShipSlotType.weapon));
        Assert.Equal(ship.Slots.Hull, types.Count(t => t == ShipSlotType.hull));
        Assert.Equal(ship.Slots.Engine, types.Count(t => t == ShipSlotType.engine));
        Assert.Equal(ship.Slots.Computer, types.Count(t => t == ShipSlotType.computer));
    }

    [Fact]
    public void Every_stat_in_the_table_is_one_the_client_knows()
    {
        // A column the client has no ObjectStat for would be dropped silently on
        // the way out, leaving the ship quietly weaker than the table says.
        foreach (var column in Catalogue.Ships.SelectMany(s => s.Stats.Keys).Distinct())
            Assert.True(ShipCardProvider.StatFor(column) is not null,
                $"the table has a \"{column}\" column with no matching ObjectStat");
    }

    [Fact]
    public void A_ship_a_player_can_be_given_names_its_paperdoll_layout()
    {
        // The client loads the layout only when the name is non-empty, then
        // reads it unconditionally. A starter ship without one puts the hangar
        // in a throw-per-frame loop, which is what the whole room entry was
        // blocked on to begin with.
        foreach (var faction in new[] { Faction.Colonial, Faction.Cylon })
        {
            var starter = Catalogue.StarterFor(faction);
            Assert.NotNull(starter);
            Assert.NotEmpty(starter.Paperdoll);
        }
    }

    [Fact]
    public void The_world_card_matches_the_shape_the_room_card_already_proved()
    {
        var r = Card(Viper.CardGuid, CardView.World);

        Assert.Equal(Viper.Prefab, r.ReadString());
        Assert.Equal(1, r.ReadByte());          // levels of detail
        Assert.True(r.ReadSingle() > 0);        // radius
        Assert.Equal(0, r.ReadLength());        // spots
        Assert.Equal(string.Empty, r.ReadString());
        Assert.Equal(-1, r.ReadSByte());
        Assert.Equal(0, r.ReadSByte());
        Assert.True(r.ReadBool());              // targetable
        Assert.True(r.ReadBool());
        Assert.False(r.ReadBool());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void The_gui_and_price_cards_are_complete()
    {
        var gui = Card(Viper.CardGuid, CardView.GUI);
        Assert.Equal(Viper.Name, gui.ReadString());
        gui.ReadByte(); gui.ReadString(); gui.ReadUInt16();
        gui.ReadString(); gui.ReadString(); gui.ReadString();
        Assert.Equal(0, gui.ReadLength());
        Assert.Equal(0, gui.Remaining);

        var price = Card(Viper.CardGuid, CardView.Price);
        price.ReadByte(); price.ReadByte(); price.ReadByte(); price.ReadByte();
        Assert.Equal(0, price.ReadLength());   // sorting names
        price.ReadUInt16();
        Assert.Equal(0, price.ReadLength());   // buy
        Assert.Equal(0, price.ReadLength());   // upgrade
        Assert.Equal(0, price.ReadLength());   // sell
        Assert.False(price.ReadBool());
        Assert.Equal(0, price.Remaining);
    }

    [Fact]
    public void Every_ship_answers_all_four_views()
    {
        foreach (var ship in Catalogue.Ships)
            foreach (var view in new[] { CardView.Ship, CardView.World, CardView.GUI, CardView.Price })
            {
                var w = new BgoWriter();
                Assert.True(Provider.TryWriteCard(ship.CardGuid, view, w),
                    $"{ship.Name} has no {view} card");
                Assert.NotEmpty(w.ToArray());
            }
    }

    [Fact]
    public void Cards_of_other_providers_are_left_alone()
    {
        var w = new BgoWriter();
        Assert.False(Provider.TryWriteCard(Viper.CardGuid, CardView.Room, w));
        Assert.False(Provider.TryWriteCard(1u, CardView.Ship, w));
    }

    [Fact]
    public void Each_faction_starts_with_the_ship_the_game_gave_it()
    {
        Assert.Equal("Viper Mark II", Catalogue.StarterFor(Faction.Colonial)?.Name);
        Assert.Equal("Cylon Raider", Catalogue.StarterFor(Faction.Cylon)?.Name);
        Assert.Null(Catalogue.StarterFor(Faction.Neutral));
    }
}
