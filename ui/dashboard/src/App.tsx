import { useEffect, useMemo, useState } from "react";
import type { FormEvent } from "react";
import "./App.css";
import { getHealth, listRuns, getRun, startBench } from "./lib/api";

interface BenchRunRequest {
  protocol: "rest" | "grpc";
  security: "S0" | "S1" | "S2" | "S3" | "S4";
  workload: string;
  rps: number;
  connections: number;
  duration: number;
  warmup: number;
}

interface BenchmarkRunListItem {
  id: string;
  startedAt: string;
  protocol: string;
  securityProfile: string;
  workload: string;
  rps: number;
  p50Ms: number;
  p95Ms: number;
  p99Ms: number;
  throughput: number;
  errorRatePct: number;
}

interface BenchmarkRunDetails extends BenchmarkRunListItem {
  connections: number;
  durationSeconds: number;
  warmupSeconds: number;
  p75Ms: number;
  p90Ms: number;
  avgMs: number;
  minMs: number;
  maxMs: number;
  tool: string;
  summaryPath: string;
}

interface HealthStatus {
  status: string;
  profile: string;
  requiresHttps: boolean;
  requiresMtls: boolean;
  requiresJwt: boolean;
  runs: number;
}

interface BenchRunResponse {
  id: string;
  message?: string;
}

function normalizeNumber(value: number, fractionDigits = 2) {
  return Number.isFinite(value) ? value.toFixed(fractionDigits) : "-";
}

const defaultRequest: BenchRunRequest = {
  protocol: "grpc",
  security: "S2",
  workload: "orders-create",
  rps: 10,
  connections: 1,
  duration: 5,
  warmup: 1,
};

