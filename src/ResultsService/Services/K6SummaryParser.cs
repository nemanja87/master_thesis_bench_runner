using System.Globalization;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ResultsService.Models;

namespace ResultsService.Services;

public record K6Summary(BenchmarkMetrics Metrics, double RequestCount, double FailureCount, double CheckPasses, double CheckFails);

public static class K6SummaryParser
{
    public static K6Summary Parse(byte[] json, ILogger logger)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("metrics", out var metricsElement))
        {
            throw new InvalidOperationException("k6 summary missing metrics section.");
        }

        var durationMetrics = metricsElement.GetProperty("http_req_duration");

        var avg = GetMetric(durationMetrics, new[] { "avg" });
        var min = GetMetric(durationMetrics, new[] { "min" });
        var max = GetMetric(durationMetrics, new[] { "max" });
        var p90 = GetMetric(durationMetrics, new[] { "p(90)" }, fallbackValue: avg);
        var p95 = GetMetric(durationMetrics, new[] { "p(95)" }, fallbackValue: p90);
        var p99 = GetMetric(durationMetrics, new[] { "p(99)" }, fallbackValue: p95);
        var p50 = GetMetric(durationMetrics, new[] { "p(50)", "med" }, fallbackValue: avg);
        var p75 = GetMetric(durationMetrics, new[] { "p(75)", "p(90)", "med" }, fallbackValue: (p50 + p90) / 2);

        var throughput = TryGetMetric(metricsElement, "http_reqs", "rate", defaultValue: 0);
        var requestCount = TryGetMetric(metricsElement, "http_reqs", "count", defaultValue: 0);

        var failureRatio = TryGetMetric(metricsElement, "http_req_failed", "rate", defaultValue: 0, allowMissing: true, alternateProperties: new[] { "value" });
        var failureCount = TryGetMetric(metricsElement, "http_req_failed", "count", defaultValue: 0, allowMissing: true, alternateProperties: new[] { "fails" });

        var checksPasses = TryGetMetric(metricsElement, "checks", "passes", defaultValue: 0, allowMissing: true);
        var checksFails = TryGetMetric(metricsElement, "checks", "fails", defaultValue: 0, allowMissing: true);

        if (failureCount == 0 && requestCount > 0)
        {
            failureCount = failureRatio * requestCount;
        }

        var metrics = new BenchmarkMetrics(
            p50,
            p75,
            p90,
            p95,
            p99,
            avg,
            min,
            max,
            throughput,
            failureRatio * 100);

        logger.LogInformation(
            "k6 summary processed. latency p50={P50:F2}ms p95={P95:F2}ms avg={Avg:F2}ms http_reqs={Requests:F0} failed={Failures:F0} checks={Passes:F0}/{Fails:F0}",
            p50,
            p95,
            avg,
            requestCount,
            failureCount,
            checksPasses,
            checksFails);

        return new K6Summary(metrics, requestCount, failureCount, checksPasses, checksFails);
    }

    private static double GetMetric(JsonElement metrics, string[] propertyNames, double? fallbackValue = null)
    {
        foreach (var name in propertyNames.Where(static name => !string.IsNullOrWhiteSpace(name)))
        {
            if (metrics.TryGetProperty(name, out var element))
            {
                return ReadDouble(element);
            }
        }

        if (fallbackValue.HasValue)
        {
            return fallbackValue.Value;
        }

        throw new InvalidOperationException($"k6 summary missing metric '{propertyNames.FirstOrDefault() ?? "unknown"}'.");
    }

    private static double TryGetMetric(JsonElement metricsContainer, string metricName, string propertyName, double defaultValue = 0, bool allowMissing = false, string[]? alternateProperties = null)
    {
        if (!metricsContainer.TryGetProperty(metricName, out var metricElement))
        {
            if (allowMissing)
            {
                return defaultValue;
            }

            throw new InvalidOperationException($"k6 summary missing metric '{metricName}'.");
        }

        foreach (var name in EnumeratePropertyCandidates(propertyName, alternateProperties))
        {
            if (metricElement.TryGetProperty(name, out var valueElement))
            {
                return ReadDouble(valueElement, defaultValue);
            }
        }

        if (allowMissing)
        {
            return defaultValue;
        }

        throw new InvalidOperationException($"k6 metric '{metricName}' missing property '{propertyName}'.");
    }

    private static double ReadDouble(JsonElement element, double defaultValue = 0)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.String => double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : defaultValue,
            _ => defaultValue
        };
    }

    private static IEnumerable<string> EnumeratePropertyCandidates(string primary, string[]? alternates)
    {
        yield return primary;

        if (alternates is null)
        {
            yield break;
        }

        foreach (var name in alternates)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                yield return name;
            }
        }
    }
}
