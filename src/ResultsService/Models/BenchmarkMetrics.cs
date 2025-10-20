namespace ResultsService.Models;

public record BenchmarkMetrics
(
    double P50Ms,
    double P75Ms,
    double P90Ms,
    double P95Ms,
    double P99Ms,
    double AvgMs,
    double MinMs,
    double MaxMs,
    double Throughput,
    double ErrorRatePct
);
