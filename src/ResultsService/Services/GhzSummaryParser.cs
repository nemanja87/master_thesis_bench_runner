using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ResultsService.Models;

namespace ResultsService.Services;

public static class GhzSummaryParser
{
    public static BenchmarkMetrics Parse(byte[] json, ILogger logger)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var hasLatency = root.TryGetProperty("latency", out var latencyElement) && latencyElement.ValueKind == JsonValueKind.Object;
        var latencyDistribution = TryReadLatencyDistribution(root);

        double p50;
        string rawP50;
        double p75;
        double p90;
        double p95;
        double p99;
        double avg;
        double min;
        double max;

        if (hasLatency)
        {
            (p50, rawP50) = GetLatency(latencyElement, "50th");
            (p75, _) = GetLatency(latencyElement, "75th");
            (p90, _) = GetLatency(latencyElement, "90th");
            (p95, _) = GetLatency(latencyElement, "95th");
            (p99, _) = GetLatency(latencyElement, "99th");
            (avg, _) = TryGetLatency(latencyElement, "mean");
            (min, _) = TryGetLatency(latencyElement, "min");
            if (double.IsNaN(min))
            {
                (min, _) = TryGetLatency(latencyElement, "fastest");
            }

            (max, _) = TryGetLatency(latencyElement, "max");
            if (double.IsNaN(max))
            {
                (max, _) = TryGetLatency(latencyElement, "slowest");
            }
        }
        else if (latencyDistribution.Count > 0)
        {
            logger.LogInformation("ghz summary missing 'latency' section; using latencyDistribution fallback (records={Count}).", latencyDistribution.Count);
            rawP50 = TryGetLatencyFromDistribution(latencyDistribution, 50, out p50);
            TryGetLatencyFromDistribution(latencyDistribution, 75, out p75);
            TryGetLatencyFromDistribution(latencyDistribution, 90, out p90);
            TryGetLatencyFromDistribution(latencyDistribution, 95, out p95);
            TryGetLatencyFromDistribution(latencyDistribution, 99, out p99);

            avg = ConvertNanosecondsToMilliseconds(TryReadDouble(root, "average"));
            min = ConvertNanosecondsToMilliseconds(TryReadDouble(root, "fastest"));
            max = ConvertNanosecondsToMilliseconds(TryReadDouble(root, "slowest"));
        }
        else
        {
            logger.LogWarning("ghz summary missing latency section; defaulting latency metrics to 0.");
            rawP50 = string.Empty;
            p50 = p75 = p90 = p95 = p99 = avg = min = max = 0;
        }

        var throughput = TryReadDouble(root, "rps");
        var totalRequests = TryReadDouble(root, "count");
        var errorCount = TryReadDouble(root, "errorCount");

        if (double.IsNaN(errorCount))
        {
            errorCount = SumDistribution(root, "errorDistribution");
        }

        if (double.IsNaN(errorCount))
        {
            errorCount = 0;
        }

        if (double.IsNaN(throughput))
        {
            throughput = 0;
        }

        if (double.IsNaN(avg))
        {
            avg = p50;
        }

        if (double.IsNaN(min))
        {
            min = p50;
        }

        if (double.IsNaN(max))
        {
            max = p99;
        }

        var errorRatePct = totalRequests > 0 ? errorCount / totalRequests * 100 : 0;

        if (hasLatency)
        {
            logger.LogInformation(
                "Parsed gRPC latencies from ghz and normalized to ms: p50={P50:F2} p95={P95:F2} avg={Avg:F2} (raw_p50_ns={Raw})",
                p50,
                p95,
                avg,
                rawP50);
        }
        else if (latencyDistribution.Count > 0)
        {
            logger.LogInformation(
                "Parsed gRPC latencies from ghz latencyDistribution fallback: p50={P50:F2} p95={P95:F2} avg={Avg:F2}.",
                p50,
                p95,
                avg);
        }
        else
        {
            logger.LogInformation("ghz run produced no latency metrics; treating latency values as 0.");
        }

