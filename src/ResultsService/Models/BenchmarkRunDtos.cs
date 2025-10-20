using System;

namespace ResultsService.Models;

public record BenchmarkRunListItem
(
    Guid Id,
    DateTimeOffset StartedAt,
    string Protocol,
    string SecurityProfile,
    string Workload,
    int Rps,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double Throughput,
    double ErrorRatePct
);

public record BenchmarkRunDetails
(
    Guid Id,
    DateTimeOffset StartedAt,
    string Protocol,
    string SecurityProfile,
    string Workload,
    int Rps,
    int Connections,
    int DurationSeconds,
    int WarmupSeconds,
    double P50Ms,
    double P75Ms,
    double P90Ms,
    double P95Ms,
    double P99Ms,
    double AvgMs,
    double MinMs,
    double MaxMs,
    double Throughput,
    double ErrorRatePct,
    string Tool,
    string SummaryPath
);
