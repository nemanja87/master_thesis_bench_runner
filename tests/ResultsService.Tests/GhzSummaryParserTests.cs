using Microsoft.Extensions.Logging.Abstractions;
using ResultsService.Services;
using Xunit;

namespace ResultsService.Tests;

public class GhzSummaryParserTests
{
    [Fact]
    public void Parse_NormalizesNumericNanosecondValues()
    {
        var json = """
        {
          "latency": {
            "50th": 3000000,
            "75th": 3500000,
            "90th": 4000000,
            "95th": 5000000,
            "99th": 7000000,
            "mean": 3500000,
            "min": 1000000,
            "max": 9000000
          },
          "rps": 100.0,
          "count": 200,
          "errorCount": 10
        }
        """u8.ToArray();

        var metrics = GhzSummaryParser.Parse(json, NullLogger.Instance);

        Assert.Equal(3.0, metrics.P50Ms, 2);
        Assert.Equal(5.0, metrics.P95Ms, 2);
        Assert.Equal(3.5, metrics.AvgMs, 2);
        Assert.Equal(1.0, metrics.MinMs, 2);
        Assert.Equal(9.0, metrics.MaxMs, 2);
        Assert.Equal(100.0, metrics.Throughput, 2);
        Assert.Equal(5.0, metrics.ErrorRatePct, 2);
    }

    [Fact]
    public void Parse_NormalizesStringLatencyValues()
    {
        var json = """
        {
          "latency": {
            "50th": "3000000ns",
            "75th": "3ms",
            "90th": "4ms",
            "95th": "5000000ns",
            "99th": "7ms",
            "mean": "3500000ns",
            "min": "1ms",
            "max": "9ms"
          },
          "rps": "80",
          "count": "100",
          "errorDistribution": {
            "UNAVAILABLE": 5
          }
        }
        """u8.ToArray();

        var metrics = GhzSummaryParser.Parse(json, NullLogger.Instance);

        Assert.Equal(3.0, metrics.P50Ms, 2);
        Assert.Equal(5.0, metrics.P95Ms, 2);
        Assert.Equal(3.5, metrics.AvgMs, 2);
        Assert.Equal(1.0, metrics.MinMs, 2);
        Assert.Equal(9.0, metrics.MaxMs, 2);
        Assert.Equal(80.0, metrics.Throughput, 2);
        Assert.Equal(5.0, metrics.ErrorRatePct, 2);
    }
}
