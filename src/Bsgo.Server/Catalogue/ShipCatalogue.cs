using System.Text.Json;
using System.Text.Json.Serialization;
using Bsgo.Protocol;
using Bsgo.Server.Players;

namespace Bsgo.Server.Catalogue;

/// <summary>Slot counts of a ship, by system type.</summary>
public sealed class ShipSlots
{
    [JsonPropertyName("weapon")] public int Weapon { get; set; }
    [JsonPropertyName("hull")] public int Hull { get; set; }
    [JsonPropertyName("engine")] public int Engine { get; set; }
    [JsonPropertyName("computer")] public int Computer { get; set; }
}

/// <summary>
/// One ship as the game had it.
/// </summary>
/// <remarks>
/// The stats are kept as the file writes them — the client's own names, lower
/// cased — and mapped to <see cref="ObjectStat"/> only on the way out. A ship
/// the table describes but the client has no stat for would otherwise have to
/// be dropped at load time, silently.
/// </remarks>
public sealed class ShipDefinition
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;

    [JsonPropertyName("faction")]
    [JsonConverter(typeof(JsonStringEnumConverter<Faction>))]
    public Faction Faction { get; set; }

    [JsonPropertyName("class")] public string Class { get; set; } = string.Empty;
    [JsonPropertyName("role")] public string Role { get; set; } = string.Empty;
    [JsonPropertyName("level")] public int? Level { get; set; }
    [JsonPropertyName("counterpart")] public string Counterpart { get; set; } = string.Empty;

    /// <summary>
    /// Client prefab of the 3D model, without the <c>.prefab</c> extension.
    /// </summary>
    /// <remarks>
    /// Empty for the ships whose model could not be named with any confidence.
    /// A wrong name here draws nothing and reports nothing, so an empty one —
    /// which draws nothing either — is at least honest about it.
    /// </remarks>
    [JsonPropertyName("prefab")] public string Prefab { get; set; } = string.Empty;

    /// <summary>
    /// Layout file the hangar's paperdoll is drawn from, without its path.
    /// </summary>
    /// <remarks>
    /// Not optional in practice. The client only loads it when the name is
    /// non-empty, and then reads the result unconditionally — an empty name
    /// leaves the layout null and the hangar throws on it once per frame.
    /// </remarks>
    [JsonPropertyName("paperdoll")] public string Paperdoll { get; set; } = string.Empty;

    [JsonPropertyName("slots")] public ShipSlots Slots { get; set; } = new();

    [JsonPropertyName("stats")]
    public Dictionary<string, double> Stats { get; set; } = [];

    /// <summary>
    /// Identifier the client uses for this ship, everywhere a card is asked for.
    /// </summary>
    /// <remarks>
    /// Derived from the name rather than stored: Bigpoint's original identifiers
    /// are not preserved, and any stable number will do as long as the same ship
    /// answers to it every time the server starts. Deriving it keeps the table
    /// free of numbers nobody can check.
    /// </remarks>
    public uint CardGuid => Guid(Name);

    internal static uint Guid(string name)
    {
        // FNV-1a, folded into the range the client's GUIDs occupy. Not
        // cryptographic — it only has to be stable and collision-free over 64
        // names, which ShipCatalogue checks at load.
        uint hash = 2166136261;
        foreach (var c in name)
        {
            hash ^= c;
            hash *= 16777619;
        }
        return 0x0A000000 | (hash & 0x00FFFFFF);
    }
}

/// <summary>The ships, loaded from their data file.</summary>
public sealed class ShipCatalogue
{
    private readonly Dictionary<uint, ShipDefinition> _byGuid;
    private readonly Dictionary<string, ShipDefinition> _byName;

    public ShipCatalogue(IReadOnlyList<ShipDefinition> ships)
    {
        Ships = ships;
        _byName = ships.ToDictionary(s => s.Name);

        _byGuid = [];
        foreach (var ship in ships)
        {
            if (!_byGuid.TryAdd(ship.CardGuid, ship))
                throw new InvalidDataException(
                    $"\"{ship.Name}\" and \"{_byGuid[ship.CardGuid].Name}\" derive the same "
                    + $"card identifier ({ship.CardGuid}); one of them needs renaming.");
        }
    }

    public IReadOnlyList<ShipDefinition> Ships { get; }

    public ShipDefinition? Find(uint cardGuid) => _byGuid.GetValueOrDefault(cardGuid);

    public ShipDefinition? Find(string name) => _byName.GetValueOrDefault(name);

    /// <summary>The ship a new character of this faction is given.</summary>
    /// <remarks>
    /// The two the game itself started players with. Without one the hangar
    /// window has no active ship to read and takes the client down with it.
    /// </remarks>
    public ShipDefinition? StarterFor(Faction faction) => faction switch
    {
        Faction.Colonial => Find("Viper Mark II"),
        Faction.Cylon => Find("Cylon Raider"),
        _ => null,
    };

    public static ShipCatalogue LoadFrom(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Ship catalogue not found at \"{path}\".", path);

        using var stream = File.OpenRead(path);
        var file = JsonSerializer.Deserialize<CatalogueFile>(stream)
                   ?? throw new InvalidDataException($"Ship catalogue \"{path}\" is empty or malformed.");

        return new ShipCatalogue(file.Ships);
    }

    private sealed class CatalogueFile
    {
        [JsonPropertyName("ships")]
        public List<ShipDefinition> Ships { get; set; } = [];
    }
}
