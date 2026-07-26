using System.Text.Json;

namespace Bsgo.Server.Tests;

/// <summary>
/// The rules that make <c>data/ships.json</c> correct.
/// </summary>
/// <remarks>
/// The table is edited by hand, and its invariants used to live in prose asking
/// whoever touched it to remember them. They did not hold: a stat present on one
/// faction and absent on the other slipped through, because the check being run
/// only compared fields the two ships had in common.
/// <para>
/// Read as JSON rather than through a catalogue type. Nothing consumes the file
/// yet, and inventing the shape of a Ship card before the client has asked for
/// one would be guessing.
/// </para>
/// </remarks>
public class ShipTableTests
{
    /// <summary>Stats that an advanced variant never changes.</summary>
    private static readonly string[] FixedOnUpgrade =
    [
        "armor", "avoidance", "critical_defense", "firewall_rating",
        "ftl_range", "inertial_compensation", "turning_acceleration",
    ];

    private sealed record Ship(
        string Name, string Faction, string Counterpart,
        Dictionary<string, double> Stats, Dictionary<string, double> Slots);

    private static readonly Lazy<Dictionary<string, Ship>> LazyShips = new(() =>
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(ServerServices.DataFile("ships.json")));
        var ships = doc.RootElement.GetProperty("ships").EnumerateArray().Select(s => new Ship(
            s.GetProperty("name").GetString()!,
            s.GetProperty("faction").GetString()!,
            s.GetProperty("counterpart").GetString()!,
            s.GetProperty("stats").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetDouble()),
            s.GetProperty("slots").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetDouble())));
        return ships.ToDictionary(s => s.Name);
    });

    private static Dictionary<string, Ship> Ships => LazyShips.Value;

    [Fact]
    public void Every_counterpart_resolves_and_points_back()
    {
        foreach (var ship in Ships.Values)
        {
            Assert.True(Ships.ContainsKey(ship.Counterpart),
                $"{ship.Name} names a counterpart that is not in the table: {ship.Counterpart}");

            var other = Ships[ship.Counterpart];
            Assert.Equal(ship.Name, other.Counterpart);
            Assert.NotEqual(ship.Faction, other.Faction);
        }
    }

    [Fact]
    public void The_factions_mirror_each_other_exactly()
    {
        foreach (var ship in Ships.Values.Where(s => s.Faction == "Colonial"))
        {
            var other = Ships[ship.Counterpart];

            // Union, not intersection: a stat one side carries and the other
            // does not is exactly the break that went unnoticed before.
            foreach (var stat in ship.Stats.Keys.Union(other.Stats.Keys))
                Assert.True(
                    ship.Stats.TryGetValue(stat, out var mine)
                    && other.Stats.TryGetValue(stat, out var theirs)
                    && mine == theirs,
                    $"{stat}: {ship.Name} and {other.Name} disagree");

            foreach (var slot in ship.Slots.Keys.Union(other.Slots.Keys))
                Assert.True(
                    ship.Slots.TryGetValue(slot, out var mine)
                    && other.Slots.TryGetValue(slot, out var theirs)
                    && mine == theirs,
                    $"slot {slot}: {ship.Name} and {other.Name} disagree");
        }
    }

    [Fact]
    public void Advancing_a_ship_never_lowers_a_stat()
    {
        foreach (var (name, ship) in Ships)
        {
            if (!Ships.TryGetValue("Advanced " + name, out var advanced)) continue;

            foreach (var (stat, value) in ship.Stats)
                Assert.True(advanced.Stats[stat] >= value,
                    $"{stat}: Advanced {name} ({advanced.Stats[stat]}) is worse than {name} ({value})");
        }
    }

    [Fact]
    public void Advancing_a_ship_leaves_the_fixed_stats_alone()
    {
        foreach (var (name, ship) in Ships)
        {
            if (!Ships.TryGetValue("Advanced " + name, out var advanced)) continue;

            foreach (var stat in FixedOnUpgrade)
                Assert.True(advanced.Stats[stat] == ship.Stats[stat],
                    $"{stat} does not change on upgrade, but Advanced {name} has "
                    + $"{advanced.Stats[stat]} against {ship.Stats[stat]}");
        }
    }

    [Fact]
    public void Every_ship_carries_the_same_stats_and_slots()
    {
        var stats = Ships.Values.First().Stats.Keys.OrderBy(k => k).ToArray();
        var slots = new[] { "computer", "engine", "hull", "weapon" };

        foreach (var ship in Ships.Values)
        {
            Assert.Equal(stats, ship.Stats.Keys.OrderBy(k => k));
            Assert.Equal(slots, ship.Slots.Keys.OrderBy(k => k));
        }
    }
}
