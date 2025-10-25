using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Prometheus;
using ResultsService.Data;
using ResultsService.Models;
using ResultsService.Options;
using ResultsService.Services;
using Shared.Security;

var builder = WebApplication.CreateBuilder(args);

const string benchEnvironmentPrefix = "BENCH_";
var benchEnvironmentOverrides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
{
    if (entry.Key is not string key ||
        !key.StartsWith(benchEnvironmentPrefix, StringComparison.OrdinalIgnoreCase))
    {
        continue;
    }

    var suffix = key[benchEnvironmentPrefix.Length..];
    if (string.IsNullOrWhiteSpace(suffix))
    {
        continue;
    }

    var normalizedKey = $"Bench:{suffix.Replace("__", ":", StringComparison.Ordinal)}";
    benchEnvironmentOverrides[normalizedKey] = entry.Value as string;
}

if (benchEnvironmentOverrides.Count > 0)
{
    builder.Configuration.AddInMemoryCollection(benchEnvironmentOverrides);
}

var requiresHttps = SecurityProfileDefaults.RequiresHttps();
var requiresMtls = SecurityProfileDefaults.RequiresMtls();
var requiresJwt = SecurityProfileDefaults.RequiresJwt(); // applies to outbound token acquisition only

var serverCertificatePath = builder.Configuration["BENCH_Security__Tls__ServerCertificatePath"]
    ?? "/certs/servers/resultsservice/resultsservice.pfx";
var serverCertificatePassword = builder.Configuration["BENCH_Security__Tls__ServerCertificatePassword"];
var caCertificatePath = builder.Configuration["BENCH_Security__Tls__CaCertificatePath"] ?? "/certs/ca/ca.crt.pem";

var serverCertificate = requiresHttps
    ? CertificateUtilities.TryLoadPfxCertificate(serverCertificatePath, serverCertificatePassword, optional: false)
    : CertificateUtilities.TryLoadPfxCertificate(serverCertificatePath, serverCertificatePassword, optional: true);
var caCertificate = CertificateUtilities.TryLoadPemCertificate(
    caCertificatePath,
    optional: !(requiresHttps || requiresMtls || requiresJwt));
var uiScheme = builder.Configuration["RESULTS_UI_SCHEME"]
    ?? builder.Configuration["Results__UiScheme"]
    ?? "http";
var uiPort = builder.Configuration.GetValue("Results__UiPort", 8000);
var httpsPort = builder.Configuration.GetValue("Results__HttpsPort", 8443);

if (requiresHttps && serverCertificate is null)
{
    throw new InvalidOperationException($"Server certificate not found at '{serverCertificatePath}'.");
}

if ((requiresHttps || requiresMtls || requiresJwt) && caCertificate is null)
{
    throw new InvalidOperationException($"CA certificate '{caCertificatePath}' could not be loaded.");
}

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(uiPort, listenOptions =>
    {
        if (string.Equals(uiScheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            if (serverCertificate is null)
            {
                throw new InvalidOperationException("RESULTS_UI_SCHEME=https requires a server certificate.");
            }

            ConfigureHttps(listenOptions, serverCertificate);
        }
        else
        {
            listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
        }
    });

    if (requiresHttps && serverCertificate is not null && !string.Equals(uiScheme, "https", StringComparison.OrdinalIgnoreCase))
    {
        options.ListenAnyIP(httpsPort, listenOptions => ConfigureHttps(listenOptions, serverCertificate));
    }
});

var connectionString = builder.Configuration.GetConnectionString("Results")
    ?? builder.Configuration["RESULTS_DB_CONNECTION"]
    ?? "Host=postgres;Port=5432;Database=results_db;Username=postgres;Password=postgres";

builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();

builder.Services.Configure<BenchRunnerOptions>(builder.Configuration.GetSection(BenchRunnerOptions.SectionName));
builder.Services.Configure<ResultsOptions>(builder.Configuration.GetSection(ResultsOptions.SectionName));

const string DevCors = "DevCors";
builder.Services.AddDbContext<ResultsDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());

    options.AddPolicy(DevCors, policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:4173",
                "http://localhost:5173",
                "https://localhost:4173"
            )
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});


builder.Services.AddTransient<ProcessRunner>();
builder.Services.AddScoped<RestBenchmarkToolRunner>();
builder.Services.AddScoped<GrpcBenchmarkToolRunner>();
builder.Services.AddScoped<JwtTokenProvider>();
builder.Services.AddScoped<BenchmarkOrchestrator>();

var app = builder.Build();

await EnsureDatabaseCreatedAsync(app.Services);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseRouting();
app.UseCors(DevCors);
app.UseHttpMetrics();

