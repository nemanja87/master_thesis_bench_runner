using System;

namespace ResultsService.Data;

public class BenchmarkRun
{
    public Guid Id { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public string Protocol { get; set; } = string.Empty;
    public string SecurityProfile { get; set; } = string.Empty;
    public string Workload { get; set; } = string.Empty;
    public int Rps { get; set; }
    public int Connections { get; set; }
    public int DurationSeconds { get; set; }
    public int WarmupSeconds { get; set; }
    public double P50Ms { get; set; }
    public double P75Ms { get; set; }
    public double P90Ms { get; set; }
    public double P95Ms { get; set; }
    public double P99Ms { get; set; }
    public double AvgMs { get; set; }
    public double MinMs { get; set; }
    public double MaxMs { get; set; }
    public double Throughput { get; set; }
    public double ErrorRatePct { get; set; }
    public string Tool { get; set; } = string.Empty;
    public string SummaryPath { get; set; } = string.Empty;
}