function App() {
  const [health, setHealth] = useState<HealthStatus | null>(null);
  const [runs, setRuns] = useState<BenchmarkRunListItem[]>([]);
  const [selectedRunId, setSelectedRunId] = useState<string | null>(null);
  const [selectedRun, setSelectedRun] = useState<BenchmarkRunDetails | null>(null);
  const [formState, setFormState] = useState<BenchRunRequest>(defaultRequest);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [submitMessage, setSubmitMessage] = useState<string | null>(null);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [securitySynced, setSecuritySynced] = useState(false);

  useEffect(() => {
    void refreshAll();
  }, []);

  useEffect(() => {
    if (selectedRunId) {
      void fetchRunDetails(selectedRunId);
    }
  }, [selectedRunId]);

  useEffect(() => {
    if (!securitySynced && health?.profile) {
      const profile = health.profile.toUpperCase();
      if (["S0", "S1", "S2", "S3", "S4"].includes(profile)) {
        setFormState((current) => ({ ...current, security: profile as BenchRunRequest["security"] }));
        setSecuritySynced(true);
      }
    }
  }, [health, securitySynced]);

  async function refreshAll() {
    await Promise.all([fetchHealth(), fetchRuns()]);
  }

  async function fetchHealth() {
    try {
      const data = await getHealth<HealthStatus>();
      setHealth(data);
    } catch (error) {
      console.error("Failed to load health status", error);
    }
  }

  async function fetchRuns() {
    try {
      const data = await listRuns<BenchmarkRunListItem[]>();
      setRuns(data);
      if (data.length > 0) {
        setSelectedRunId((previous) => previous ?? data[0].id);
      }
    } catch (error) {
      console.error("Failed to load runs", error);
    }
  }

  async function fetchRunDetails(id: string) {
    try {
      const data = await getRun<BenchmarkRunDetails>(id);
      setSelectedRun(data);
    } catch (error) {
      console.error("Failed to load run details", error);
    }
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSubmitMessage(null);
    setSubmitError(null);
    setIsSubmitting(true);

    try {
      const payload = await startBench<BenchRunResponse>(formState);
      setSubmitMessage(payload?.message ?? "Benchmark completed successfully");
      if (payload?.id) {
        setSelectedRunId(payload.id);
      }
      await fetchRuns();
    } catch (error) {
      setSubmitError(error instanceof Error ? error.message : "Benchmark failed");
    } finally {
      setIsSubmitting(false);
    }
  }

  const percentiles = useMemo(() => runs.map((run) => ({
    id: run.id,
    startedAt: new Date(run.startedAt),
    p50: run.p50Ms,
    p95: run.p95Ms,
    p99: run.p99Ms,
  })), [runs]);

  return (
    <div className="app-container">
      <aside className="sidebar">
        <h1>BenchRunner</h1>
        <div className="profile-card">
          <h2>Security Profile</h2>
          <p className="profile-name">{health?.profile ?? "Unknown"}</p>
          <ul>
            <li>HTTPS Required: {health?.requiresHttps ? "Yes" : "No"}</li>
            <li>mTLS Required: {health?.requiresMtls ? "Yes" : "No"}</li>
            <li>JWT Required: {health?.requiresJwt ? "Yes" : "No"}</li>
          </ul>
        </div>
        <div className="summary-card">
          <h2>Recent Runs</h2>
          <p>{health?.runs ?? runs.length} recorded</p>
        </div>
      </aside>

      <main className="main-content">
        <section className="panel">
          <h2>Run Benchmark</h2>
          <form className="run-form" onSubmit={handleSubmit}>
            <div className="form-grid">
              <label>
                Protocol
                <select
                  value={formState.protocol}
                  onChange={(event) =>
                    setFormState({ ...formState, protocol: event.target.value as BenchRunRequest["protocol"] })
                  }
                >
                  <option value="rest">REST</option>
                  <option value="grpc">gRPC</option>
                </select>
              </label>
              <label>
                Security
                <select
                  value={formState.security}
                  onChange={(event) => {
                    setSecuritySynced(true);
                    setFormState({ ...formState, security: event.target.value as BenchRunRequest["security"] });
                  }}
                >
                  <option value="S0">S0 - HTTP</option>
                  <option value="S1">S1 - TLS</option>
                  <option value="S2">S2 - TLS + JWT</option>
                  <option value="S3">S3 - mTLS</option>
                  <option value="S4">S4 - mTLS + JWT</option>
                </select>
              </label>
              <label>
                RPS
                <input
                  type="number"
                  min={1}
                  value={formState.rps}
                  onChange={(event) => setFormState({ ...formState, rps: Number(event.target.value) })}
                />
              </label>
              <label>
                Connections
                <input
                  type="number"
                  min={1}
                  value={formState.connections}
                  onChange={(event) => setFormState({ ...formState, connections: Number(event.target.value) })}
                />
              </label>
              <label>
                Duration (s)
                <input
                  type="number"
                  min={1}
                  value={formState.duration}
                  onChange={(event) => setFormState({ ...formState, duration: Number(event.target.value) })}
                />
              </label>
              <label>
                Warmup (s)
                <input
                  type="number"
                  min={0}
                  value={formState.warmup}
                  onChange={(event) => setFormState({ ...formState, warmup: Number(event.target.value) })}
                />
              </label>
            </div>
            <button type="submit" disabled={isSubmitting}>
              {isSubmitting ? "Running…" : "Start Benchmark"}
            </button>
            {submitMessage && <p className="form-message success">{submitMessage}</p>}
            {submitError && <p className="form-message error">{submitError}</p>}
          </form>
        </section>

        <section className="panel">
          <h2>Latency Comparison</h2>
          <div className="comparison-grid">
            <table>
              <thead>
                <tr>
                  <th>Started</th>
                  <th>Protocol</th>
                  <th>Security</th>
                  <th>P50 (ms)</th>
                  <th>P95 (ms)</th>
                  <th>P99 (ms)</th>
                  <th>Error %</th>
                </tr>
              </thead>
              <tbody>
                {runs.map((run) => (
                  <tr key={run.id} className={run.id === selectedRunId ? "selected" : ""}>
                    <td>{new Date(run.startedAt).toLocaleString()}</td>
                    <td>{run.protocol.toUpperCase()}</td>
                    <td>{run.securityProfile}</td>
                    <td>{normalizeNumber(run.p50Ms)}</td>
                    <td>{normalizeNumber(run.p95Ms)}</td>
                    <td>{normalizeNumber(run.p99Ms)}</td>
                    <td>{normalizeNumber(run.errorRatePct)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            <div className="chart-container">
              <PercentileChart data={percentiles} />
            </div>
          </div>
        </section>

        <section className="panel">
          <h2>History</h2>
          <div className="history-layout">
            <ul className="history-list">
              {runs.map((run) => (
                <li key={run.id}>
                  <button
                    className={run.id === selectedRunId ? "history-button selected" : "history-button"}
                    onClick={() => setSelectedRunId(run.id)}
                  >
                    <span>{new Date(run.startedAt).toLocaleString()}</span>
                    <span>{run.protocol.toUpperCase()} • {run.securityProfile}</span>
                  </button>
                </li>
              ))}
            </ul>
            <div className="history-details">
              {selectedRun ? (
                <div>
                  <h3>Run {selectedRun.id}</h3>
                  <p>Started: {new Date(selectedRun.startedAt).toLocaleString()}</p>
                  <p>Workload: {selectedRun.workload}</p>
                  <p>Protocol: {selectedRun.protocol.toUpperCase()} • Security: {selectedRun.securityProfile}</p>
                  <p>RPS: {selectedRun.rps} • Connections: {selectedRun.connections}</p>
                  <p>Duration: {selectedRun.durationSeconds}s • Warmup: {selectedRun.warmupSeconds}s</p>
                  <p>Tool: {selectedRun.tool}</p>
                  <p>Summary: {selectedRun.summaryPath}</p>
                  <div className="metrics-grid">
                    <MetricBlock label="P75" value={selectedRun.p75Ms} />
                    <MetricBlock label="P90" value={selectedRun.p90Ms} />
                    <MetricBlock label="Average" value={selectedRun.avgMs} />
                    <MetricBlock label="Min" value={selectedRun.minMs} />
                    <MetricBlock label="Max" value={selectedRun.maxMs} />
                    <MetricBlock label="Throughput" value={selectedRun.throughput} suffix=" req/s" />
                    <MetricBlock label="Error Rate" value={selectedRun.errorRatePct} suffix=" %" />
                  </div>
                </div>
              ) : (
                <p>Select a run to inspect detailed metrics.</p>
              )}
            </div>
          </div>
        </section>
      </main>
    </div>
  );
}

interface PercentilePoint {
  id: string;
  startedAt: Date;
  p50: number;
  p95: number;
  p99: number;
}

function PercentileChart({ data }: { data: PercentilePoint[] }) {
  if (data.length === 0) {
    return <div className="chart-empty">No runs to visualise yet.</div>;
  }

  const maxLatency = Math.max(...data.flatMap((point) => [point.p50, point.p95, point.p99]));
  const chartHeight = 160;
  const chartWidth = 480;
  const padding = 20;
  const scaleY = (value: number) => chartHeight - padding - (value / maxLatency) * (chartHeight - padding * 2);
  const scaleX = (index: number) => padding + (index / Math.max(1, data.length - 1)) * (chartWidth - padding * 2);

  const createPath = (extractor: (point: PercentilePoint) => number) => {
    return data
      .map((point, index) => `${index === 0 ? "M" : "L"}${scaleX(index)},${scaleY(extractor(point))}`)
      .join(" ");
  };

  return (
    <svg className="chart" viewBox={`0 0 ${chartWidth} ${chartHeight}`}>
      <line x1={padding} x2={padding} y1={padding} y2={chartHeight - padding} className="axis" />
      <line x1={padding} x2={chartWidth - padding} y1={chartHeight - padding} y2={chartHeight - padding} className="axis" />
      <path d={createPath((point) => point.p50)} className="line p50" />
      <path d={createPath((point) => point.p95)} className="line p95" />
      <path d={createPath((point) => point.p99)} className="line p99" />
      {data.map((point, index) => (
        <g key={point.id}>
          <circle cx={scaleX(index)} cy={scaleY(point.p50)} r={3} className="point p50" />
          <circle cx={scaleX(index)} cy={scaleY(point.p95)} r={3} className="point p95" />
          <circle cx={scaleX(index)} cy={scaleY(point.p99)} r={3} className="point p99" />
        </g>
      ))}
      <text x={padding} y={padding} className="chart-title">Latency percentiles (ms)</text>
    </svg>
  );
}

function MetricBlock({ label, value, suffix }: { label: string; value: number; suffix?: string }) {
  return (
    <div className="metric-block">
      <span className="metric-label">{label}</span>
      <span className="metric-value">{normalizeNumber(value)}{suffix ?? ""}</span>
    </div>
  );
}

export default App;
