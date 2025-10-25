using System;
using System.Net.Http;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using OrderService.Models;
using OrderService.Services;
using Prometheus;
using Shared.Security;

var builder = WebApplication.CreateBuilder(args);

static string? GetConfigurationValue(IConfiguration configuration, string key)
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
var requiresPerMethodPolicies = SecurityProfileDefaults.RequiresPerMethodPolicies();

var serverCertificatePath = GetConfigurationValue(builder.Configuration, "BENCH_Security__Tls__ServerCertificatePath")
    ?? "/certs/servers/orderservice/orderservice.pfx";
var serverCertificatePassword = GetConfigurationValue(builder.Configuration, "BENCH_Security__Tls__ServerCertificatePassword");
var caCertificatePath = GetConfigurationValue(builder.Configuration, "BENCH_Security__Tls__CaCertificatePath") ?? "/certs/ca/ca.crt.pem";

var serverCertificate = requiresHttps
    ? CertificateUtilities.TryLoadPfxCertificate(serverCertificatePath, serverCertificatePassword, optional: false)
    : CertificateUtilities.TryLoadPfxCertificate(serverCertificatePath, serverCertificatePassword, optional: true);
var caCertificate = CertificateUtilities.TryLoadPemCertificate(
    caCertificatePath,
    optional: !(requiresHttps || requiresJwt || requiresMtls));
var clientCertificatePath = GetConfigurationValue(builder.Configuration, "BENCH_Security__Tls__ClientCertificatePath");
var clientCertificateKeyPath = GetConfigurationValue(builder.Configuration, "BENCH_Security__Tls__ClientCertificateKeyPath");
Console.WriteLine($"OrderService TLS configuration: requiresMtls={requiresMtls}, requiresJwt={requiresJwt}, clientCertPath='{clientCertificatePath ?? "<null>"}', clientKeyPath='{clientCertificateKeyPath ?? "<null>"}'");

if (requiresHttps && serverCertificate is null)
{
    throw new InvalidOperationException($"Server certificate not found at '{serverCertificatePath}'.");
}

if ((requiresHttps || requiresJwt || requiresMtls) && caCertificate is null)
{
    throw new InvalidOperationException($"Certificate authority file '{caCertificatePath}' could not be loaded.");
}


builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(8081, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http1;
        if (requiresHttps && serverCertificate is not null)
        {
            listenOptions.UseHttps(serverCertificate, ConfigureHttpsOptions);
        }
    });

    options.ListenAnyIP(9091, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http2;
        if (requiresHttps && serverCertificate is not null)
        {
            listenOptions.UseHttps(serverCertificate, ConfigureHttpsOptions);
        }
    });

    options.Limits.Http2.MaxStreamsPerConnection = 1024;
    options.Limits.Http2.InitialConnectionWindowSize = 1_048_576;
    options.Limits.Http2.InitialStreamWindowSize = 1_048_576; // 1 MB
});

builder.Services.AddGrpc(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

builder.Services.AddAuthorization(options =>
{
    if (requiresPerMethodPolicies)
    {
        options.AddPolicy("Orders.Read", policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireAssertion(context => context.User.HasScope("orders.read"));
        });

        options.AddPolicy("Orders.Write", policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireAssertion(context => context.User.HasScope("orders.write"));
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
    var authority = GetConfigurationValue(builder.Configuration, "BENCH_Security__Jwt__Authority") ?? "https://authserver:5001";
    var authorityTrimmed = authority.TrimEnd('/');
    var metadataAddress = GetConfigurationValue(builder.Configuration, "BENCH_Security__Jwt__MetadataAddress")
        ?? $"{authorityTrimmed}/.well-known/openid-configuration";
    X509Certificate2? clientCertificate = null;
    if (requiresMtls)
    {
        clientCertificate = CertificateUtilities.TryLoadPemCertificate(
            clientCertificatePath,
            clientCertificateKeyPath,
            optional: true) ?? serverCertificate;
    }
    Console.WriteLine($"OrderService JWT configuration: authority='{authorityTrimmed}', clientCertPath='{clientCertificatePath ?? "<null>"}', clientKeyPath='{clientCertificateKeyPath ?? "<null>"}'");

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
            options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = context =>
                {
                    var logger = context.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("OrderService.JwtBearer");
                    logger.LogError(context.Exception, "JWT authentication failed for {Path}", context.Request.Path);
                    return Task.CompletedTask;
                }
            };
        });
}

builder.Services.Configure<InventoryClientOptions>(builder.Configuration.GetSection("Inventory"));

builder.Services.AddHttpClient<IInventoryClient, InventoryHttpClient>(client =>
{
    var baseUrl = GetConfigurationValue(builder.Configuration, "Inventory__BaseUrl") ?? "http://inventoryservice:8082";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
})
.ConfigurePrimaryHttpMessageHandler(() =>
{
    if (caCertificate is null)
    {
        return new SocketsHttpHandler();
    }

    return HttpHandlerFactory.CreateBackchannelHandler(caCertificate);
});

builder.Services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();
builder.Services.AddSingleton<OrderApplicationService>();

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
            app.Logger.LogInformation("OrderService accepted client certificate. Subject={Subject}", certificate.Subject);
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
app.MapGet("/healthz", () => Results.Json(new
{
    status = "ok",
    profile = SecurityProfileDefaults.CurrentProfile.ToString(),
    requiresHttps,
    requiresMtls,
    requiresJwt
}));

var ordersGroup = app.MapGroup("/api/orders");

if (requiresJwt && !requiresPerMethodPolicies)
{
    ordersGroup.RequireAuthorization();
}

var createOrderEndpoint = ordersGroup.MapPost("", async (OrderCreateRequest request, OrderApplicationService service, CancellationToken cancellationToken) =>
{
    try
    {
        var order = await service.CreateOrderAsync(request, cancellationToken);
        var response = new OrderAcceptedResponse(order.OrderId, true);
        return Results.Created($"/api/orders/{order.OrderId}", response);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
})
.Produces<OrderAcceptedResponse>(StatusCodes.Status201Created)
.Produces(StatusCodes.Status400BadRequest)
.WithName("CreateOrder");

if (requiresJwt && requiresPerMethodPolicies)
{
    createOrderEndpoint.RequireAuthorization("Orders.Write");
}

var getOrderEndpoint = ordersGroup.MapGet("{orderId}", async (string orderId, OrderApplicationService service, CancellationToken cancellationToken) =>
{
    var order = await service.GetOrderAsync(orderId, cancellationToken);
    if (order is null)
    {
        return Results.NotFound();
    }

    var dto = new OrderDto(order.OrderId, order.CustomerId, order.ItemSkus, order.TotalAmount, order.CreatedAt);
    return Results.Ok(dto);
})
.Produces<OrderDto>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.WithName("GetOrder");

if (requiresJwt && requiresPerMethodPolicies)
{
    getOrderEndpoint.RequireAuthorization("Orders.Read");
}

app.MapGrpcService<OrderGrpcService>();

app.Logger.LogInformation("OrderService started with profile {Profile}", SecurityProfileDefaults.CurrentProfile);

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
