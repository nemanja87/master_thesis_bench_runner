using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InventoryService.Tests.Infrastructure;

internal sealed class TestAuthOptions : AuthenticationSchemeOptions;

internal sealed class TestAuthHandler : AuthenticationHandler<TestAuthOptions>
{
    public const string SchemeName = "Test";
    private const string AuthHeader = "X-Test-Auth";
    private const string ScopesHeader = "X-Test-Scopes";

    #pragma warning disable CS0618
    public TestAuthHandler(
        IOptionsMonitor<TestAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock) : base(options, logger, encoder, clock)
    {
    }
    #pragma warning restore CS0618

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(AuthHeader, out var token) || string.IsNullOrWhiteSpace(token))
        {
            return Task.FromResult(AuthenticateResult.Fail("Missing authentication header."));
        }

        var scopes = Request.Headers.TryGetValue(ScopesHeader, out var scopeHeader)
            ? scopeHeader.ToString().Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : Array.Empty<string>();

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "inventory-service-test") };
        if (scopes.Length > 0)
        {
            claims.Add(new Claim("scope", string.Join(' ', scopes)));
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
