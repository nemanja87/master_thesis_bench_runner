import clsx from "clsx";
import { useEffect, useMemo, useState } from "react";
import { useRunSelection } from "../store/selection";
import type { BenchmarkRunListItem, TabKey } from "../types";

interface HistoryProps {
  runs: BenchmarkRunListItem[];
  isLoading: boolean;
  onRefresh: () => Promise<void> | void;
  setActiveTab: (tab: TabKey) => void;
}

const PAGE_SIZE = 10;
const dateFormatter = new Intl.DateTimeFormat(undefined, {
  dateStyle: "medium",
  timeStyle: "short",
});

function History({ runs, isLoading, onRefresh, setActiveTab }: HistoryProps) {
  const [page, setPage] = useState(0);
  const [lastCheckedIndex, setLastCheckedIndex] = useState<number | null>(null);
  const [focusedRunId, setFocusedRunId] = useState<string | null>(null);
  const selectedIds = useRunSelection((state) => state.selectedIds);
  const select = useRunSelection((state) => state.select);
  const remove = useRunSelection((state) => state.remove);
  const selectMany = useRunSelection((state) => state.selectMany);
  const clearSelection = useRunSelection((state) => state.clear);
  const selectedCount = selectedIds.length;

  const totalPages = Math.max(1, Math.ceil(runs.length / PAGE_SIZE));
  const pageStart = page * PAGE_SIZE;
  const pageRuns = runs.slice(pageStart, pageStart + PAGE_SIZE);
  const focusedRun = runs.find((run) => run.id === focusedRunId) ?? runs[0] ?? null;

  useEffect(() => {
    if (pageStart >= runs.length && page > 0) {
      setPage((current) => Math.max(0, current - 1));
    }
  }, [runs.length, pageStart, page]);

  useEffect(() => {
    if (!focusedRunId && runs.length > 0) {
      setFocusedRunId(runs[0].id);
    }
  }, [runs, focusedRunId]);

  const tableRows = useMemo(() => pageRuns, [pageRuns]);

  function handleCheckboxChange(runId: string, absoluteIndex: number, checked: boolean, shiftKey: boolean) {
    if (shiftKey && lastCheckedIndex !== null) {
      const start = Math.min(lastCheckedIndex, absoluteIndex);
      const end = Math.max(lastCheckedIndex, absoluteIndex);
      const idsInRange = runs.slice(start, end + 1).map((run) => run.id);
      if (checked) {
        selectMany(idsInRange);
      } else {
        idsInRange.forEach((id) => remove(id));
      }
    } else if (checked) {
      select(runId);
    } else {
      remove(runId);
    }
    setLastCheckedIndex(absoluteIndex);
  }

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <p className="text-sm font-semibold uppercase tracking-wide text-slate-500">Screen C</p>
          <h2 className="text-3xl font-semibold text-slate-900">History</h2>
          <p className="text-base text-slate-600">Review prior runs and queue up to six for comparison.</p>
        </div>
        <button
          type="button"
          onClick={() => onRefresh?.()}
          className="inline-flex items-center rounded-xl border border-slate-300 px-4 py-2 text-sm font-semibold text-slate-700 transition hover:border-slate-400 hover:bg-slate-50"
        >
          Refresh
        </button>
      </header>

      <section className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 pb-3">
          <div>
            <p className="text-sm font-semibold text-slate-900">{runs.length} runs</p>
            <p className="text-xs text-slate-500">Select 2–6 to enable Compare.</p>
          </div>
          <div className="flex flex-wrap gap-2">
            <button
              type="button"
              onClick={() => {
                clearSelection();
                setLastCheckedIndex(null);
              }}
              className="rounded-lg border border-slate-300 px-3 py-1.5 text-sm text-slate-700 transition hover:border-slate-400 hover:bg-slate-50"
            >
              Select none
            </button>
          </div>
        </div>

        <div className="mt-3 overflow-x-auto">
          <table className="min-w-full table-fixed border-collapse text-sm">
            <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
              <tr>
                <th scope="col" className="w-10 px-3 py-2 text-left">
                  <span className="sr-only">Select run</span>
                </th>
                <th scope="col" className="px-3 py-2 text-left">Run ID</th>
                <th scope="col" className="px-3 py-2 text-left">Started</th>
                <th scope="col" className="px-3 py-2 text-left">Workload</th>
                <th scope="col" className="px-3 py-2 text-left">Protocol</th>
                <th scope="col" className="px-3 py-2 text-left">Profile</th>
                <th scope="col" className="px-3 py-2 text-left">RPS</th>
                <th scope="col" className="px-3 py-2 text-left">Duration</th>
                <th scope="col" className="px-3 py-2 text-left">Status</th>
              </tr>
            </thead>
            <tbody>
              {isLoading && (
                <tr>
                  <td colSpan={9} className="px-3 py-6 text-center text-sm text-slate-500">
                    Loading runs…
                  </td>
                </tr>
              )}
              {!isLoading && tableRows.length === 0 && (
                <tr>
                  <td colSpan={9} className="px-3 py-6 text-center text-sm text-slate-500">
                    No runs recorded.
                  </td>
                </tr>
              )}
              {tableRows.map((run, index) => {
                const absoluteIndex = pageStart + index;
                const isSelected = selectedIds.includes(run.id);
                return (
                  <tr
                    key={run.id}
                    className={clsx(
                      "cursor-pointer border-b border-slate-100 transition hover:bg-slate-50",
                      focusedRunId === run.id && "bg-slate-50",
                    )}
                    onClick={() => setFocusedRunId(run.id)}
                  >
                    <td className="px-3 py-2">
                      <input
                        type="checkbox"
                        aria-label={`Select run ${run.id}`}
                        checked={isSelected}
                        onChange={(event) => {
                          event.stopPropagation();
                          const nativeEvent = event.nativeEvent as MouseEvent | PointerEvent | KeyboardEvent;
                          const shiftKey = Boolean(nativeEvent?.shiftKey);
                          handleCheckboxChange(run.id, absoluteIndex, event.target.checked, shiftKey);
                        }}
                        onClick={(event) => event.stopPropagation()}
                        className="h-4 w-4 rounded border-slate-300 text-slate-900 focus:ring-slate-500"
                      />
                    </td>
                    <td className="px-3 py-2 font-mono text-xs text-slate-700">{run.id}</td>
                    <td className="px-3 py-2 text-slate-700">{formatDate(run.startedAt)}</td>
                    <td className="px-3 py-2 text-slate-700">{run.workload}</td>
                    <td className="px-3 py-2 text-slate-700">{run.protocol?.toUpperCase()}</td>
                    <td className="px-3 py-2 text-slate-700">{run.securityProfile}</td>
                    <td className="px-3 py-2 text-slate-700">{run.rps}</td>
                    <td className="px-3 py-2 text-slate-700">{formatDuration(run.durationSeconds)}</td>
                    <td className="px-3 py-2">
                      <StatusBadge status={run.status} />
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>

        <div className="mt-4 flex items-center justify-between">
          <p className="text-xs text-slate-500">
            Page {Math.min(page + 1, totalPages)} of {totalPages}
          </p>
          <div className="flex gap-2">
            <button
              type="button"
              onClick={() => setPage((current) => Math.max(0, current - 1))}
              disabled={page === 0}
              className="rounded-lg border border-slate-300 px-3 py-1 text-sm text-slate-700 disabled:cursor-not-allowed disabled:opacity-50"
            >
              Previous
            </button>
            <button
              type="button"
              onClick={() => setPage((current) => Math.min(totalPages - 1, current + 1))}
              disabled={page >= totalPages - 1}
              className="rounded-lg border border-slate-300 px-3 py-1 text-sm text-slate-700 disabled:cursor-not-allowed disabled:opacity-50"
            >
              Next
            </button>
          </div>
        </div>
      </section>

      {focusedRun && (
        <section className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm">
          <h3 className="text-lg font-semibold text-slate-900">Run detail</h3>
          <p className="text-sm text-slate-500">Quick snapshot of run {focusedRun.id}</p>
          <dl className="mt-4 grid gap-4 text-sm text-slate-600 md:grid-cols-3">
            <InfoRow label="Started" value={formatDate(focusedRun.startedAt)} />
            <InfoRow label="Protocol" value={focusedRun.protocol?.toUpperCase()} />
            <InfoRow label="Security" value={focusedRun.securityProfile} />
            <InfoRow label="Workload" value={focusedRun.workload} />
            <InfoRow label="Requested RPS" value={focusedRun.rps?.toString()} />
            <InfoRow label="Throughput" value={formatNumber(focusedRun.throughput)} />
            <InfoRow label="p50 (ms)" value={formatNumber(focusedRun.p50Ms)} />
            <InfoRow label="p95 (ms)" value={formatNumber(focusedRun.p95Ms)} />
            <InfoRow label="p99 (ms)" value={formatNumber(focusedRun.p99Ms)} />
          </dl>
        </section>
      )}

      {selectedCount >= 2 && (
        <div className="fixed bottom-6 left-1/2 z-40 -translate-x-1/2">
          <div className="flex items-center gap-4 rounded-2xl border border-slate-200 bg-white px-5 py-3 shadow-xl">
            <p className="text-sm font-semibold text-slate-900">{selectedCount} selected</p>
            <button
              type="button"
              onClick={() => {
                clearSelection();
                setLastCheckedIndex(null);
              }}
              className="text-sm font-medium text-slate-500 transition hover:text-slate-700"
            >
              Clear
            </button>
            <button
              type="button"
              onClick={() => setActiveTab("compare")}
              className="rounded-xl bg-slate-900 px-4 py-2 text-sm font-semibold text-white transition hover:bg-slate-800"
            >
              Compare
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

function formatDate(timestamp?: string) {
  if (!timestamp) return "—";
  try {
    return dateFormatter.format(new Date(timestamp));
  } catch {
    return timestamp;
  }
}

function formatDuration(duration?: number) {
  if (typeof duration !== "number" || Number.isNaN(duration)) {
    return "—";
  }
  return `${duration}s`;
}

function formatNumber(value?: number) {
  if (typeof value !== "number" || Number.isNaN(value)) {
    return "—";
  }
  return value.toFixed(2);
}

function InfoRow({ label, value }: { label: string; value?: string }) {
  return (
    <div>
      <dt className="text-xs uppercase tracking-wide text-slate-500">{label}</dt>
      <dd className="text-base font-semibold text-slate-900">{value ?? "—"}</dd>
    </div>
  );
}

function StatusBadge({ status }: { status?: string }) {
  if (!status) {
    return <span className="text-slate-500">—</span>;
  }
  const styles: Record<string, string> = {
    Succeeded: "bg-green-100 text-green-800",
    Failed: "bg-red-100 text-red-800",
    Running: "bg-amber-100 text-amber-800",
  };
  return (
    <span className={clsx("rounded-full px-2 py-1 text-xs font-semibold", styles[status] ?? "bg-slate-100 text-slate-700")}>
      {status}
    </span>
  );
}

export default History;
