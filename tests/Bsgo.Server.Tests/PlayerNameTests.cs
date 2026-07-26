using Bsgo.Server.Players;

namespace Bsgo.Server.Tests;

/// <summary>
/// Name rules are enforced on the server: the client can send anything, and the
/// name ends up seen by everyone else.
/// </summary>
/// <remarks>
/// How a store applies these rules is checked in <see cref="PlayerStoreContract"/>,
/// against every implementation. Here only the rule itself is examined.
/// </remarks>
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
}