if (requiresMtls)
{
    app.Use(async (context, next) =>
    {
        var certificate = await context.Connection.GetClientCertificateAsync();
        if (certificate is not null)
        {
            app.Logger.LogInformation("ResultsService accepted client certificate. Subject={Subject}", certificate.Subject);
        }

        await next();
    });
}

app.MapMetrics();

var api = app.MapGroup("/api");
api.MapGet("/healthz", GetHealthStatus)
    .WithName("ApiHealthStatus")
    .RequireCors(DevCors);
api.MapPost("/benchrunner/run", async (BenchRunRequest request, BenchmarkOrchestrator orchestrator, CancellationToken cancellationToken) =>
{
    try
    {
        var run = await orchestrator.ExecuteAsync(request, cancellationToken);
        return Results.Ok(new BenchRunResponse(run.Id, "Benchmark completed successfully"));
    }
    catch (BenchRunValidationException ex)
    {
        return Results.BadRequest(new { errors = ex.Errors });
    }
})
.WithName("RunBenchmarks")
.RequireCors(DevCors);

api.MapGet("/runs", async (ResultsDbContext db, IOptions<ResultsOptions> resultsOptions, [FromQuery] int? limit, CancellationToken cancellationToken) =>
{
    if (!resultsOptions.Value.AllowAnonymousReads)
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    IQueryable<BenchmarkRun> query = db.BenchmarkRuns
        .OrderByDescending(run => run.StartedAt);

    if (limit.HasValue && limit.Value > 0)
    {
        query = query.Take(Math.Min(limit.Value, 1000));
    }

    var runs = await query
        .Select(run => new BenchmarkRunListItem(
            run.Id,
            run.StartedAt,
            run.Protocol,
            run.SecurityProfile,
            run.Workload,
            run.Rps,
            run.P50Ms,
            run.P95Ms,
            run.P99Ms,
            run.Throughput,
            run.ErrorRatePct))
        .ToListAsync(cancellationToken);

    return Results.Ok(runs);
})
.WithName("ListRuns")
.RequireCors(DevCors);

api.MapGet("/runs/{id:guid}", async (Guid id, ResultsDbContext db, IOptions<ResultsOptions> resultsOptions, CancellationToken cancellationToken) =>
{
    if (!resultsOptions.Value.AllowAnonymousReads)
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    var run = await db.BenchmarkRuns.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    if (run is null)
    {
        return Results.NotFound();
    }

    var details = new BenchmarkRunDetails(
        run.Id,
        run.StartedAt,
        run.Protocol,
        run.SecurityProfile,
        run.Workload,
        run.Rps,
        run.Connections,
        run.DurationSeconds,
        run.WarmupSeconds,
        run.P50Ms,
        run.P75Ms,
        run.P90Ms,
        run.P95Ms,
        run.P99Ms,
        run.AvgMs,
        run.MinMs,
        run.MaxMs,
        run.Throughput,
        run.ErrorRatePct,
        run.Tool,
        run.SummaryPath);

    return Results.Ok(details);
})
.WithName("GetRun")
.RequireCors(DevCors);

app.MapGet("/healthz", GetHealthStatus)
    .WithName("HealthStatus")
    .RequireCors(DevCors);

app.Logger.LogInformation("ResultsService listening on profile {Profile}", SecurityProfileDefaults.CurrentProfile);

app.Run();

void ConfigureHttps(ListenOptions listenOptions, X509Certificate2 certificate)
{
    listenOptions.UseHttps(certificate, httpsOptions =>
    {
        httpsOptions.SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13;
        httpsOptions.ClientCertificateMode = requiresMtls ? Microsoft.AspNetCore.Server.Kestrel.Https.ClientCertificateMode.RequireCertificate : Microsoft.AspNetCore.Server.Kestrel.Https.ClientCertificateMode.AllowCertificate;
        if (requiresMtls)
        {
            httpsOptions.ClientCertificateValidation = (cert, _, _) => ValidateClientCertificate(cert);
        }
    });
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

static async Task EnsureDatabaseCreatedAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ResultsDbContext>();
    await db.Database.EnsureCreatedAsync();
}

async Task<IResult> GetHealthStatus(ResultsDbContext db, CancellationToken cancellationToken)
{
    var count = await db.BenchmarkRuns.CountAsync(cancellationToken);
    return Results.Json(new
    {
        status = "ok",
        profile = SecurityProfileDefaults.CurrentProfile.ToString(),
        requiresHttps,
        requiresMtls,
        requiresJwt,
        runs = count
    });
}
