using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResultsService.Models;
using ResultsService.Options;

namespace ResultsService.Services;

public class GrpcBenchmarkToolRunner : IBenchmarkToolRunner
{
    private const string GrpcCallName = "shared.contracts.orders.OrderService/Create";

    private readonly BenchRunnerOptions _options;
    private readonly ProcessRunner _processRunner;
    private readonly ILogger<GrpcBenchmarkToolRunner> _logger;

    public GrpcBenchmarkToolRunner(
        IOptions<BenchRunnerOptions> options,
        ProcessRunner processRunner,
        ILogger<GrpcBenchmarkToolRunner> logger)
    {
        _options = options.Value;
        _processRunner = processRunner;
        _logger = logger;
    }

    public async Task<BenchmarkRunResult> RunAsync(BenchmarkExecutionContext context, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(context.WorkingDirectory);

        if (context.Request.Warmup > 0)
        {
            await ExecuteGhzAsync(context, TimeSpan.FromSeconds(context.Request.Warmup), null, cancellationToken);
        }

        var summaryPath = Path.Combine(context.WorkingDirectory, $"ghz-summary-{context.RunId:N}.json");
        await ExecuteGhzAsync(context, TimeSpan.FromSeconds(context.Request.Duration), summaryPath, cancellationToken);

        if (!File.Exists(summaryPath))
        {
            throw new FileNotFoundException("ghz summary file not found.", summaryPath);
        }

        var json = await File.ReadAllBytesAsync(summaryPath, cancellationToken);
        var metrics = GhzSummaryParser.Parse(json, _logger);

        _logger.LogInformation("ghz summary file path: {SummaryPath}", summaryPath);

        return new BenchmarkRunResult(metrics, "ghz", summaryPath);
    }

    private async Task ExecuteGhzAsync(BenchmarkExecutionContext context, TimeSpan duration, string? summaryPath, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _options.Tools.GhzPath,
            WorkingDirectory = context.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("--call");
        startInfo.ArgumentList.Add(GrpcCallName);
        startInfo.ArgumentList.Add("--proto");
        startInfo.ArgumentList.Add(_options.Tools.GhzProtoPath);
        startInfo.ArgumentList.Add("--connections");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("--concurrency");
        startInfo.ArgumentList.Add(context.Request.Connections.ToString());
        startInfo.ArgumentList.Add("--rps");
        startInfo.ArgumentList.Add(context.Request.Rps.ToString());
        startInfo.ArgumentList.Add("--duration");
        startInfo.ArgumentList.Add($"{duration.TotalSeconds}s");
        startInfo.ArgumentList.Add("--data");
        startInfo.ArgumentList.Add(BuildPayload());
        if (!context.UseTls)
        {
            startInfo.ArgumentList.Add("--insecure");
        }
        else if (!string.IsNullOrWhiteSpace(_options.Security.Tls.CaCertificatePath))
        {
            startInfo.ArgumentList.Add("--cacert");
            startInfo.ArgumentList.Add(_options.Security.Tls.CaCertificatePath);
        }

        if (context.UseMtls)
        {
            if (!string.IsNullOrWhiteSpace(_options.Security.Tls.ClientCertificatePath))
            {
                startInfo.ArgumentList.Add("--cert");
                startInfo.ArgumentList.Add(_options.Security.Tls.ClientCertificatePath);
            }

            if (!string.IsNullOrWhiteSpace(_options.Security.Tls.ClientCertificateKeyPath))
            {
                startInfo.ArgumentList.Add("--key");
                startInfo.ArgumentList.Add(_options.Security.Tls.ClientCertificateKeyPath);
            }
        }

        if (!string.IsNullOrWhiteSpace(context.JwtToken))
        {
            startInfo.ArgumentList.Add("--metadata");
            var metadataJson = $"{{\"authorization\":\"Bearer {context.JwtToken}\"}}";
            startInfo.ArgumentList.Add(metadataJson);
        }

        if (summaryPath is not null)
        {
            startInfo.ArgumentList.Add("--format");
            startInfo.ArgumentList.Add("json");
            startInfo.ArgumentList.Add("--output");
            startInfo.ArgumentList.Add(summaryPath);
        }

        startInfo.ArgumentList.Add(context.TargetUrl);

        var result = await _processRunner.RunAsync(startInfo, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"ghz exited with code {result.ExitCode}: {result.StandardError}");
        }
    }

    private static string BuildPayload()
    {
        return "{\"customerId\":\"bench-client\",\"itemSkus\":[\"SKU-1000\",\"SKU-2000\"],\"totalAmount\":199.99}";
    }
}