        return new BenchmarkMetrics(
            p50,
            p75,
            p90,
            p95,
            p99,
            avg,
            min,
            max,
            throughput,
            errorRatePct);
    }

    private static string TryGetLatencyFromDistribution(Dictionary<double, double> distribution, double targetPercentile, out double valueMs)
    {
        if (distribution.TryGetValue(targetPercentile, out valueMs))
        {
            return $"{valueMs}ms";
        }

        valueMs = 0;
        return string.Empty;
    }

    private static (double ValueMs, string RawValue) GetLatency(JsonElement latencyElement, string propertyName)
    {
        if (!latencyElement.TryGetProperty(propertyName, out var valueElement))
        {
            throw new InvalidOperationException($"ghz latency missing '{propertyName}'.");
        }

        return (NormalizeToMilliseconds(valueElement, out var raw), raw);
    }

    private static (double ValueMs, string RawValue) TryGetLatency(JsonElement latencyElement, string propertyName)
    {
        if (!latencyElement.TryGetProperty(propertyName, out var valueElement))
        {
            return (double.NaN, string.Empty);
        }

        return (NormalizeToMilliseconds(valueElement, out var raw), raw);
    }

    private static double NormalizeToMilliseconds(JsonElement element, out string rawValue)
    {
        rawValue = element.GetRawText();

        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                if (element.TryGetInt64(out var intValue))
                {
                    return intValue / 1_000_000d;
                }

                return element.GetDouble() / 1_000_000d;

            case JsonValueKind.String:
                var text = element.GetString()!.Trim();
                rawValue = text;
                return ParseLatencyString(text);

            default:
                throw new InvalidOperationException("Unexpected ghz latency value type.");
        }
    }

    private static double ParseLatencyString(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return double.NaN;
        }

        var trimmed = value.Trim();

        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric))
        {
            return numeric / 1_000_000d;
        }

        static double Parse(string input, string suffix, double factor)
        {
            if (!input.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return double.NaN;
            }

            var numericPart = input[..^suffix.Length];
            return double.TryParse(numericPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed * factor
                : double.NaN;
        }

        var ns = Parse(trimmed, "ns", 1d / 1_000_000d);
        if (!double.IsNaN(ns))
        {
            return ns;
        }

        var us = Parse(trimmed, "us", 1d / 1_000d);
        if (!double.IsNaN(us))
        {
            return us;
        }

        var mus = Parse(trimmed, "µs", 1d / 1_000d);
        if (!double.IsNaN(mus))
        {
            return mus;
        }

        var ms = Parse(trimmed, "ms", 1d);
        if (!double.IsNaN(ms))
        {
            return ms;
        }

        var s = Parse(trimmed, "s", 1_000d);
        if (!double.IsNaN(s))
        {
            return s;
        }

        throw new FormatException($"Unable to parse ghz latency value '{value}'.");
    }

    private static Dictionary<double, double> TryReadLatencyDistribution(JsonElement root)
    {
        var distribution = new Dictionary<double, double>();

        JsonElement distributionElement = default;
        var hasDistribution = root.TryGetProperty("latencyDistribution", out distributionElement);

        if (!hasDistribution)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (string.Equals(property.Name, "latencyDistribution", StringComparison.OrdinalIgnoreCase))
                {
                    distributionElement = property.Value;
                    hasDistribution = true;
                    break;
                }
            }
        }

        if (hasDistribution && distributionElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in distributionElement.EnumerateArray())
            {
                if (!entry.TryGetProperty("percentage", out var percentageElement) ||
                    !entry.TryGetProperty("latency", out var latencyElement) ||
                    percentageElement.ValueKind is not JsonValueKind.Number ||
                    latencyElement.ValueKind is not JsonValueKind.Number)
                {
                    continue;
                }

                var percentage = percentageElement.GetDouble();
                var latencyNs = latencyElement.GetDouble();
                var latencyMs = ConvertNanosecondsToMilliseconds(latencyNs);
                distribution[percentage] = latencyMs;
            }
        }

        return distribution;
    }

    private static double TryReadDouble(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var element))
        {
            return double.NaN;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.String => double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : double.NaN,
            _ => double.NaN
        };
    }

    private static double ConvertNanosecondsToMilliseconds(double value)
    {
        if (double.IsNaN(value))
        {
            return double.NaN;
        }

        return value / 1_000_000d;
    }

    private static double SumDistribution(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return double.NaN;
        }

        double total = 0;
        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Number)
            {
                total += property.Value.GetDouble();
            }
            else if (property.Value.ValueKind == JsonValueKind.String && double.TryParse(property.Value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                total += parsed;
            }
        }

        return total;
    }
}
