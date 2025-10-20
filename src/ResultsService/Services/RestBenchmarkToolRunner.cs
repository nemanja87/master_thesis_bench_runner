using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResultsService.Models;
using ResultsService.Options;

namespace ResultsService.Services;

public class RestBenchmarkToolRunner : IBenchmarkToolRunner
{
    private readonly BenchRunnerOptions _options;
    private readonly ProcessRunner _processRunner;
    private readonly ILogger<RestBenchmarkToolRunner> _logger;

    public RestBenchmarkToolRunner(
        IOptions<BenchRunnerOptions> options,
        ProcessRunner processRunner,
        ILogger<RestBenchmarkToolRunner> logger)
    {
        _options = options.Value;
        _processRunner = processRunner;
        _logger = logger;
    }

    public async Task<BenchmarkRunResult> RunAsync(BenchmarkExecutionContext context, CancellationToken cancellationToken)
    {
        var scriptPath = Path.Combine(context.WorkingDirectory, $"k6-script-{context.RunId:N}.js");
        var summaryPath = Path.Combine(context.WorkingDirectory, $"k6-summary-{context.RunId:N}.json");
        var hasClientCertificate = context.UseMtls &&
            !string.IsNullOrWhiteSpace(_options.Security.Tls.ClientCertificatePath) &&
            !string.IsNullOrWhiteSpace(_options.Security.Tls.ClientCertificateKeyPath);

        Directory.CreateDirectory(context.WorkingDirectory);

        await File.WriteAllTextAsync(scriptPath, BuildScript(context, hasClientCertificate), cancellationToken);

        _logger.LogInformation("k6 script path: {ScriptPath}", scriptPath);
        _logger.LogInformation("k6 summary file path: {SummaryPath}", summaryPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = _options.Tools.K6Path,
            WorkingDirectory = context.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--summary-export");
        startInfo.ArgumentList.Add(summaryPath);
        startInfo.ArgumentList.Add(scriptPath);

        if (context.UseTls)
        {
            if (!string.IsNullOrWhiteSpace(_options.Security.Tls.CaCertificatePath))
            {
                startInfo.Environment["SSL_CERT_FILE"] = _options.Security.Tls.CaCertificatePath;
            }
            else
            {
                startInfo.ArgumentList.Add("--insecure-skip-tls-verify");
            }
        }

        if (context.UseMtls && !hasClientCertificate)
        {
            _logger.LogWarning("REST benchmark requested mTLS but REST client certificates are not configured; continuing without presenting a client certificate.");
        }

        if (!string.IsNullOrWhiteSpace(context.JwtToken))
        {
            startInfo.Environment["BENCH_AUTH_HEADER"] = $"Bearer {context.JwtToken}";
        }

        startInfo.Environment["BENCH_BASE_URL"] = context.TargetUrl;

        var result = await _processRunner.RunAsync(startInfo, cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"k6 exited with code {result.ExitCode}: {result.StandardError}");
        }

        if (!File.Exists(summaryPath))
        {
            throw new FileNotFoundException("k6 summary file not found.", summaryPath);
        }

        var json = await File.ReadAllBytesAsync(summaryPath, cancellationToken);
        var summary = K6SummaryParser.Parse(json, _logger);

        return new BenchmarkRunResult(summary.Metrics, "k6", summaryPath);
    }

