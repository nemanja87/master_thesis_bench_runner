using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Gateway.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Logging;
using Shared.Security;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

var builder = WebApplication.CreateBuilder(args);

static string? GetBenchmarkSetting(IConfiguration configuration, string key)
{
    var envValue = Environment.GetEnvironmentVariable(key);
    if (!string.IsNullOrWhiteSpace(envValue))
    {
        return envValue;
    }

    var colonKey = key.Replace("__", ":", StringComparison.Ordinal);
    return configuration[colonKey] ?? configuration[key];
}

var requiresHttps = SecurityProfileDefaults.RequiresHttps();
var requiresMtls = SecurityProfileDefaults.RequiresMtls();
var requiresJwt = SecurityProfileDefaults.RequiresJwt();

var serverCertificatePath = GetBenchmarkSetting(builder.Configuration, "BENCH_Security__Tls__ServerCertificatePath")
    ?? "/certs/servers/gateway/gateway.pfx";
var serverCertificatePassword = GetBenchmarkSetting(builder.Configuration, "BENCH_Security__Tls__ServerCertificatePassword");
var caCertificatePath = GetBenchmarkSetting(builder.Configuration, "BENCH_Security__Tls__CaCertificatePath") ?? "/certs/ca/ca.crt.pem";
var serverCertificate = requiresHttps
    ? CertificateUtilities.TryLoadPfxCertificate(serverCertificatePath, serverCertificatePassword, optional: false)
    : CertificateUtilities.TryLoadPfxCertificate(serverCertificatePath, serverCertificatePassword, optional: true);
var caCertificate = CertificateUtilities.TryLoadPemCertificate(
    caCertificatePath,
    optional: !(requiresHttps || requiresJwt || requiresMtls));
var clientCertificatePath = GetBenchmarkSetting(builder.Configuration, "BENCH_Security__Tls__ClientCertificatePath");
var clientCertificateKeyPath = GetBenchmarkSetting(builder.Configuration, "BENCH_Security__Tls__ClientCertificateKeyPath");
Console.WriteLine($"Gateway TLS configuration: requiresMtls={requiresMtls}, clientCertPath='{clientCertificatePath ?? "<null>"}', clientKeyPath='{clientCertificateKeyPath ?? "<null>"}'");
var clientCertificate = CertificateUtilities.TryLoadPemCertificate(
    clientCertificatePath,
    clientCertificateKeyPath,
    optional: !(requiresMtls || requiresJwt));

if (requiresHttps && serverCertificate is null)
{
    throw new InvalidOperationException($"SEC_PROFILE requires TLS but server certificate was not found at '{serverCertificatePath}'.");
}

if ((requiresJwt || requiresMtls || requiresHttps) && caCertificate is null)
{
    throw new InvalidOperationException($"Certificate authority file '{caCertificatePath}' could not be loaded.");
}

builder.Services.AddOpenApi();
builder.Services.AddSingleton<GatewayHandshakeTracker>();

var routes = new[]
{
    new RouteConfig
    {
        RouteId = "orders-rest",
        ClusterId = "orders-rest",
        Match = new RouteMatch { Path = "/orders/{**catchall}" },
        Order = 1,
        Transforms = new List<Dictionary<string, string>>
        {
            new() { ["PathRemovePrefix"] = "/orders" }
        }
    },
    new RouteConfig
    {
        RouteId = "inventory-rest",
        ClusterId = "inventory-rest",
        Match = new RouteMatch { Path = "/inventory/{**catchall}" },
        Order = 2,
        Transforms = new List<Dictionary<string, string>>
        {
            new() { ["PathRemovePrefix"] = "/inventory" }
        }
    },
    new RouteConfig
    {
        RouteId = "orders-grpc",
        ClusterId = "orders-grpc",
        Match = new RouteMatch { Path = "/{**catchall}" },
        Order = 100
    }
};

