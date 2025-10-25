using System.Security.Claims;
using Shared.Security;

namespace Shared.Tests.Security;

public class ClaimsPrincipalExtensionsTests
{
    [Fact]
    public void HasScope_ReturnsTrue_WhenScopePresent()
    {
        var principal = CreatePrincipal("orders.read inventory.write");

        Assert.True(principal.HasScope("inventory.write"));
        Assert.False(principal.HasScope("orders.write"));
    }

    [Fact]
    public void HasAnyScope_ReturnsTrue_WhenAnyScopeMatches()
    {
        var principal = CreatePrincipal("orders.write inventory.read");

        Assert.True(principal.HasAnyScope("inventory.write", "orders.write"));
        Assert.False(principal.HasAnyScope("inventory.write", "inventory.delete"));
    }

    private static ClaimsPrincipal CreatePrincipal(string scopeValue)
    {
        var claims = new List<Claim> { new("scope", scopeValue) };
        var identity = new ClaimsIdentity(claims, "test");
        return new ClaimsPrincipal(identity);
    }
}
