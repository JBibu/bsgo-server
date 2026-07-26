using System.Text.Json;
using System.Text.Json.Serialization;
using Bsgo.Protocol;

namespace Bsgo.Server.Catalogue;

/// <summary>
/// One avatar option: race, sex and the pieces available for each slot.
/// </summary>
public sealed class AvatarIndex : IBgoWritable
{
    [JsonPropertyName("sex")]
    public string Sex { get; set; } = string.Empty;

    [JsonPropertyName("race")]
    public string Race { get; set; } = string.Empty;

    /// <summary>Pieces per slot, e.g. <c>head</c> → available meshes.</summary>
    [JsonPropertyName("items")]
    public Dictionary<string, List<string>> Items { get; set; } = [];

    /// <summary>Materials per slot and variant.</summary>
    [JsonPropertyName("materials")]
    public Dictionary<string, Dictionary<string, List<string>>> Materials { get; set; } = [];

    [JsonPropertyName("textures")]
    public Dictionary<string, List<string>> Textures { get; set; } = [];

    /// <summary>
    /// Serialises in the exact order the client reads: sex, race, pieces,
    /// materials and textures. The order <b>is</b> the contract; there are no
    /// tags that would reveal a field out of place.
    /// </summary>
    public void Write(BgoWriter w)
    {
        w.Write(Sex);
        w.Write(Race);
        WriteMap(w, Items);

        w.WriteLength(Materials.Count);
        foreach (var (slot, variants) in Materials)
        {
            w.Write(slot);
            WriteMap(w, variants);
        }

        WriteMap(w, Textures);
    }

    private static void WriteMap(BgoWriter w, Dictionary<string, List<string>> map)
    {
        w.WriteLength(map.Count);
        foreach (var (key, values) in map)
        {
            w.Write(key);
            w.WriteLength(values.Count);
            foreach (var value in values) w.Write(value);
        }
    }
}

/// <summary>
/// Avatar catalogue the client requests when opening character creation.
/// </summary>
/// <remarks>
/// The piece names are generated from the client's assetbundles
/// (<c>tools/generate_avatar_catalogue.py</c>), so they match meshes the
/// client can actually load. An invented name would produce an invisible
/// avatar.
/// </remarks>
public sealed class AvatarCatalogue
{
    /// <summary>
    /// Card identifier, hardcoded in the client: it always asks with this
    /// value, so it is not configurable.
    /// </summary>
    public const uint CardGuid = 109873795;

    public IReadOnlyList<AvatarIndex> Avatars { get; }

    public AvatarCatalogue(IReadOnlyList<AvatarIndex> avatars) => Avatars = avatars;

    /// <summary>Loads the catalogue from its data file.</summary>
    public static AvatarCatalogue LoadFrom(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Avatar catalogue not found at \"{path}\". " +
                "Generate it with tools/generate_avatar_catalogue.py.", path);

        using var stream = File.OpenRead(path);
        var file = JsonSerializer.Deserialize<CatalogueFile>(stream)
                   ?? throw new InvalidDataException($"Catalogue \"{path}\" is empty or malformed.");

        return new AvatarCatalogue(file.Avatars);
    }

    /// <summary>Writes the card payload: the full list of avatars.</summary>
    public void Write(BgoWriter w) => w.WriteDescList(Avatars);

    private sealed class CatalogueFile
    {
        [JsonPropertyName("avatars")]
        public List<AvatarIndex> Avatars { get; set; } = [];
    }
}