var clusters = new[]
{
    new ClusterConfig
    {
        ClusterId = "orders-rest",
        Destinations = new Dictionary<string, DestinationConfig>
        {
            ["orders-rest-1"] = new() { Address = $"{(requiresHttps ? "https" : "http")}://orderservice:8081/" }
        },
        HttpClient = requiresHttps
            ? new HttpClientConfig { DangerousAcceptAnyServerCertificate = true }
            : null
    },
    new ClusterConfig
    {
        ClusterId = "inventory-rest",
        Destinations = new Dictionary<string, DestinationConfig>
        {
            ["inventory-rest-1"] = new() { Address = $"{(requiresHttps ? "https" : "http")}://inventoryservice:8082/" }
        },
        HttpClient = requiresHttps
            ? new HttpClientConfig { DangerousAcceptAnyServerCertificate = true }
            : null
    },
    new ClusterConfig
    {
        ClusterId = "orders-grpc",
        Destinations = new Dictionary<string, DestinationConfig>
        {
            ["orders-grpc-1"] = new() { Address = $"{(requiresHttps ? "https" : "http")}://orderservice:9091/" }
        },
        HttpRequest = new ForwarderRequestConfig
        {
            Version = new Version(2, 0),
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        },
        HttpClient = requiresHttps
            ? new HttpClientConfig { DangerousAcceptAnyServerCertificate = true }
            : null
    }
};

builder.Services
    .AddReverseProxy()
    .LoadFromMemory(routes, clusters);

if (requiresMtls)
{
    builder.Services.AddSingleton<IForwarderHttpClientFactory>(_ =>
        new MtlsProxyHttpClientFactory(clientCertificate, caCertificate));
}

SocketsHttpHandler? jwtBackchannelHandler = null;
OpenIdConnectConfiguration? preloadedOpenIdConfiguration = null;
Exception? jwksPreloadException = null;
string? authorityOverride = null;

if (requiresJwt)
{
    var authority = builder.Configuration["BENCH_Security__Jwt__Authority"] ?? "https://authserver:5001";
    authorityOverride = authority.TrimEnd('/');
    var metadataAddress = builder.Configuration["BENCH_Security__Jwt__MetadataAddress"]
        ?? $"{authorityOverride}/.well-known/openid-configuration";

    jwtBackchannelHandler = HttpHandlerFactory.CreateBackchannelHandler(caCertificate, clientCertificate);

    var jwksUri = new Uri($"{authorityOverride}/.well-known/jwks");
    (preloadedOpenIdConfiguration, jwksPreloadException) =
        await TryPreloadConfigurationAsync(jwtBackchannelHandler, jwksUri, authorityOverride);

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = authorityOverride;
            options.MetadataAddress = metadataAddress;
            options.RequireHttpsMetadata = true;
            options.BackchannelHttpHandler = jwtBackchannelHandler;
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuers = new[]
                {
                    authorityOverride,
                    $"{authorityOverride}/"
                },
                ValidateAudience = false
            };

           if (preloadedOpenIdConfiguration is not null)
           {
               options.Configuration = preloadedOpenIdConfiguration;
                options.TokenValidationParameters.IssuerSigningKeys = preloadedOpenIdConfiguration.SigningKeys;
           }
        });

    builder.Services.AddAuthorization();
}
else
{
    builder.Services.AddAuthorization();
}

builder.WebHost.ConfigureKestrel(options =>
{
    options.ConfigureEndpointDefaults(static endpointOptions =>
    {
        endpointOptions.Protocols = HttpProtocols.Http1AndHttp2;
    });

    options.ListenAnyIP(8080, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http1;
        if (requiresHttps && serverCertificate is not null)
        {
            listenOptions.UseHttps(serverCertificate, ConfigureHttpsOptions);
        }
    });

    options.ListenAnyIP(9090, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http2;
        if (requiresHttps && serverCertificate is not null)
        {
            listenOptions.UseHttps(serverCertificate, ConfigureHttpsOptions);
        }
    });
});

var app = builder.Build();

