using Bsgo.Protocol;
using Bsgo.Server.Catalogue;
using Xunit;

namespace Bsgo.Server.Tests;

/// <summary>
/// The catalogue is serialised without tags: field order is the only contract,
/// so these tests re-read it field by field just as the client does. One field
/// too many or too few shifts everything that follows.
/// </summary>
public class AvatarCatalogueTests
{
    private static AvatarIndex SampleCylon() => new()
    {
        Sex = "centurion",
        Race = "cylon",
        Items = new Dictionary<string, List<string>>
        {
            ["CylonHead"] = ["centurion_head_v1", "centurion_head_v2"],
            ["CylonHeadSkin"] = [""],
        },
    };

    [Fact]
    public void An_avatar_is_re_read_field_by_field_in_the_expected_order()
    {
        var w = new BgoWriter();
        SampleCylon().Write(w);

        var r = new BgoReader(w.WrittenSpan);
        Assert.Equal("centurion", r.ReadString());   // sex first
        Assert.Equal("cylon", r.ReadString());       // race second

        Assert.Equal(2, r.ReadUInt16());             // two slots
        Assert.Equal("CylonHead", r.ReadString());
        Assert.Equal(2, r.ReadUInt16());
        Assert.Equal("centurion_head_v1", r.ReadString());
        Assert.Equal("centurion_head_v2", r.ReadString());
        Assert.Equal("CylonHeadSkin", r.ReadString());
        Assert.Equal(1, r.ReadUInt16());
        Assert.Equal("", r.ReadString());

        Assert.Equal(0, r.ReadUInt16());             // no materials
        Assert.Equal(0, r.ReadUInt16());             // no textures
        Assert.Equal(0, r.Remaining);                // nothing left over
    }

    [Fact]
    public void Materials_nest_slot_variant_and_values()
    {
        var avatar = new AvatarIndex
        {
            Sex = "male",
            Race = "human",
            Materials = new Dictionary<string, Dictionary<string, List<string>>>
            {
                ["hair"] = new() { ["blond"] = ["male_hair_03_5.mat"] },
            },
        };

        var w = new BgoWriter();
        avatar.Write(w);

        var r = new BgoReader(w.WrittenSpan);
        r.ReadString();                       // sex
        r.ReadString();                       // race
        Assert.Equal(0, r.ReadUInt16());      // no pieces

        Assert.Equal(1, r.ReadUInt16());      // one material slot
        Assert.Equal("hair", r.ReadString());
        Assert.Equal(1, r.ReadUInt16());      // one variant
        Assert.Equal("blond", r.ReadString());
        Assert.Equal(1, r.ReadUInt16());
        Assert.Equal("male_hair_03_5.mat", r.ReadString());

        Assert.Equal(0, r.ReadUInt16());      // no textures
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void The_catalogue_leads_with_the_avatar_count()
    {
        var catalogue = new AvatarCatalogue([SampleCylon(), SampleCylon()]);

        var w = new BgoWriter();
        catalogue.Write(w);

        var r = new BgoReader(w.WrittenSpan);
        Assert.Equal(2, r.ReadUInt16());
        Assert.Equal("centurion", r.ReadString());
    }

    [Fact]
    public void A_missing_data_file_produces_an_error_saying_how_to_generate_it()
    {
        var ex = Assert.Throws<FileNotFoundException>(
            () => AvatarCatalogue.LoadFrom("/no/existe/avatar-catalogue.json"));

        Assert.Contains("generate_avatar_catalogue.py", ex.Message);
    }

    [Fact]
    public void The_generated_catalogue_covers_both_factions()
    {
        // Uses the real file shipped with the server.
        var catalogue = TestData.Avatars;

        Assert.Contains(catalogue.Avatars, a => a.Race == "cylon");
        Assert.Contains(catalogue.Avatars, a => a.Race == "human");

        var cylon = catalogue.Avatars.First(a => a.Race == "cylon");

        // The client looks the keys up by exact name: if one is missing it
        // throws KeyNotFoundException reading the card and draws nothing.
        Assert.Equal(["arms", "body", "head", "legs"], cylon.Items.Keys.Order());
        Assert.Equal(["arms_", "body_", "head_", "legs_"], cylon.Materials.Keys.Order());

        // The names must come from the client's real meshes: an invented one
        // would produce an invisible avatar, with no error at all.
        Assert.Contains("centurion_head_v1", cylon.Items["head"]);
        Assert.Contains("centurion_body_v1", cylon.Items["body"]);
        Assert.Contains("centurion_arms_v1", cylon.Items["arms"]);
        Assert.Contains("centurion_legs_v1", cylon.Items["legs"]);

        // Colours are indexed by the specific mesh they apply to, and carry an
        // extension: the client uses the value verbatim as an asset name, so
        // without ".mat" nothing loads and the colour is not applied.
        Assert.Contains("centurion_arms_v1_black_1.mat", cylon.Materials["arms_"]["centurion_arms_v1"]);
    }


    [Fact]
    public void Colonials_expose_every_option_present_in_the_assets()
    {
        var male = TestData.Avatars.Avatars.First(a => a.Sex == "male");

        // One piece per slot is not enough: the client lets you choose among
        // every piece the bundle ships.
        Assert.True(male.Items["hair"].Count >= 10, "all 10 hairstyles should be present");
        Assert.True(male.Items["head"].Count >= 8, "all heads should be present");
        Assert.True(male.Textures["faces_tex"].Count >= 10, "all faces should be present");
        Assert.True(male.Textures["hands_tex"].Count >= 10, "all hands should be present");

        // The "none" option must be selectable.
        Assert.Contains("helmet_empty", male.Items["helmet"]);
        Assert.Contains("glasses_empty", male.Items["glasses"]);
        Assert.Contains("volume_beard_empty", male.Items["beard"]);
    }

    [Fact]
    public void Faces_are_ordered_numerically_not_alphabetically()
    {
        var male = TestData.Avatars.Avatars.First(a => a.Sex == "male");

        var faces = male.Textures["faces_tex"];
        Assert.Equal("male_face_1.tga", faces[0]);
        Assert.Equal("male_face_2.tga", faces[1]);   // not male_face_10
    }

    [Fact]
    public void The_human_avatar_declares_the_keys_the_client_looks_up()
    {
        var human = TestData.Avatars.Avatars.First(a => a.Race == "human");

        Assert.Equal(
            ["beard", "glasses", "hair", "head", "helmet", "suit"],
            human.Items.Keys.Order());
        Assert.Equal(["beard_", "hair_"], human.Materials.Keys.Order());
        Assert.Equal(["faces_tex", "hands_tex"], human.Textures.Keys.Order());
    }
}
