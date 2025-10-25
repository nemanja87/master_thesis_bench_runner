import clsx from "clsx";
import { useEffect, useMemo, useState } from "react";
import { getHealth, listRuns } from "./lib/api";
import Compare from "./pages/Compare";
import History from "./pages/History";
import RunExperiments from "./pages/RunExperiments";
import { useRunSelection } from "./store/selection";
import type { BenchmarkRunListItem, HealthStatus, TabKey } from "./types";

const NAV_ITEMS: Array<{ id: TabKey; label: string }> = [
  { id: "run", label: "Run Experiments" },
  { id: "compare", label: "Compare" },
  { id: "history", label: "History" },
];

function App() {
  const [activeTab, setActiveTab] = useState<TabKey>("run");
  const [runs, setRuns] = useState<BenchmarkRunListItem[]>([]);
  const [health, setHealth] = useState<HealthStatus | null>(null);
  const [isLoadingRuns, setIsLoadingRuns] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const selectionCount = useRunSelection((state) => state.selectedIds.length);

  useEffect(() => {
    void refreshAll();
  }, []);

  async function refreshAll() {
    await Promise.all([fetchHealth(), fetchRuns()]);
  }

  async function fetchRuns() {
    setIsLoadingRuns(true);
    setError(null);
    try {
      const data = await listRuns<BenchmarkRunListItem[]>();
      setRuns(data);
    } catch (err) {
      console.error("Failed to load runs", err);
      setError(err instanceof Error ? err.message : "Failed to load runs");
    } finally {
      setIsLoadingRuns(false);
    }
  }

  async function fetchHealth() {
    try {
      const data = await getHealth<HealthStatus>();
      setHealth(data);
    } catch (err) {
      console.error("Failed to load health status", err);
    } finally {
    }
  }

  const sidebarStats = useMemo(() => {
    if (!health) {
      return null;
    }

    return [
      { label: "Requires HTTPS", value: health.requiresHttps ? "Yes" : "No" },
      { label: "Requires mTLS", value: health.requiresMtls ? "Yes" : "No" },
      { label: "Requires JWT", value: health.requiresJwt ? "Yes" : "No" },
    ];
  }, [health]);

  return (
    <div className="flex min-h-screen bg-slate-100 text-slate-900">
      <aside className="flex w-64 flex-col border-r border-slate-200 bg-slate-950 text-slate-100">
        <div className="px-6 py-8">
          <p className="text-xs uppercase tracking-widest text-slate-400">SG Benchmark</p>
          <h1 className="mt-1 text-2xl font-semibold text-white">BenchRunner</h1>
          <p className="mt-6 text-sm text-slate-300">Security Profile</p>
          <p className="text-3xl font-semibold text-white">{health?.profile ?? "Unknown"}</p>
          <dl className="mt-6 space-y-2 text-sm text-slate-300">
            {sidebarStats?.map((entry) => (
              <div key={entry.label} className="flex items-center justify-between">
                <dt>{entry.label}</dt>
                <dd className="font-medium text-white">{entry.value}</dd>
              </div>
            ))}
          </dl>
          <div className="mt-8 rounded-xl border border-white/10 bg-white/5 px-4 py-3 text-sm">
            <p className="text-slate-300">Recorded Runs</p>
            <p className="text-2xl font-semibold text-white">{health?.runs ?? runs.length}</p>
          </div>
        </div>
        <nav className="mt-auto space-y-1 px-2 pb-4">
          {NAV_ITEMS.map((item) => (
            <button
              key={item.id}
              type="button"
              className={clsx(
                "flex w-full items-center justify-between rounded-lg px-4 py-3 text-left text-sm font-medium transition",
                activeTab === item.id ? "bg-white/10 text-white" : "text-slate-300 hover:bg-white/5 hover:text-white",
              )}
              onClick={() => setActiveTab(item.id)}
            >
              <span>{item.label}</span>
              {item.id === "compare" && selectionCount > 0 && (
                <span className="rounded-full bg-white/20 px-2 py-0.5 text-xs">{selectionCount}</span>
              )}
            </button>
          ))}
        </nav>
      </aside>

      <main className="flex-1 overflow-y-auto bg-slate-50 p-6 md:p-10">
        {error && (
          <div className="mb-4 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
            {error}
          </div>
        )}
        {activeTab === "run" && (
          <RunExperiments
            onRunCompleted={fetchRuns}
            setActiveTab={setActiveTab}
          />
        )}
        {activeTab === "history" && (
          <History
            runs={runs}
            isLoading={isLoadingRuns}
            onRefresh={fetchRuns}
            setActiveTab={setActiveTab}
          />
        )}
        {activeTab === "compare" && <Compare runs={runs} onNavigate={setActiveTab} />}
      </main>
    </div>
  );
}

export default App;
