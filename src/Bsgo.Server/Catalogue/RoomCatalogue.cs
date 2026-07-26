using System.Text.Json;
using System.Text.Json.Serialization;
using Bsgo.Protocol;

namespace Bsgo.Server.Catalogue;

/// <summary>
/// A playable room (each faction's CIC).
/// </summary>
/// <remarks>
/// The client needs <b>two</b> cards with the same identifier to build the
/// scene — <c>Room</c> and <c>World</c> — and will not load it until both have
/// arrived.
/// </remarks>
public sealed class RoomDefinition
{
    [JsonPropertyName("cardGuid")]
    public uint CardGuid { get; set; }

    [JsonPropertyName("sectorId")]
    public uint SectorId { get; set; }

    /// <summary>Faction the room belongs to.</summary>
    /// <remarks>
    /// Written by name in the data file ("Colonial"), which reads far better
    /// than the enum's number.
    /// </remarks>
    [JsonPropertyName("faction")]
    [JsonConverter(typeof(JsonStringEnumConverter<Faction>))]
    public Faction Faction { get; set; }

    /// <summary>Client prefab loaded as the scenery.</summary>
    [JsonPropertyName("prefabName")]
    public string PrefabName { get; set; } = string.Empty;

    [JsonPropertyName("radius")]
    public float Radius { get; set; } = 100f;

    [JsonPropertyName("music")]
    public string Music { get; set; } = string.Empty;

    /// <summary>
    /// <c>Room</c> card: doors, characters and music.
    /// </summary>
    /// <remarks>
    /// Doors and NPCs are empty for now: the room loads and you can stand in
    /// it, but there is nowhere to go and no one to talk to.
    /// </remarks>
    public void WriteRoomCard(BgoWriter w)
    {
        w.WriteLength(0);   // doors
        w.WriteLength(0);   // NPCs
        w.Write(Music);
    }

    /// <summary>
    /// <c>World</c> card: the room's physical object in the world.
    /// </summary>
    public void WriteWorldCard(BgoWriter w)
    {
        w.Write(PrefabName);
        w.Write((byte)1);        // levels of detail
        w.Write(Radius);
        w.WriteLength(0);        // attachment spots
        w.Write(string.Empty);   // system map texture
        w.Write((sbyte)-1);      // no frame on the map
        w.Write((sbyte)0);
        w.Write(false);          // cannot be targeted
        w.Write(false);
        w.Write(false);
    }
}

/// <summary>Available rooms, loaded from their data file.</summary>
public sealed class RoomCatalogue(IReadOnlyList<RoomDefinition> rooms)
{
    public IReadOnlyList<RoomDefinition> Rooms { get; } = rooms;

    public static RoomCatalogue LoadFrom(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Room catalogue not found at \"{path}\".", path);

        using var stream = File.OpenRead(path);
        var file = JsonSerializer.Deserialize<CatalogueFile>(stream)
                   ?? throw new InvalidDataException($"Room catalogue \"{path}\" is empty or malformed.");

        return new RoomCatalogue(file.Rooms);
    }

    public RoomDefinition? Find(uint cardGuid) => Rooms.FirstOrDefault(r => r.CardGuid == cardGuid);

    /// <summary>A faction's starting room.</summary>
    public RoomDefinition? ForFaction(Faction faction) => Rooms.FirstOrDefault(r => r.Faction == faction);

    private sealed class CatalogueFile
    {
        [JsonPropertyName("rooms")]
        public List<RoomDefinition> Rooms { get; set; } = [];
    }
}
