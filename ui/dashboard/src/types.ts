export type ProtocolOption = "rest" | "grpc";

export type SecurityProfile = "S0" | "S1" | "S2" | "S3" | "S4";

export type CallPathOption = "gateway" | "direct";

export interface BenchRunRequest {
  protocol: ProtocolOption;
  callPath: CallPathOption;
  security: SecurityProfile;
  workload: string;
  rps: number;
  duration: number;
  warmup: number;
  connections: number;
}

export interface BenchRunResponse {
  id: string;
  message?: string;
}

export interface HealthStatus {
  status: string;
  profile: string;
  requiresHttps: boolean;
  requiresMtls: boolean;
  requiresJwt: boolean;
  runs: number;
}

export interface BenchmarkRunListItem {
  id: string;
  startedAt: string;
  endedAt?: string;
  protocol: string;
  securityProfile: string;
  callPath: CallPathOption | string;
  workload: string;
  rps: number;
  durationSeconds?: number;
  warmupSeconds?: number;
  connections?: number;
  status?: "Succeeded" | "Failed" | "Running";
  throughput?: number;
  errorRatePct?: number;
  p50Ms?: number;
  p75Ms?: number;
  p90Ms?: number;
  p95Ms?: number;
  p99Ms?: number;
}

export interface BenchmarkRunDetails extends BenchmarkRunListItem {
  avgMs?: number;
  minMs?: number;
  maxMs?: number;
  tool?: string;
  summaryPath?: string;
}

export interface RunSummary {
  id: string;
  startedAt: string;
  endedAt?: string;
  protocol: string;
  securityMode: string;
  callPath?: string;
  workload: string;
  rpsRequested: number;
  durationSec?: number;
  warmupSec?: number;
  connections?: number;
  status?: "Succeeded" | "Failed" | "Running";
}

export interface RunMetrics {
  latency: {
    p50?: number;
    p75?: number;
    p90?: number;
    p95?: number;
    p99?: number;
    avg?: number;
  };
  throughput: {
    achievedRps?: number;
  };
  errors?: {
    errorRatePct?: number;
  };
}

export type TabKey = "run" | "compare" | "history";
