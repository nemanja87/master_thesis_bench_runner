using System;
using System.Linq;
using System.Net.Http;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using InventoryService.Models;
using InventoryService.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Logging;
using Prometheus;
using Shared.Security;

var builder = WebApplication.CreateBuilder(args);

var requiresHttps = SecurityProfileDefaults.RequiresHttps();
var requiresMtls = SecurityProfileDefaults.RequiresMtls();
var requiresJwt = SecurityProfileDefaults.RequiresJwt();
var requiresPerMethodPolicies = SecurityProfileDefaults.RequiresPerMethodPolicies();

var serverCertificatePath = builder.Configuration["BENCH_Security__Tls__ServerCertificatePath"]
    ?? "/certs/servers/inventoryservice/inventoryservice.pfx";
var serverCertificatePassword = builder.Configuration["BENCH_Security__Tls__ServerCertificatePassword"];
var caCertificatePath = builder.Configuration["BENCH_Security__Tls__CaCertificatePath"] ?? "/certs/ca/ca.crt.pem";

var serverCertificate = requiresHttps
    ? CertificateUtilities.TryLoadPfxCertificate(serverCertificatePath, serverCertificatePassword, optional: false)
    : CertificateUtilities.TryLoadPfxCertificate(serverCertificatePath, serverCertificatePassword, optional: true);
var caCertificate = CertificateUtilities.TryLoadPemCertificate(
    caCertificatePath,
    optional: !(requiresHttps || requiresJwt || requiresMtls));

if (requiresHttps && serverCertificate is null)
{
    throw new InvalidOperationException($"Server certificate not found at '{serverCertificatePath}'.");
}

if ((requiresHttps || requiresJwt || requiresMtls) && caCertificate is null)
{
    throw new InvalidOperationException($"CA certificate '{caCertificatePath}' could not be loaded.");
}

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(8082, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http1;
        if (requiresHttps && serverCertificate is not null)
        {
            listenOptions.UseHttps(serverCertificate, ConfigureHttpsOptions);
        }
    });

    options.ListenAnyIP(9092, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http2;
        if (requiresHttps && serverCertificate is not null)
        {
            listenOptions.UseHttps(serverCertificate, ConfigureHttpsOptions);
        }
    });
});

builder.Services.AddAuthorization(options =>
{
    if (requiresPerMethodPolicies)
    {
        options.AddPolicy("Inventory.Write", policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireAssertion(ctx => ctx.User.HasScope("inventory.write"));
        });
    }

    if (requiresJwt && !requiresPerMethodPolicies)
    {
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
    }
});

SocketsHttpHandler? jwtBackchannelHandler = null;

if (requiresJwt)
{
    var authority = builder.Configuration["BENCH_Security__Jwt__Authority"] ?? "https://authserver:5001";
    var authorityTrimmed = authority.TrimEnd('/');
    var metadataAddress = builder.Configuration["BENCH_Security__Jwt__MetadataAddress"]
        ?? $"{authorityTrimmed}/.well-known/openid-configuration";
    var clientCertificatePath = builder.Configuration["BENCH_Security__Tls__ClientCertificatePath"];
    var clientCertificateKeyPath = builder.Configuration["BENCH_Security__Tls__ClientCertificateKeyPath"];
    var clientCertificate = CertificateUtilities.TryLoadPemCertificate(clientCertificatePath, clientCertificateKeyPath);

    jwtBackchannelHandler = HttpHandlerFactory.CreateBackchannelHandler(caCertificate, clientCertificate);

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = authorityTrimmed;
            options.MetadataAddress = metadataAddress;
            options.RequireHttpsMetadata = true;
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuers = new[] { authorityTrimmed, $"{authorityTrimmed}/" },
                ValidateAudience = false,
                NameClaimType = "name",
                RoleClaimType = "role"
            };
            options.BackchannelHttpHandler = jwtBackchannelHandler;
        });
}

builder.Services.AddSingleton<InventoryReserveStore>();

var app = builder.Build();

if (jwtBackchannelHandler is not null)
{
    app.Lifetime.ApplicationStopping.Register(jwtBackchannelHandler.Dispose);
}

app.UseRouting();
app.UseHttpMetrics();

if (requiresMtls)
{
    app.Use(async (context, next) =>
    {
        var certificate = await context.Connection.GetClientCertificateAsync();
        if (certificate is not null)
        {
            app.Logger.LogInformation("InventoryService accepted client certificate. Subject={Subject}", certificate.Subject);
        }

        await next();
    });
}

if (requiresJwt)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.MapMetrics();

var inventoryGroup = app.MapGroup("/api/inventory");
if (requiresJwt && !requiresPerMethodPolicies)
{
    inventoryGroup.RequireAuthorization();
}

var reserveEndpoint = inventoryGroup.MapPost("reserve", (InventoryReserveRequest request, InventoryReserveStore store, ILoggerFactory loggerFactory) =>
{
    var record = new InventoryReserveRecord(
        request.OrderId,
        request.CustomerId,
        DateTimeOffset.UtcNow,
        request.ItemSkus,
        request.TotalAmount);

    store.Record(record);
    loggerFactory.CreateLogger("InventoryReserve").LogInformation(
        "Reserve accepted for order {OrderId} (items={ItemCount}, total={Total:C})",
        record.OrderId,
        record.ItemSkus.Count,
        record.TotalAmount);

    return Results.NoContent();
})
.Produces(StatusCodes.Status204NoContent)
.WithName("ReserveInventory");

if (requiresJwt && requiresPerMethodPolicies)
{
    reserveEndpoint.RequireAuthorization("Inventory.Write");
}

app.MapGet("/healthz", (InventoryReserveStore store) => Results.Json(new
{
    status = "ok",
    profile = SecurityProfileDefaults.CurrentProfile.ToString(),
    requiresHttps,
    requiresMtls,
    requiresJwt,
    recentReservations = store.Snapshot().Select(record => new
    {
        record.OrderId,
        record.CustomerId,
        record.TotalAmount,
        record.RequestedAt
    })
}));

app.Logger.LogInformation("InventoryService started with profile {Profile}", SecurityProfileDefaults.CurrentProfile);

app.Run();

void ConfigureHttpsOptions(HttpsConnectionAdapterOptions options)
{
    options.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
    options.ClientCertificateMode = requiresMtls ? ClientCertificateMode.RequireCertificate : ClientCertificateMode.AllowCertificate;
    if (requiresMtls)
    {
        options.ClientCertificateValidation = (certificate, _, _) => ValidateClientCertificate(certificate);
    }
}

bool ValidateClientCertificate(X509Certificate2? certificate)
{
    if (certificate is null || caCertificate is null)
    {
        return false;
    }

    using var chain = new X509Chain
    {
        ChainPolicy =
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = X509RevocationMode.NoCheck,
            VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority
        }
    };

    chain.ChainPolicy.CustomTrustStore.Add(caCertificate);
    return chain.Build(certificate);
}