if (jwtBackchannelHandler is not null)
{
    app.Lifetime.ApplicationStopping.Register(() => jwtBackchannelHandler.Dispose());
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseRouting();

if (requiresMtls)
{
    app.Use(async (context, next) =>
    {
        var certificate = await context.Connection.GetClientCertificateAsync();
        if (certificate is not null)
        {
            var tracker = context.RequestServices.GetRequiredService<GatewayHandshakeTracker>();
            tracker.Record(certificate.Subject);
            app.Logger.LogInformation("Gateway accepted client certificate. Subject={Subject}", certificate.Subject);
        }

        await next();
    });
}

if (requiresJwt)
{
    foreach (var logAction in BuildJwtLogActions(authorityOverride!, jwksPreloadException))
    {
        logAction(app.Logger);
    }

    app.UseAuthentication();
}

app.MapGet("/healthz", (GatewayHandshakeTracker tracker) => Results.Json(new
{
    status = "ok",
    profile = SecurityProfileDefaults.CurrentProfile.ToString(),
    requiresHttps,
    requiresMtls,
    requiresJwt,
    handshakes = tracker.TotalHandshakes,
    clientCertificates = tracker.RecentSubjects
}));

app.MapReverseProxy(proxyPipeline =>
{
    if (requiresJwt)
    {
        proxyPipeline.Use(async (context, next) =>
        {
            var authenticateResult = await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
            if (!authenticateResult.Succeeded || authenticateResult.Principal is null)
            {
                await context.ChallengeAsync(JwtBearerDefaults.AuthenticationScheme);
                return;
            }

            context.User = authenticateResult.Principal;
            await next();
        });
    }
});

await app.RunAsync();

void ConfigureHttpsOptions(HttpsConnectionAdapterOptions httpsOptions)
{
    httpsOptions.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
    httpsOptions.ClientCertificateMode = requiresMtls ? ClientCertificateMode.RequireCertificate : ClientCertificateMode.AllowCertificate;
    if (requiresMtls)
    {
        httpsOptions.ClientCertificateValidation = (certificate, chain, errors) =>
            ValidateClientCertificate(certificate as X509Certificate2);
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
            RevocationMode = X509RevocationMode.NoCheck,
            VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority,
            TrustMode = X509ChainTrustMode.CustomRootTrust
        }
    };

    chain.ChainPolicy.CustomTrustStore.Add(caCertificate);
    return chain.Build(certificate);
}

static async Task<(OpenIdConnectConfiguration? Configuration, Exception? Error)> TryPreloadConfigurationAsync(
    HttpMessageHandler handler,
    Uri jwksUri,
    string issuer,
    CancellationToken cancellationToken = default)
{
    try
    {
        using var client = new HttpClient(handler, disposeHandler: false)
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        var jwksPayload = await client.GetStringAsync(jwksUri, cancellationToken);
        if (string.IsNullOrWhiteSpace(jwksPayload))
        {
            return (null, null);
        }

        var jsonWebKeySet = new JsonWebKeySet(jwksPayload);
        var configuration = new OpenIdConnectConfiguration
        {
            Issuer = issuer,
            JsonWebKeySet = jsonWebKeySet
        };
        foreach (var key in jsonWebKeySet.GetSigningKeys())
        {
            configuration.SigningKeys.Add(key);
        }

        return (configuration, null);
    }
    catch (Exception ex)
    {
        return (null, ex);
    }
}

static IEnumerable<Action<ILogger>> BuildJwtLogActions(string authority, Exception? preloadException)
{
    if (preloadException is not null)
    {
        yield return logger => logger.LogWarning(preloadException, "Failed to preload JWKS from {Authority}/.well-known/jwks", authority);
    }

    yield return logger => logger.LogInformation("Gateway JWT backchannel enabled. Authority={Authority}", authority);
}

sealed class MtlsProxyHttpClientFactory : IForwarderHttpClientFactory
{
    private readonly X509Certificate2? _clientCertificate;
    private readonly X509Certificate2? _caCertificate;
    private readonly ForwarderHttpClientFactory _innerFactory = new();

    public MtlsProxyHttpClientFactory(X509Certificate2? clientCertificate, X509Certificate2? caCertificate)
    {
        _clientCertificate = clientCertificate;
        _caCertificate = caCertificate;
    }

    public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context)
    {
        if (_clientCertificate is null && _caCertificate is null)
        {
            return _innerFactory.CreateClient(context);
        }

        var handler = HttpHandlerFactory.CreateBackchannelHandler(_caCertificate, _clientCertificate);
        handler.AllowAutoRedirect = false;
        handler.AutomaticDecompression = DecompressionMethods.None;
        handler.UseCookies = false;

        if (context.NewConfig?.DangerousAcceptAnyServerCertificate == true)
        {
            handler.SslOptions.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
        }

        handler.ActivityHeadersPropagator = new ReverseProxyPropagator(DistributedContextPropagator.Current);

        return new HttpMessageInvoker(handler, disposeHandler: true);
    }
}
