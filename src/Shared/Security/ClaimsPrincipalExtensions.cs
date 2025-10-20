using System;
using System.Linq;
using System.Security.Claims;

namespace Shared.Security;

public static class ClaimsPrincipalExtensions
{
    public static bool HasScope(this ClaimsPrincipal principal, string scope)
    {
        if (principal is null)
        {
            return false;
        }

        return principal.FindAll("scope")
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Any(value => string.Equals(value, scope, StringComparison.Ordinal));
    }

    public static bool HasAnyScope(this ClaimsPrincipal principal, params string[] scopes)
    {
        if (scopes.Length == 0)
        {
            return true;
        }

        return scopes.Any(principal.HasScope);
    }
}
