using Bsgo.Protocol;

namespace Bsgo.Server.Tests;

/// <summary>Checks the encoding of the creation replies.</summary>
/// <remarks>
/// The store's own rules live in <see cref="PlayerStoreContract"/>.
/// </remarks>
public class CharacterCreationWireTests
{
    [Fact]
    public void The_faction_travels_as_a_single_byte()
    {
        var w = new BgoWriter(1);
        w.Write((byte)Faction.Cylon);

        Assert.Equal(new byte[] { 2 }, w.ToArray());
    }

    [Fact]
    public void WriteRaw_adds_no_length_prefix()
    {
        // Opaque blobs already carry their own structure: an extra prefix
        // would corrupt them on the way back to the client.
        var blob = new byte[] { 0xAA, 0xBB };

        var raw = new BgoWriter();
        raw.WriteRaw(blob);
        Assert.Equal(blob, raw.ToArray());

        var prefixed = new BgoWriter();
        prefixed.Write(blob);
        Assert.Equal(new byte[] { 0x02, 0x00, 0xAA, 0xBB }, prefixed.ToArray());
    }
}
