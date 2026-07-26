namespace Bsgo.Server.Tests;

/// <summary>
/// The client resolves assets by name, and the extension rule is not uniform:
/// it appends ".prefab" itself, while materials and textures must arrive with
/// theirs already in place.
/// </summary>
/// <remarks>
/// Getting it wrong produces no server error: the client requests an asset that
/// does not exist and draws nothing (or repeats the failure thousands of times,
/// as happened with "cic_cylon.prefab.prefab").
/// </remarks>
public class AssetNamingTests
{
    [Fact]
    public void Room_prefabs_carry_no_extension()
    {
        var catalogue = TestData.Rooms;

        foreach (var room in catalogue.Rooms)
        {
            Assert.False(room.PrefabName.EndsWith(".prefab"),
                $"\"{room.PrefabName}\": the client appends .prefab, so it must not be here");
            Assert.NotEmpty(room.PrefabName);
        }
    }

    [Fact]
    public void Avatar_pieces_carry_no_extension_either()
    {
        var catalogue = TestData.Avatars;

        foreach (var avatar in catalogue.Avatars)
            foreach (var (slot, values) in avatar.Items)
                foreach (var value in values)
                    Assert.False(value.EndsWith(".prefab"),
                        $"piece \"{value}\" in {slot} must not carry an extension");
    }

    [Fact]
    public void Materials_and_textures_do_carry_one()
    {
        var catalogue = TestData.Avatars;

        foreach (var avatar in catalogue.Avatars)
        {
            foreach (var value in avatar.Materials.Values
                         .SelectMany(m => m.Values)
                         .SelectMany(v => v)
                         .Where(v => v.Length > 0))
                Assert.EndsWith(".mat", value);

            foreach (var value in avatar.Textures.Values.SelectMany(v => v))
                Assert.True(value.EndsWith(".tga") || value.EndsWith(".png"),
                    $"texture \"{value}\" should carry .tga or .png");
        }
    }
}
