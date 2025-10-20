namespace ResultsService.Models;

public record BenchmarkRunResult
(
    BenchmarkMetrics Metrics,
    string Tool,
    string SummaryPath
);
