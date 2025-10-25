using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResultsService.Data;
using ResultsService.Models;
using ResultsService.Options;
using Shared.Security;

namespace ResultsService.Services;

public class BenchmarkOrchestrator
{
    private static readonly HashSet<string> SupportedProtocols = new(StringComparer.OrdinalIgnoreCase)
    {
        "rest",
        "grpc"
    };
    private static readonly HashSet<string> SupportedCallPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "gateway",
        "direct"
    };

    private static readonly HashSet<string> SupportedWorkloads = new(StringComparer.OrdinalIgnoreCase)
    {
        "orders-create"
    };

    private readonly ResultsDbContext _dbContext;
    private readonly JwtTokenProvider _tokenProvider;
    private readonly RestBenchmarkToolRunner _restRunner;
    private readonly GrpcBenchmarkToolRunner _grpcRunner;
    private readonly BenchRunnerOptions _options;
    private readonly ILogger<BenchmarkOrchestrator> _logger;

    public BenchmarkOrchestrator(
        ResultsDbContext dbContext,
        JwtTokenProvider tokenProvider,
        RestBenchmarkToolRunner restRunner,
        GrpcBenchmarkToolRunner grpcRunner,
        IOptions<BenchRunnerOptions> options,
        ILogger<BenchmarkOrchestrator> logger)
    {
        _dbContext = dbContext;
        _tokenProvider = tokenProvider;
        _restRunner = restRunner;
        _grpcRunner = grpcRunner;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<BenchmarkRun> ExecuteAsync(BenchRunRequest request, CancellationToken cancellationToken)
    {
        var errors = ValidateRequest(request);
        if (errors.Count > 0)
        {
            throw new BenchRunValidationException(errors);
        }

        var protocol = request.Protocol.Trim().ToLowerInvariant();
        var securityProfileName = request.Security.Trim().ToUpperInvariant();
        var callPath = string.IsNullOrWhiteSpace(request.CallPath)
            ? "gateway"
            : request.CallPath.Trim().ToLowerInvariant();
        var securityProfile = SecurityProfileDefaults.Parse(securityProfileName);
        var runId = Guid.NewGuid();
        var workingDirectory = Path.Combine(Path.GetTempPath(), "bench-runner", runId.ToString("N"));

        var requiresJwt = SecurityProfileDefaults.RequiresJwt(securityProfile);
        var useMtls = SecurityProfileDefaults.RequiresMtls(securityProfile);
        var useTls = SecurityProfileDefaults.RequiresHttps(securityProfile) || useMtls;

        var token = await _tokenProvider.TryAcquireTokenAsync(requiresJwt, cancellationToken);

        var targetUrl = ResolveTargetUrl(protocol, securityProfileName, callPath);

        var context = new BenchmarkExecutionContext(
            runId,
            request,
            protocol,
            securityProfileName,
            callPath,
            targetUrl,
            token,
            useMtls,
            useTls,
            workingDirectory);

        _logger.LogInformation(
            "Executing benchmark run {RunId} protocol={Protocol} security={Security} callPath={CallPath} workload={Workload} rps={Rps} connections={Connections} duration={Duration}s warmup={Warmup}s",
            runId,
            protocol,
            securityProfileName,
            callPath,
            request.Workload,
            request.Rps,
            request.Connections,
            request.Duration,
            request.Warmup);

        BenchmarkRunResult result = protocol switch
        {
            "rest" => await _restRunner.RunAsync(context, cancellationToken),
            "grpc" => await _grpcRunner.RunAsync(context, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported protocol '{protocol}'.")
        };

        var entity = new BenchmarkRun
        {
            Id = runId,
            StartedAt = DateTimeOffset.UtcNow,
            Protocol = protocol,
            SecurityProfile = securityProfileName,
            CallPath = callPath,
            Workload = request.Workload,
            Rps = request.Rps,
            Connections = request.Connections,
            DurationSeconds = request.Duration,
            WarmupSeconds = request.Warmup,
            P50Ms = result.Metrics.P50Ms,
            P75Ms = result.Metrics.P75Ms,
            P90Ms = result.Metrics.P90Ms,
            P95Ms = result.Metrics.P95Ms,
            P99Ms = result.Metrics.P99Ms,
            AvgMs = result.Metrics.AvgMs,
            MinMs = result.Metrics.MinMs,
            MaxMs = result.Metrics.MaxMs,
            Throughput = result.Metrics.Throughput,
            ErrorRatePct = result.Metrics.ErrorRatePct,
            Tool = result.Tool,
            SummaryPath = result.SummaryPath
        };

        _dbContext.BenchmarkRuns.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return entity;
    }

    private static List<string> ValidateRequest(BenchRunRequest request)
    {
        var errors = new List<string>();

        if (!SupportedProtocols.Contains(request.Protocol ?? string.Empty))
        {
            errors.Add("Protocol must be 'rest' or 'grpc'.");
        }

        if (!SupportedCallPaths.Contains(request.CallPath ?? "gateway"))
        {
            errors.Add("Call path must be 'gateway' or 'direct'.");
        }

        if (!SupportedWorkloads.Contains(request.Workload ?? string.Empty))
        {
            errors.Add("Unsupported workload specified.");
        }

        if (!SecurityProfileDefaults.TryParse(request.Security, out _))
        {
            errors.Add("Security profile must be one of S0, S1, S2, S3, S4.");
        }

        if (request.Rps <= 0)
        {
            errors.Add("Rps must be greater than zero.");
        }

        if (request.Connections <= 0)
        {
            errors.Add("Connections must be greater than zero.");
        }

        if (request.Duration <= 0)
        {
            errors.Add("Duration must be greater than zero.");
        }

        if (request.Warmup < 0)
        {
            errors.Add("Warmup cannot be negative.");
        }

        return errors;
    }

    private string ResolveTargetUrl(string protocol, string securityProfile, string callPath)
    {
        var useGateway = !string.Equals(callPath, "direct", StringComparison.OrdinalIgnoreCase);

        if (string.Equals(protocol, "rest", StringComparison.OrdinalIgnoreCase))
        {
            return securityProfile switch
            {
                "S3" or "S4" when useGateway => _options.Target.RestMtlsBaseUrl.TrimEnd('/'),
                "S3" or "S4" => _options.Target.RestDirectMtlsBaseUrl.TrimEnd('/'),
                "S1" or "S2" when useGateway => _options.Target.RestTlsBaseUrl.TrimEnd('/'),
                "S1" or "S2" => _options.Target.RestDirectTlsBaseUrl.TrimEnd('/'),
                _ when useGateway => _options.Target.RestBaseUrl.TrimEnd('/'),
                _ => _options.Target.RestDirectBaseUrl.TrimEnd('/')
            };
        }

        return useGateway ? _options.Target.GrpcAddress : _options.Target.GrpcDirectAddress;
    }
}
