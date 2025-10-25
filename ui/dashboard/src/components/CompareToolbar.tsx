import clsx from "clsx";

export type MetricOption = "p50" | "p90" | "p99" | "throughput" | "errorRate";

const METRIC_OPTIONS: Array<{ id: MetricOption; label: string }> = [
  { id: "p50", label: "Latency p50" },
  { id: "p90", label: "Latency p90" },
  { id: "p99", label: "Latency p99" },
  { id: "throughput", label: "Throughput" },
  { id: "errorRate", label: "Error rate" },
];

interface CompareToolbarProps {
  selectedMetrics: MetricOption[];
  onToggleMetric: (metric: MetricOption) => void;
  axisScale: "linear" | "log";
  onAxisScaleChange: (scale: "linear" | "log") => void;
  onClearSelection: () => void;
  canClear: boolean;
}

function CompareToolbar({
  selectedMetrics,
  onToggleMetric,
  axisScale,
  onAxisScaleChange,
  onClearSelection,
  canClear,
}: CompareToolbarProps) {
  return (
    <div className="flex flex-col gap-4 rounded-2xl border border-slate-200 bg-white p-4 shadow-sm lg:flex-row lg:items-center lg:justify-between">
      <div>
        <p className="text-sm font-semibold text-slate-900">Metric selector</p>
        <div className="mt-2 flex flex-wrap gap-2">
          {METRIC_OPTIONS.map((option) => {
            const active = selectedMetrics.includes(option.id);
            return (
              <button
                key={option.id}
                type="button"
                className={clsx(
                  "rounded-full px-4 py-1.5 text-sm font-medium transition",
                  active
                    ? "bg-slate-900 text-white shadow"
                    : "bg-slate-100 text-slate-600 hover:bg-slate-200 hover:text-slate-900",
                )}
                onClick={() => onToggleMetric(option.id)}
                aria-pressed={active}
              >
                {option.label}
              </button>
            );
          })}
        </div>
      </div>
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
        <div>
          <p className="text-xs uppercase tracking-wide text-slate-500">Axis scale</p>
          <div className="mt-2 inline-flex overflow-hidden rounded-2xl border border-slate-200">
            {(["linear", "log"] as const).map((scale) => (
              <button
                key={scale}
                type="button"
                onClick={() => onAxisScaleChange(scale)}
                className={clsx(
                  "px-4 py-2 text-sm font-semibold transition",
                  axisScale === scale ? "bg-slate-900 text-white" : "bg-white text-slate-700 hover:bg-slate-50",
                )}
              >
                {scale === "linear" ? "Linear" : "Log"}
              </button>
            ))}
          </div>
        </div>
        <button
          type="button"
          onClick={onClearSelection}
          disabled={!canClear}
          className="rounded-2xl border border-slate-300 px-4 py-2 text-sm font-semibold text-slate-700 transition hover:border-slate-400 hover:bg-slate-50 disabled:cursor-not-allowed disabled:border-slate-200 disabled:text-slate-400"
        >
          Clear selection
        </button>
      </div>
    </div>
  );
}

export { METRIC_OPTIONS };
export default CompareToolbar;
