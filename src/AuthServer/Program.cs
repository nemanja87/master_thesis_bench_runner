using AuthServer;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using Shared.Security;
using static OpenIddict.Abstractions.OpenIddictConstants;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var serverCertificatePath = builder.Configuration["BENCH_Security__Tls__ServerCertificatePath"]
    ?? "/certs/servers/authserver/authserver.pfx";
var serverCertificatePassword = builder.Configuration["BENCH_Security__Tls__ServerCertificatePassword"];
var signingCertificatePath = builder.Configuration["BENCH_Security__Jwt__SigningCertificatePath"] ?? serverCertificatePath;
var signingCertificatePassword = builder.Configuration["BENCH_Security__Jwt__SigningCertificatePassword"]
    ?? serverCertificatePassword;
var caCertificatePath = builder.Configuration["BENCH_Security__Tls__CaCertificatePath"] ?? "/certs/ca/ca.crt.pem";

var serverCertificate = CertificateUtilities.TryLoadPfxCertificate(serverCertificatePath, serverCertificatePassword, optional: false)
    ?? throw new InvalidOperationException($"Unable to load server certificate from '{serverCertificatePath}'.");
var signingCertificate = CertificateUtilities.TryLoadPfxCertificate(signingCertificatePath, signingCertificatePassword, optional: false)
    ?? throw new InvalidOperationException($"Unable to load signing certificate from '{signingCertificatePath}'.");
var caCertificate = CertificateUtilities.TryLoadPemCertificate(caCertificatePath);

if (SecurityProfileDefaults.RequiresMtls() && caCertificate is null)
{
    throw new InvalidOperationException("mTLS is enabled but the CA certificate could not be loaded.");
}

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5001, listenOptions =>
    {
        listenOptions.UseHttps(serverCertificate, httpsOptions =>
        {
            if (SecurityProfileDefaults.RequiresMtls())
            {
                httpsOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                httpsOptions.ClientCertificateValidation = (certificate, chain, errors) =>
                {
                    if (certificate is null || caCertificate is null)
                    {
                        return false;
                    }

                    using var chainToValidate = new X509Chain
                    {
                        ChainPolicy =
                        {
                            RevocationMode = X509RevocationMode.NoCheck,
                            VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority,
                            TrustMode = X509ChainTrustMode.CustomRootTrust
                        }
                    };

                    chainToValidate.ChainPolicy.CustomTrustStore.Add(caCertificate);
                    return chainToValidate.Build(certificate);
                };
            }
            else
            {
                httpsOptions.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
            }
        });
    });
});

builder.Services
    .AddOpenIddict()
    .AddServer(options =>
    {
        options.SetIssuer(new Uri(builder.Configuration["Auth__Issuer"] ?? AuthConstants.Issuer));
        options.SetTokenEndpointUris("/connect/token");
        options.SetJsonWebKeySetEndpointUris("/.well-known/jwks");
        options.SetConfigurationEndpointUris("/.well-known/openid-configuration");

        options.AllowClientCredentialsFlow();
        options.AcceptAnonymousClients();
        options.DisableAccessTokenEncryption();
        options.AddSigningCertificate(signingCertificate);
        options.AddEphemeralEncryptionKey();
        options.EnableDegradedMode();
        options.DisableTokenStorage();
        options.RegisterScopes(AuthConstants.AllowedScopes);
        options.DisableScopeValidation();

        options.AddEventHandler<OpenIddictServerEvents.ValidateTokenRequestContext>(handlerBuilder =>
        {
            handlerBuilder.UseInlineHandler(context =>
            {
                if (!string.Equals(context.ClientId, AuthConstants.ClientId, StringComparison.Ordinal))
                {
                    context.Reject(error: Errors.InvalidClient, description: "Unknown client_id.");
                    return ValueTask.CompletedTask;
                }

                if (!string.Equals(context.ClientSecret, AuthConstants.ClientSecret, StringComparison.Ordinal))
                {
                    context.Reject(error: Errors.InvalidClient, description: "Invalid client secret.");
                    return ValueTask.CompletedTask;
                }

                return ValueTask.CompletedTask;
            });
        });

        options.AddEventHandler<OpenIddictServerEvents.HandleTokenRequestContext>(handlerBuilder =>
        {
            handlerBuilder.UseInlineHandler(context =>
            {
                if (!context.Request.IsClientCredentialsGrantType())
                {
                    context.Reject(error: Errors.UnsupportedGrantType, description: "Only client_credentials is supported.");
                    return ValueTask.CompletedTask;
                }

                if (!string.Equals(context.ClientId, AuthConstants.ClientId, StringComparison.Ordinal))
                {
                    context.Reject(error: Errors.InvalidClient, description: "Unknown client_id.");
                    return ValueTask.CompletedTask;
                }

                if (!string.Equals(context.Request.ClientSecret, AuthConstants.ClientSecret, StringComparison.Ordinal))
                {
                    context.Reject(error: Errors.InvalidClient, description: "Invalid client secret.");
                    return ValueTask.CompletedTask;
                }

                var requestedScopes = (context.Request.Scope ?? string.Empty)
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var grantedScopes = requestedScopes.Intersect(AuthConstants.AllowedScopes, StringComparer.Ordinal).ToArray();

                var identity = new ClaimsIdentity(TokenTypes.Bearer);
                identity.SetClaim(Claims.Subject, AuthConstants.ClientId);
                identity.SetClaim(Claims.ClientId, AuthConstants.ClientId);
                identity.SetClaim(Claims.Name, AuthConstants.ClientId);

                var principal = new ClaimsPrincipal(identity);
                principal.SetScopes(grantedScopes.Length > 0 ? grantedScopes : AuthConstants.AllowedScopes);
                principal.SetResources("gateway", "orderservice", "inventoryservice");
                principal.SetDestinations(static claim => claim.Type switch
                {
                    Claims.Subject or Claims.Name or Claims.ClientId => new[] { Destinations.AccessToken },
                    _ => Array.Empty<string>()
                });

                context.SignIn(principal);
                return ValueTask.CompletedTask;
            });
        });

        options.UseAspNetCore()
            .EnableStatusCodePagesIntegration();
    });

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (SecurityProfileDefaults.RequiresMtls())
{
    app.Use(async (context, next) =>
    {
        var certificate = await context.Connection.GetClientCertificateAsync();
        if (certificate is not null)
        {
            app.Logger.LogInformation("mTLS client certificate accepted. Subject={Subject}", certificate.Subject);
        }

        await next();
    });
}

app.UseHttpsRedirection();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
