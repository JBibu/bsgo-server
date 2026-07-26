using Bsgo.Protocol;

namespace Bsgo.Server.Catalogue;

/// <summary>
/// Serves the four cards the client reads for one ship.
/// </summary>
/// <remarks>
/// Asking for a <c>Ship</c> card is not enough on its own: reading it makes the
/// client fetch <c>GUI</c> and <c>Price</c> for the same identifier from its
/// base class, and <c>World</c> for the 3D object. It waits on all of them
/// before the ship is usable, so a provider that answers only the first leaves
/// the hangar loading forever.
/// </remarks>
public sealed class ShipCardProvider(ShipCatalogue catalogue) : ICardProvider
{
    /// <summary>
    /// Which <see cref="ObjectStat"/> each column of the ship table is.
    /// </summary>
    /// <remarks>
    /// The table's names are the client's own, lower cased and with underscores;
    /// most map by that rule alone and are resolved by <c>Enum.TryParse</c>.
    /// These are the ones where the client's name differs.
    /// </remarks>
    private static readonly Dictionary<string, ObjectStat> Renamed = new()
    {
        ["hull_points"] = ObjectStat.MaxHullPoints,
        ["power"] = ObjectStat.MaxPowerPoints,
        ["power_recharge"] = ObjectStat.PowerRecovery,
        ["armor"] = ObjectStat.ArmorValue,
        ["durability"] = ObjectStat.DurabilityBonus,
        ["turning_speed"] = ObjectStat.TurnSpeed,
        ["turning_acceleration"] = ObjectStat.TurnAcceleration,
        ["inertial_compensation"] = ObjectStat.InertiaCompensation,
    };

    /// <summary>How big the client considers a ship, for targeting and camera.</summary>
    private const float ShipRadius = 10f;

    public bool TryWriteCard(uint cardGuid, CardView view, BgoWriter w)
    {
        if (view is not (CardView.Ship or CardView.World or CardView.GUI or CardView.Price))
            return false;
        if (catalogue.Find(cardGuid) is not { } ship) return false;

        switch (view)
        {
            case CardView.Ship: WriteShip(cardGuid, ship, w); break;
            case CardView.World: WriteWorld(ship, w); break;
            case CardView.GUI: WriteGui(ship, w); break;
            default: WritePrice(w); break;
        }
        return true;
    }

    /// <summary>Resolves a table column to the client's stat, if it has one.</summary>
    public static ObjectStat? StatFor(string column)
    {
        if (Renamed.TryGetValue(column, out var renamed)) return renamed;

        var camel = string.Concat(column.Split('_')
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
        return Enum.TryParse<ObjectStat>(camel, out var stat) ? stat : null;
    }

    private static void WriteShip(uint cardGuid, ShipDefinition ship, BgoWriter w)
    {
        w.Write(cardGuid);               // object key: the ship's own world object
        w.Write((byte)1);                // level
        w.Write((byte)1);                // max level: no upgrade path yet
        w.Write((byte)(ship.Level ?? 1));
        w.Write((byte)1);                // hangar id, per player rather than per card
        w.Write(0u);                     // next card: nothing to upgrade into
        w.Write(1f);                     // durability, full
        w.Write((byte)1);                // tier

        w.WriteLength(0);                // roles
        w.Write((byte)0);                // deprecated role
        w.Write(ship.Paperdoll);         // paperdoll layout, see ShipDefinition.Paperdoll

        WriteSlots(ship, w);

        w.Write(false);                  // repairs are not cubit-only
        w.WriteLength(0);                // no variants
        w.Write(-1);                     // no parent hangar entry

        WriteStats(ship, w);

        w.Write((byte)ship.Faction);
        w.WriteLength(0);                // immutable slots
        w.Write(0u);                     // trailing key, unused by the client
    }

    /// <summary>
    /// The ship's slots, numbered from one and grouped by system type.
    /// </summary>
    /// <remarks>
    /// <c>ObjectPoint</c> names the attachment point on the model; the client
    /// only needs it to hang systems off, and leaves an unknown one unplaced
    /// rather than failing.
    /// </remarks>
    private static void WriteSlots(ShipDefinition ship, BgoWriter w)
    {
        var counts = new (ShipSlotType Type, int Count)[]
        {
            (ShipSlotType.weapon, ship.Slots.Weapon),
            (ShipSlotType.hull, ship.Slots.Hull),
            (ShipSlotType.engine, ship.Slots.Engine),
            (ShipSlotType.computer, ship.Slots.Computer),
        };

        w.WriteLength(counts.Sum(c => c.Count));

        ushort slotId = 1;
        foreach (var (type, count) in counts)
        {
            for (int i = 0; i < count; i++, slotId++)
            {
                w.Write(slotId);
                w.Write($"{type}_{i + 1}".ToLowerInvariant());   // object point
                w.Write(slotId);                                 // its server hash
                w.Write((byte)type);
                w.Write((byte)1);                                // slot level
            }
        }
    }

    private static void WriteStats(ShipDefinition ship, BgoWriter w)
    {
        var stats = ship.Stats
            .Select(s => (Stat: StatFor(s.Key), s.Value))
            .Where(s => s.Stat is not null)
            .ToArray();

        w.WriteLength(stats.Length);
        foreach (var (stat, value) in stats)
        {
            w.Write((ushort)stat!.Value);
            w.Write((float)value);
        }
    }

    private static void WriteWorld(ShipDefinition ship, BgoWriter w) =>
        WorldCard.Write(w, ship.Prefab, ShipRadius, targetable: true);

    private static void WriteGui(ShipDefinition ship, BgoWriter w)
    {
        w.Write(ship.Name);      // localisation key; unknown keys show as-is
        w.Write((byte)1);        // level
        w.Write(string.Empty);   // atlas texture
        w.Write((ushort)0);      // frame in the atlas
        w.Write(string.Empty);   // icon
        w.Write(string.Empty);   // avatar slot texture
        w.Write(string.Empty);   // full texture
        w.WriteLength(0);        // arguments for the localised string
    }

    /// <summary>
    /// The shop card. Ships cannot be bought yet, so every price is empty and
    /// the ship is not for sale.
    /// </summary>
    private static void WritePrice(BgoWriter w)
    {
        w.Write((byte)0);        // shop category
        w.Write((byte)0);        // item type
        w.Write((byte)1);        // tier
        w.Write((byte)0);        // faction: none, it is not on sale
        w.WriteLength(0);        // sorting names
        w.Write((ushort)0);      // sorting weight
        WriteEmptyPrice(w);      // buy
        WriteEmptyPrice(w);      // upgrade
        WriteEmptyPrice(w);      // sell
        w.Write(false);          // cannot be sold
    }

    private static void WriteEmptyPrice(BgoWriter w) => w.WriteLength(0);
}
