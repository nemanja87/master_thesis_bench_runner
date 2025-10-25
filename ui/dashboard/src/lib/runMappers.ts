import type { BenchmarkRunDetails, BenchmarkRunListItem, RunMetrics, RunSummary } from "../types";

export function toRunSummary(run: BenchmarkRunListItem | BenchmarkRunDetails): RunSummary {
  return {
    id: run.id,
    startedAt: run.startedAt,
    endedAt: run.endedAt,
    protocol: run.protocol?.toUpperCase?.() ?? run.protocol,
    securityMode: run.securityProfile,
    callPath: run.callPath,
    workload: run.workload,
    rpsRequested: run.rps,
    durationSec: run.durationSeconds,
    warmupSec: run.warmupSeconds,
    connections: run.connections,
    status: run.status,
  };
}

export function toRunMetrics(run: BenchmarkRunListItem | BenchmarkRunDetails): RunMetrics {
  return {
    latency: {
      p50: run.p50Ms,
      p75: run.p75Ms,
      p90: run.p90Ms,
      p95: run.p95Ms,
      p99: run.p99Ms,
      avg: "avgMs" in run ? run.avgMs : undefined,
    },
    throughput: {
      achievedRps: run.throughput,
    },
    errors: {
      errorRatePct: run.errorRatePct,
    },
  };
}
