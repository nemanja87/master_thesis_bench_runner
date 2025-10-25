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

export default Compare;
