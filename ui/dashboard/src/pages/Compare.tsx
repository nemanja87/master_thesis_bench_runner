import { useEffect, useMemo, useState } from "react";
import CompareToolbar, { type MetricOption } from "../components/CompareToolbar";
import MetricChart, { type ComparisonRun } from "../components/MetricChart";
import { getRun } from "../lib/api";
import { toRunMetrics, toRunSummary } from "../lib/runMappers";
import { useRunSelection } from "../store/selection";
import type { BenchmarkRunDetails, BenchmarkRunListItem, TabKey } from "../types";

interface CompareProps {
  runs: BenchmarkRunListItem[];
  onNavigate: (tab: TabKey) => void;
}

type LatencyMetric = "p50" | "p90" | "p99";

const LATENCY_BUCKETS: Array<{ id: LatencyMetric; label: string }> = [
  { id: "p50", label: "p50" },
  { id: "p90", label: "p90" },
  { id: "p99", label: "p99" },
];

const RUN_COLORS = ["#0f172a", "#2563eb", "#f97316", "#0d9488", "#f43f5e", "#7c3aed"];
const detailDateFormatter = new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" });

function Compare({ runs, onNavigate }: CompareProps) {
  const selectedIds = useRunSelection((state) => state.selectedIds);
  const remove = useRunSelection((state) => state.remove);
  const clearSelection = useRunSelection((state) => state.clear);
  const [details, setDetails] = useState<Record<string, BenchmarkRunDetails>>({});
  const [axisScale, setAxisScale] = useState<"linear" | "log">("linear");
  const [selectedMetrics, setSelectedMetrics] = useState<MetricOption[]>(["p50", "p90", "p99", "throughput"]);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let canceled = false;

    async function hydrateRuns() {
      const missing = selectedIds.filter((id) => !details[id]);
      if (missing.length === 0) {
        setIsLoading(false);
        return;
      }
      setIsLoading(true);
      setError(null);
      try {
        const fetched = await Promise.all(
          missing.map(async (id) => {
            const record = await getRun<BenchmarkRunDetails>(id);
            return [id, record] as const;
          }),
        );
        if (!canceled) {
          setDetails((previous) => {
            const next = { ...previous };
            for (const [id, record] of fetched) {
              next[id] = record;
            }
            return next;
          });
        }
      } catch (err) {
        if (!canceled) {
          setError(err instanceof Error ? err.message : "Failed to load run details.");
        }
      } finally {
        if (!canceled) {
          setIsLoading(false);
        }
      }
    }

    if (selectedIds.length > 0) {
      void hydrateRuns();
    } else {
      setIsLoading(false);
    }

    return () => {
      canceled = true;
    };
  }, [selectedIds, details]);

  const chipSummaries = useMemo(() => {
    return selectedIds.map((id, index) => {
      const detail = details[id];
      const fallback = runs.find((run) => run.id === id);
      const summarySource = detail ?? fallback;
      return {
        id,
        color: RUN_COLORS[index % RUN_COLORS.length],
        summary: summarySource ? toRunSummary(summarySource) : null,
      };
    });
  }, [selectedIds, details, runs]);

  const comparisonRuns: ComparisonRun[] = useMemo(() => {
    return chipSummaries
      .map((chip) => {
        const detail = details[chip.id];
        const fallback = runs.find((run) => run.id === chip.id);
        const source = detail ?? fallback;
        if (!source) {
          return null;
        }
        return {
          id: chip.id,
          label: chip.summary?.workload ?? chip.id,
          color: chip.color,
          metrics: toRunMetrics(source),
        };
      })
      .filter(Boolean) as ComparisonRun[];
  }, [chipSummaries, details, runs]);

  const activeLatencyBuckets = LATENCY_BUCKETS.filter((bucket) => selectedMetrics.includes(bucket.id));
  const showThroughput = selectedMetrics.includes("throughput");
  const showErrors = selectedMetrics.includes("errorRate");
  const comparisonRunMap = useMemo(() => {
    const map: Record<string, ComparisonRun> = {};
    for (const run of comparisonRuns) {
      map[run.id] = run;
    }
    return map;
  }, [comparisonRuns]);

  const detailRows = useMemo(
    () =>
      chipSummaries.map((chip) => ({
        id: chip.id,
        summary: chip.summary,
        metrics: comparisonRunMap[chip.id]?.metrics,
      })),
    [chipSummaries, comparisonRunMap],
  );

  function toggleMetric(metric: MetricOption) {
    setSelectedMetrics((current) => {
      if (current.includes(metric)) {
        const next = current.filter((item) => item !== metric);
        const hasLatency = next.some((item) => LATENCY_BUCKETS.some((bucket) => bucket.id === item));
        if (metric === "throughput" && !next.includes("throughput")) {
          return current;
        }
        if (!hasLatency) {
          return current;
        }
        return next;
      }
      return [...current, metric];
    });
  }

  const hasEnoughRuns = selectedIds.length >= 2;

  return (
    <div className="space-y-6">
      <header>
        <p className="text-sm font-semibold uppercase tracking-wide text-slate-500">Screen B</p>
        <h2 className="text-3xl font-semibold text-slate-900">Compare Results</h2>
        <p className="text-base text-slate-600">Analyze latency percentiles, throughput, and error rates side-by-side.</p>
      </header>

      <div className="flex flex-wrap gap-2">
        {chipSummaries.map((chip) => (
          <RunChip
            key={chip.id}
            id={chip.id}
            color={chip.color}
            summary={chip.summary}
            onRemove={() => remove(chip.id)}
          />
        ))}
      </div>

      <CompareToolbar
        selectedMetrics={selectedMetrics}
        onToggleMetric={toggleMetric}
        axisScale={axisScale}
        onAxisScaleChange={setAxisScale}
        onClearSelection={clearSelection}
        canClear={selectedIds.length > 0}
      />

      {error && (
        <div className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
          {error}
        </div>
      )}
      {isLoading && (
        <div className="rounded-xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-600">
          Loading run details…
        </div>
      )}

      {!hasEnoughRuns ? (
        <section className="rounded-2xl border border-dashed border-slate-300 bg-white p-10 text-center shadow-sm">
          <p className="text-base text-slate-600">Select 2–6 runs from History to unlock comparison charts.</p>
          <button
            type="button"
            onClick={() => onNavigate("history")}
            className="mt-4 rounded-2xl bg-slate-900 px-5 py-3 text-base font-semibold text-white transition hover:bg-slate-800"
          >
            Go to History
          </button>
        </section>
      ) : (
        <div className="space-y-6">
          {activeLatencyBuckets.length > 0 && (
            <MetricChart
              title="Latency Percentiles"
              variant="latency"
              runs={comparisonRuns}
              axisScale={axisScale}
              latencyBuckets={activeLatencyBuckets}
              emptyMessage="Latency percentiles are unavailable for the selected runs."
            />
          )}

          {showThroughput && (
            <MetricChart
              title="Throughput (requests/sec)"
              variant="bar"
              runs={comparisonRuns}
              axisScale={axisScale}
              metricLabel="req/s"
              metricAccessor={(run) => run.metrics.throughput.achievedRps}
              emptyMessage="Throughput metrics unavailable."
            />
          )}

          {showErrors && (
            <MetricChart
              title="Error rate (%)"
              variant="bar"
              runs={comparisonRuns}
              axisScale={axisScale}
              metricLabel="%"
              metricAccessor={(run) => run.metrics.errors?.errorRatePct}
              emptyMessage="Error rate metrics unavailable."
            />
          )}

          {detailRows.length > 0 && (
            <section className="w-full rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
              <h3 className="text-lg font-semibold text-slate-900">Metric Details</h3>
              <p className="text-sm text-slate-500">Numeric values for the selected runs.</p>
              <div className="mt-4 overflow-x-auto">
                <table className="w-full border-collapse text-sm">
                  <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
                    <tr>
                      <th scope="col" className="px-3 py-2 text-left">Run ID</th>
                      <th scope="col" className="px-3 py-2 text-left">Started</th>
                      <th scope="col" className="px-3 py-2 text-left">Protocol</th>
                      <th scope="col" className="px-3 py-2 text-left">Profile</th>
                      <th scope="col" className="px-3 py-2 text-left">Requested RPS</th>
                      <th scope="col" className="px-3 py-2 text-left">Throughput</th>
                      <th scope="col" className="px-3 py-2 text-left">p50 (ms)</th>
                      <th scope="col" className="px-3 py-2 text-left">p90 (ms)</th>
                      <th scope="col" className="px-3 py-2 text-left">p99 (ms)</th>
                      <th scope="col" className="px-3 py-2 text-left">Error %</th>
                    </tr>
                  </thead>
                  <tbody>
                    {detailRows.map((row) => (
                      <tr key={row.id} className="border-b border-slate-100">
                        <td className="px-3 py-2 font-mono text-xs text-slate-700">{row.id}</td>
                        <td className="px-3 py-2 text-slate-700">{formatDate(row.summary?.startedAt)}</td>
                        <td className="px-3 py-2 text-slate-700">{row.summary?.protocol ?? "—"}</td>
                        <td className="px-3 py-2 text-slate-700">{row.summary?.securityMode ?? "—"}</td>
                        <td className="px-3 py-2 text-slate-700">{row.summary?.rpsRequested ?? "—"}</td>
                        <td className="px-3 py-2 text-slate-700">{formatNumber(row.metrics?.throughput.achievedRps)}</td>
                        <td className="px-3 py-2 text-slate-700">{formatNumber(row.metrics?.latency.p50)}</td>
                        <td className="px-3 py-2 text-slate-700">{formatNumber(row.metrics?.latency.p90)}</td>
                        <td className="px-3 py-2 text-slate-700">{formatNumber(row.metrics?.latency.p99)}</td>
                        <td className="px-3 py-2 text-slate-700">{formatNumber(row.metrics?.errors?.errorRatePct)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </section>
          )}
        </div>
      )}
    </div>
  );
}

interface RunChipProps {
  id: string;
  color: string;
  summary: ReturnType<typeof toRunSummary> | null;
  onRemove: () => void;
}

function RunChip({ id, color, summary, onRemove }: RunChipProps) {
  return (
    <div className="inline-flex items-center gap-3 rounded-full border border-slate-200 bg-white px-4 py-1.5 text-sm shadow-sm">
      <span className="flex h-2 w-2 rounded-full" style={{ backgroundColor: color }} aria-hidden="true" />
      <div className="flex flex-col text-left">
        <span className="font-semibold text-slate-900">{summary?.workload ?? id}</span>
        <span className="text-xs text-slate-500">
          {summary?.protocol ?? "—"} • {summary?.securityMode ?? "—"}
        </span>
      </div>
      <button
        type="button"
        onClick={onRemove}
        className="text-slate-400 transition hover:text-slate-600"
        aria-label={`Remove run ${id}`}
      >
        ×
      </button>
    </div>
  );
}

function formatDate(value?: string) {
  if (!value) {
    return "—";
  }
  try {
    return detailDateFormatter.format(new Date(value));
  } catch {
    return value;
  }
}

function formatNumber(value?: number) {
  if (typeof value !== "number" || Number.isNaN(value)) {
    return "—";
  }
  return value.toFixed(2);
}

export default Compare;
