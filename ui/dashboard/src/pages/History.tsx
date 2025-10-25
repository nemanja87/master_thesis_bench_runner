import clsx from "clsx";
import { useMemo, useState } from "react";
import { useRunSelection } from "../store/selection";
import type { BenchmarkRunListItem, TabKey } from "../types";

interface HistoryProps {
  runs: BenchmarkRunListItem[];
  isLoading: boolean;
  onRefresh: () => Promise<void> | void;
  setActiveTab: (tab: TabKey) => void;
}

type SortColumn = "startedAt" | "securityProfile" | "rps" | "protocol";

const dateFormatter = new Intl.DateTimeFormat(undefined, {
  dateStyle: "medium",
  timeStyle: "short",
});

function History({ runs, isLoading, onRefresh, setActiveTab }: HistoryProps) {
  const [lastCheckedIndex, setLastCheckedIndex] = useState<number | null>(null);
  const [sortState, setSortState] = useState<Array<{ column: SortColumn; direction: "asc" | "desc" }>>([
    { column: "startedAt", direction: "desc" },
  ]);
  const selectedIds = useRunSelection((state) => state.selectedIds);
  const select = useRunSelection((state) => state.select);
  const remove = useRunSelection((state) => state.remove);
  const selectMany = useRunSelection((state) => state.selectMany);
  const clearSelection = useRunSelection((state) => state.clear);
  const selectedCount = selectedIds.length;

  const effectiveSortState: Array<{ column: SortColumn; direction: "asc" | "desc" }> =
    sortState.length > 0 ? sortState : [{ column: "startedAt", direction: "desc" }];

  const sortedRuns = useMemo(() => {
    const copy = [...runs];
    copy.sort((a, b) => {
      for (const rule of effectiveSortState) {
        let comparison = 0;
        switch (rule.column) {
          case "startedAt": {
            const aTime = a.startedAt ? new Date(a.startedAt).getTime() : 0;
            const bTime = b.startedAt ? new Date(b.startedAt).getTime() : 0;
            comparison = aTime - bTime;
            break;
          }
          case "securityProfile":
            comparison = (a.securityProfile ?? "").localeCompare(b.securityProfile ?? "");
            break;
          case "protocol":
            comparison = (a.protocol ?? "").localeCompare(b.protocol ?? "");
            break;
          case "rps":
            comparison = (a.rps ?? 0) - (b.rps ?? 0);
            break;
          default:
            comparison = 0;
        }

        if (comparison !== 0) {
          return rule.direction === "asc" ? comparison : -comparison;
        }
      }
      return 0;
    });
    return copy;
  }, [runs, effectiveSortState]);

  const tableRows = useMemo(() => sortedRuns, [sortedRuns]);

  const handleSort = (column: SortColumn) => {
    setSortState((current) => {
      const index = current.findIndex((entry) => entry.column === column);
      if (index === -1) {
        return [...current, { column, direction: column === "startedAt" ? "desc" : "asc" }];
      }
      const next = [...current];
      if (next[index].direction === "asc") {
        next[index] = { column, direction: "desc" };
        return next;
      }
      next.splice(index, 1);
      return next.length > 0 ? next : [{ column: "startedAt", direction: "desc" }];
    });
  };

  function handleCheckboxChange(runId: string, absoluteIndex: number, checked: boolean, shiftKey: boolean) {
    if (shiftKey && lastCheckedIndex !== null) {
      const start = Math.min(lastCheckedIndex, absoluteIndex);
      const end = Math.max(lastCheckedIndex, absoluteIndex);
      const idsInRange = sortedRuns.slice(start, end + 1).map((run) => run.id);
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
          <div className="max-h-[65vh] overflow-y-auto">
          <table className="min-w-full table-fixed border-collapse text-sm">
              <thead className="sticky top-0 z-10 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
                <tr>
                  <th scope="col" className="w-10 px-3 py-2 text-left">
                    <span className="sr-only">Select run</span>
                  </th>
                  <th scope="col" className="px-3 py-2 text-left">Run ID</th>
                  <th scope="col" className="px-3 py-2 text-left">
                    <SortableHeader column="startedAt" label="Started" sortState={effectiveSortState} onSort={handleSort} />
                  </th>
                  <th scope="col" className="px-3 py-2 text-left">Workload</th>
                  <th scope="col" className="px-3 py-2 text-left">
                    <SortableHeader column="protocol" label="Protocol" sortState={effectiveSortState} onSort={handleSort} />
                  </th>
                  <th scope="col" className="px-3 py-2 text-left">
                    <SortableHeader column="securityProfile" label="Profile" sortState={effectiveSortState} onSort={handleSort} />
                  </th>
                  <th scope="col" className="px-3 py-2 text-left">
                    <SortableHeader column="rps" label="RPS" sortState={effectiveSortState} onSort={handleSort} />
                  </th>
                  <th scope="col" className="px-3 py-2 text-left">p50 (ms)</th>
                  <th scope="col" className="px-3 py-2 text-left">p95 (ms)</th>
                  <th scope="col" className="px-3 py-2 text-left">p99 (ms)</th>
                  <th scope="col" className="px-3 py-2 text-left">Throughput</th>
                  <th scope="col" className="px-3 py-2 text-left">Error %</th>
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
                const absoluteIndex = index;
                const isSelected = selectedIds.includes(run.id);
                return (
                  <tr
                    key={run.id}
                    className={clsx("border-b border-slate-100 transition hover:bg-slate-50")}
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
                    <td className="px-3 py-2 text-slate-700">{formatNumber(run.p50Ms)}</td>
                    <td className="px-3 py-2 text-slate-700">{formatNumber(run.p95Ms)}</td>
                    <td className="px-3 py-2 text-slate-700">{formatNumber(run.p99Ms)}</td>
                    <td className="px-3 py-2 text-slate-700">{formatNumber(run.throughput)}</td>
                    <td className="px-3 py-2 text-slate-700">{formatNumber(run.errorRatePct)}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
          </div>
        </div>
        <div className="mt-4 text-xs text-slate-500">Showing {tableRows.length} total runs.</div>
      </section>

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

function formatNumber(value?: number) {
  if (typeof value !== "number" || Number.isNaN(value)) {
    return "—";
  }
  return value.toFixed(2);
}

function SortableHeader({
  column,
  label,
  sortState,
  onSort,
}: {
  column: SortColumn;
  label: string;
  sortState: Array<{ column: SortColumn; direction: "asc" | "desc" }>;
  onSort: (column: SortColumn) => void;
}) {
  const entryIndex = sortState.findIndex((entry) => entry.column === column);
  const entry = entryIndex >= 0 ? sortState[entryIndex] : null;
  const indicator = entry ? (entry.direction === "asc" ? "↑" : "↓") : "↕";

  return (
    <button
      type="button"
      onClick={() => onSort(column)}
      className="inline-flex items-center gap-1 text-slate-600 transition hover:text-slate-900"
    >
      {label}
      <span aria-hidden="true" className="text-[10px]">
        {indicator}
      </span>
      {entry && (
        <span className="rounded-full bg-slate-200 px-1 text-[10px] text-slate-700">
          {entryIndex + 1}
        </span>
      )}
    </button>
  );
}

export default History;
