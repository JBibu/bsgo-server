using Bsgo.Server.Players;
using Xunit;

namespace Bsgo.Server.Tests;

/// <summary>
/// The client keeps the identifier the server gives it and identifies with it
/// from then on; it sends 0 the first time. Handing that 0 back made every
/// player share one character, and the mistake fed itself: the client stored
/// the 0 and sent it again on the next start.
/// </summary>
public class PlayerIdentityTests
{
    [Fact]
    public void Assigned_identifiers_never_repeat()
    {
        var store = new InMemoryPlayerStore();

        var ids = Enumerable.Range(0, 100).Select(_ => store.AllocatePlayerId()).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Zero_is_never_assigned()
    {
        var store = new InMemoryPlayerStore();

        // 0 is the "I have no identifier yet" signal: assigning it would
        // reintroduce the problem.
        Assert.All(
            Enumerable.Range(0, 50).Select(_ => store.AllocatePlayerId()),
            id => Assert.NotEqual(0u, id));
    }

    [Fact]
    public void An_assigned_identifier_never_clobbers_an_existing_character()
    {
        var store = new InMemoryPlayerStore();
        var first = store.AllocatePlayerId();

        var player = store.GetOrCreate(first);
        player.Name = "Starbuck";
        store.Save(player);

        var second = store.AllocatePlayerId();

        Assert.NotEqual(first, second);
        Assert.Equal("Starbuck", store.GetOrCreate(first).Name);
        Assert.Empty(store.GetOrCreate(second).Name);
    }

    [Fact]
    public void Two_players_keep_separate_characters()
    {
        var store = new InMemoryPlayerStore();

        var a = store.GetOrCreate(store.AllocatePlayerId());
        a.Name = "Starbuck";
        a.Faction = Faction.Colonial;
        store.Save(a);

        var b = store.GetOrCreate(store.AllocatePlayerId());
        b.Name = "Apollo";
        b.Faction = Faction.Cylon;
        store.Save(b);

        Assert.Equal("Starbuck", store.GetOrCreate(a.Id).Name);
        Assert.Equal(Faction.Colonial, store.GetOrCreate(a.Id).Faction);
        Assert.Equal("Apollo", store.GetOrCreate(b.Id).Name);
        Assert.Equal(Faction.Cylon, store.GetOrCreate(b.Id).Faction);
    }
}

/// <summary>
/// Name rules are enforced on the server: the client can send anything, and the
/// name ends up seen by everyone else.
/// </summary>
public class PlayerNameTests
{
    [Theory]
    [InlineData("Starbuck")]
    [InlineData("Apollo_1")]
    [InlineData("Six-2")]
    [InlineData("abc")]                  // minimum length
    [InlineData("1234567890123456")]     // maximum length
    public void Reasonable_names_are_accepted(string name) =>
        Assert.True(PlayerName.IsValid(name));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ab")]                    // too short
    [InlineData("12345678901234567")]     // too long
    [InlineData("Star buck")]             // spaces
    [InlineData("<script>")]              // markup characters
    [InlineData("Star\nbuck")]            // line breaks
    [InlineData("nombre;drop")]
    public void Problematic_names_are_rejected(string name) =>
        Assert.False(PlayerName.IsValid(name));

    [Fact]
    public void An_invalid_name_is_not_available_either()
    {
        var store = new InMemoryPlayerStore();

        // Without this, validation could be bypassed by going straight to naming.
        Assert.False(store.IsNameAvailable("ab", requestingPlayerId: 1));
        Assert.False(store.IsNameAvailable("Star buck", requestingPlayerId: 1));
        Assert.True(store.IsNameAvailable("Starbuck", requestingPlayerId: 1));
    }
}
