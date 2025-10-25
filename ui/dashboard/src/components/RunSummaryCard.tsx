import clsx from "clsx";
import type { RunMetrics, RunSummary } from "../types";

interface RunSummaryCardProps {
  summary: RunSummary;
  metrics?: RunMetrics | null;
  onClose?: () => void;
  onViewHistory?: () => void;
  onCompare?: () => void;
}

const timeFormatter = new Intl.DateTimeFormat(undefined, {
  dateStyle: "medium",
  timeStyle: "short",
});

function formatTimestamp(value?: string) {
  if (!value) {
    return "Pending";
  }
  try {
    return timeFormatter.format(new Date(value));
  } catch {
    return value;
  }
}

const statusStyles: Record<string, string> = {
  Succeeded: "bg-green-100 text-green-800",
  Failed: "bg-red-100 text-red-800",
  Running: "bg-amber-100 text-amber-800",
};

function metricRow(label: string, value?: number, suffix = "") {
  if (!Number.isFinite(value ?? NaN)) {
    return null;
  }
  return (
    <div key={label} className="flex flex-col">
      <span className="text-xs uppercase tracking-wider text-slate-500">{label}</span>
      <span className="text-sm font-semibold text-slate-900">
        {value?.toFixed?.(2)} {suffix}
      </span>
    </div>
  );
}

function RunSummaryCard({ summary, metrics, onClose, onViewHistory, onCompare }: RunSummaryCardProps) {
  return (
    <div
      className="pointer-events-auto max-w-lg rounded-2xl border border-slate-200 bg-white/95 p-5 shadow-2xl ring-1 ring-black/5 backdrop-blur"
      role="status"
      aria-live="polite"
    >
      <div className="flex items-start justify-between gap-4">
        <div>
          <p className="text-xs uppercase tracking-wide text-slate-500">Run completed</p>
          <h3 className="text-lg font-semibold text-slate-900">Run {summary.id}</h3>
        </div>
        {summary.status && (
          <span
            className={clsx(
              "rounded-full px-3 py-1 text-xs font-semibold",
              statusStyles[summary.status] ?? "bg-slate-100 text-slate-700",
            )}
          >
            {summary.status}
          </span>
        )}
        {onClose && (
          <button
            type="button"
            onClick={onClose}
            className="text-slate-400 transition hover:text-slate-600"
            aria-label="Dismiss run summary"
          >
            ×
          </button>
        )}
      </div>

      <dl className="mt-4 grid grid-cols-2 gap-3 text-sm text-slate-600">
        <div>
          <dt className="text-xs text-slate-500">Started</dt>
          <dd className="font-medium text-slate-900">{formatTimestamp(summary.startedAt)}</dd>
        </div>
        <div>
          <dt className="text-xs text-slate-500">Finished</dt>
          <dd className="font-medium text-slate-900">{formatTimestamp(summary.endedAt)}</dd>
        </div>
        <div>
          <dt className="text-xs text-slate-500">Protocol</dt>
          <dd className="font-medium text-slate-900">{summary.protocol}</dd>
        </div>
        <div>
          <dt className="text-xs text-slate-500">Call path</dt>
          <dd className="font-medium text-slate-900 capitalize">{summary.callPath ?? "—"}</dd>
        </div>
        <div>
          <dt className="text-xs text-slate-500">Security</dt>
          <dd className="font-medium text-slate-900">{summary.securityMode}</dd>
        </div>
        <div className="col-span-2">
          <dt className="text-xs text-slate-500">Workload</dt>
          <dd className="font-medium text-slate-900">{summary.workload}</dd>
        </div>
      </dl>

      <div className="mt-4 grid grid-cols-3 gap-3">
        {metricRow("Target RPS", summary.rpsRequested)}
        {metricRow("Duration (s)", summary.durationSec)}
        {metricRow("Warmup (s)", summary.warmupSec)}
        {metricRow("Connections", summary.connections)}
        {metricRow("p50 (ms)", metrics?.latency.p50)}
        {metricRow("p99 (ms)", metrics?.latency.p99)}
        {metricRow("Throughput (rps)", metrics?.throughput.achievedRps)}
        {metricRow("Error %", metrics?.errors?.errorRatePct)}
      </div>

      <div className="mt-5 flex flex-wrap gap-3">
        {onViewHistory && (
          <button
            type="button"
            onClick={onViewHistory}
            className="rounded-lg border border-slate-300 px-4 py-2 text-sm font-medium text-slate-700 transition hover:border-slate-400 hover:bg-slate-50"
          >
            View in History
          </button>
        )}
        {onCompare && (
          <button
            type="button"
            onClick={onCompare}
            className="rounded-lg bg-slate-900 px-4 py-2 text-sm font-semibold text-white transition hover:bg-slate-800"
          >
            Compare
          </button>
        )}
      </div>
    </div>
  );
}

export default RunSummaryCard;
