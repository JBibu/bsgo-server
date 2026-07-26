using Bsgo.Protocol;
using Bsgo.Server.Players;
using Xunit;

namespace Bsgo.Server.Tests;

/// <summary>Character store rules, without going through the network.</summary>
public class PlayerStoreTests
{
    [Fact]
    public void A_new_player_starts_with_no_faction_and_no_name()
    {
        var store = new InMemoryPlayerStore();
        var player = store.GetOrCreate(42);

        Assert.Equal(42u, player.Id);
        Assert.Equal(Faction.Neutral, player.Faction);
        Assert.Empty(player.Name);
        Assert.False(player.IsCreated);
    }

    [Fact]
    public void A_character_counts_as_created_only_with_faction_and_name()
    {
        var store = new InMemoryPlayerStore();
        var player = store.GetOrCreate(1);

        player.Faction = Faction.Colonial;
        Assert.False(player.IsCreated);   // still unnamed

        player.Name = "Starbuck";
        Assert.True(player.IsCreated);
    }

    [Fact]
    public void A_name_taken_by_someone_else_is_not_available()
    {
        var store = new InMemoryPlayerStore();
        var first = store.GetOrCreate(1);
        first.Name = "Starbuck";
        store.Save(first);

        Assert.False(store.IsNameAvailable("Starbuck", requestingPlayerId: 2));
        Assert.False(store.IsNameAvailable("starbuck", requestingPlayerId: 2));   // case-insensitive
        Assert.True(store.IsNameAvailable("Apollo", requestingPlayerId: 2));
    }

    [Fact]
    public void Your_own_name_stays_available_to_you()
    {
        // Otherwise re-sending the same name on confirmation would be rejected.
        var store = new InMemoryPlayerStore();
        var player = store.GetOrCreate(1);
        player.Name = "Starbuck";
        store.Save(player);

        Assert.True(store.IsNameAvailable("Starbuck", requestingPlayerId: 1));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_name_is_never_available(string name)
    {
        var store = new InMemoryPlayerStore();
        Assert.False(store.IsNameAvailable(name, requestingPlayerId: 1));
    }

    [Fact]
    public void Opaque_blobs_are_stored_unmodified()
    {
        // Only the client interprets settings and avatar: they must come back identical.
        var store = new InMemoryPlayerStore();
        var player = store.GetOrCreate(1);
        var blob = new byte[] { 0x00, 0xFF, 0x42, 0x00 };

        player.Settings = blob;
        player.AvatarDescription = blob;
        store.Save(player);

        Assert.Equal(blob, store.GetOrCreate(1).Settings);
        Assert.Equal(blob, store.GetOrCreate(1).AvatarDescription);
    }
}

/// <summary>Checks the encoding of the creation replies.</summary>
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