    private string BuildScript(BenchmarkExecutionContext context, bool includeClientCertificate)
    {
        var sb = new StringBuilder();
        sb.AppendLine("import http from 'k6/http';");
        sb.AppendLine("import { check } from 'k6';");
        sb.AppendLine();

        if (includeClientCertificate)
        {
            sb.AppendLine($"const clientCert = open('{EscapeForJavaScriptString(_options.Security.Tls.ClientCertificatePath)}');");
            sb.AppendLine($"const clientKey = open('{EscapeForJavaScriptString(_options.Security.Tls.ClientCertificateKeyPath)}');");
            sb.AppendLine();
        }

        var scenarioBuilder = new StringBuilder();
        scenarioBuilder.AppendLine("export const options = {");
        scenarioBuilder.AppendLine("  discardResponseBodies: true,");
        if (includeClientCertificate)
        {
            var domains = string.Join(", ", GetMtlsDomains(context));
            scenarioBuilder.AppendLine("  tlsAuth: [{");
            scenarioBuilder.AppendLine("    domains: [" + domains + "],");
            scenarioBuilder.AppendLine("    cert: clientCert,");
            scenarioBuilder.AppendLine("    key: clientKey");
            scenarioBuilder.AppendLine("  }],");
        }
        scenarioBuilder.AppendLine("  scenarios: {");

        var vus = Math.Max(context.Request.Connections, Math.Max(1, context.Request.Rps));

        if (context.Request.Warmup > 0)
        {
            scenarioBuilder.AppendLine("    warmup: {");
            scenarioBuilder.AppendLine("      executor: 'constant-arrival-rate',");
            scenarioBuilder.AppendLine($"      duration: '{context.Request.Warmup}s',");
            scenarioBuilder.AppendLine($"      rate: {context.Request.Rps},");
            scenarioBuilder.AppendLine("      timeUnit: '1s',");
            scenarioBuilder.AppendLine($"      preAllocatedVUs: {vus},");
            scenarioBuilder.AppendLine($"      maxVUs: {vus},");
            scenarioBuilder.AppendLine("      gracefulStop: '0s'\n    },");
        }

        scenarioBuilder.AppendLine("    benchmark: {");
        scenarioBuilder.AppendLine("      executor: 'constant-arrival-rate',");
        scenarioBuilder.AppendLine($"      duration: '{context.Request.Duration}s',");
        if (context.Request.Warmup > 0)
        {
            scenarioBuilder.AppendLine($"      startTime: '{context.Request.Warmup}s',");
        }
        scenarioBuilder.AppendLine($"      rate: {context.Request.Rps},");
        scenarioBuilder.AppendLine("      timeUnit: '1s',");
        scenarioBuilder.AppendLine($"      preAllocatedVUs: {vus},");
        scenarioBuilder.AppendLine($"      maxVUs: {vus},");
        scenarioBuilder.AppendLine("      gracefulStop: '0s'\n    }");
        scenarioBuilder.AppendLine("  }");
        scenarioBuilder.AppendLine("};");

        sb.AppendLine(scenarioBuilder.ToString());

        sb.AppendLine();
        sb.AppendLine("export default function () {");
        sb.AppendLine("  const headers = { 'Content-Type': 'application/json' };");
        sb.AppendLine("  const authHeader = __ENV.BENCH_AUTH_HEADER;");
        sb.AppendLine("  if (authHeader) { headers['Authorization'] = authHeader; }");
        sb.AppendLine("  const payload = JSON.stringify({");
        sb.AppendLine("    customerId: 'bench-client',");
        sb.AppendLine("    itemSkus: ['SKU-1000', 'SKU-2000'],");
        sb.AppendLine("    totalAmount: 199.99");
        sb.AppendLine("  });");
        sb.AppendLine("  const res = http.post(`${__ENV.BENCH_BASE_URL}/orders/api/orders`, payload, { headers });");
        sb.AppendLine("  check(res, { 'status 201': r => r.status === 201 });");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string EscapeForJavaScriptString(string value) =>
        value.Replace("\\", "\\\\").Replace("'", "\\'");

    private static IEnumerable<string> GetMtlsDomains(BenchmarkExecutionContext context)
    {
        if (Uri.TryCreate(context.TargetUrl, UriKind.Absolute, out var uri))
        {
            yield return $"'{uri.Host}'";
            if (!uri.IsDefaultPort)
            {
                yield return $"'{uri.Host}:{uri.Port}'";
            }
        }
        else
        {
            yield return "'gateway'";
            yield return "'gateway:9090'";
            yield return "'gateway:8080'";
        }
    }
}
