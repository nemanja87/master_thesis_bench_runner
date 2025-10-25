import type { FormEvent } from "react";
import { useEffect, useMemo, useState } from "react";
import RunSummaryCard from "../components/RunSummaryCard";
import { getRun, startBench } from "../lib/api";
import { toRunMetrics, toRunSummary } from "../lib/runMappers";
import { useRunSelection } from "../store/selection";
import type {
  BenchRunRequest,
  BenchRunResponse,
  BenchmarkRunDetails,
  RunMetrics,
  RunSummary,
  SecurityProfile,
  TabKey,
} from "../types";

interface RunExperimentsProps {
  onRunCompleted: () => Promise<void> | void;
  setActiveTab: (tab: TabKey) => void;
}

const DEFAULT_REQUEST: BenchRunRequest = {
  protocol: "grpc",
  security: "S2",
  workload: "orders-create",
  rps: 10,
  duration: 60,
  warmup: 10,
  connections: 8,
};

const SECURITY_OPTIONS: Array<{ value: SecurityProfile; label: string; description: string }> = [
  { value: "S0", label: "S0 – HTTP", description: "Plain HTTP" },
  { value: "S1", label: "S1 – TLS", description: "TLS required" },
  { value: "S2", label: "S2 – TLS + JWT", description: "TLS and bearer token" },
  { value: "S3", label: "S3 – mTLS", description: "Mutual TLS" },
  { value: "S4", label: "S4 – mTLS + JWT", description: "Full mutual TLS with JWT" },
];

const WORKLOAD_PRESETS = ["orders-create", "orders-update", "orders-cancel", "inventory-sync", "payments-capture"];

function RunExperiments({ onRunCompleted, setActiveTab }: RunExperimentsProps) {
  const [formState, setFormState] = useState<BenchRunRequest>(DEFAULT_REQUEST);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [runStartTime, setRunStartTime] = useState<number | null>(null);
  const [elapsedSeconds, setElapsedSeconds] = useState(0);
  const [statusMessage, setStatusMessage] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [latestSummary, setLatestSummary] = useState<RunSummary | null>(null);
  const [latestMetrics, setLatestMetrics] = useState<RunMetrics | null>(null);
  const addToSelection = useRunSelection((state) => state.selectMany);
  useEffect(() => {
    if (!isSubmitting || !runStartTime) {
      return;
    }
    const timer = window.setInterval(() => {
      setElapsedSeconds(Math.floor((Date.now() - runStartTime) / 1000));
    }, 1000);
    return () => window.clearInterval(timer);
  }, [isSubmitting, runStartTime]);

  const workloadOptions = useMemo(() => WORKLOAD_PRESETS, []);
  const isFormValid =
    formState.workload.trim().length > 0 &&
    formState.rps > 0 &&
    formState.duration > 0 &&
    formState.connections > 0 &&
    formState.warmup >= 0;

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (isSubmitting || !isFormValid) {
      return;
    }
    setIsSubmitting(true);
    setErrorMessage(null);
    setStatusMessage("Triggering BenchRunner…");
    setLatestSummary(null);
    setLatestMetrics(null);
    setRunStartTime(Date.now());
    setElapsedSeconds(0);
    try {
      const response = await startBench<BenchRunResponse>(formState);
      if (!response?.id) {
        throw new Error("BenchRunner did not return a Run ID.");
      }
      setStatusMessage("Collecting results…");
      const details = await getRun<BenchmarkRunDetails>(response.id);
      const summary = toRunSummary(details);
      const metrics = toRunMetrics(details);
      setLatestSummary(summary);
      setLatestMetrics(metrics);
      setStatusMessage("Run complete.");
      await onRunCompleted?.();
    } catch (error) {
      console.error(error);
      setErrorMessage(error instanceof Error ? error.message : "Benchmark failed.");
    } finally {
      setIsSubmitting(false);
      setRunStartTime(null);
    }
  }

  function handleCompareShortcut() {
    if (!latestSummary) {
      return;
    }
    addToSelection([latestSummary.id]);
    setActiveTab("compare");
  }

  return (
    <>
      <div className="space-y-6">
        <header className="flex flex-col gap-2">
          <p className="text-sm font-semibold uppercase tracking-wide text-slate-500">Screen A</p>
          <h2 className="text-3xl font-semibold text-slate-900">Run Experiments</h2>
          <p className="text-base text-slate-600">
            Configure a BenchRunner workload and trigger a new experiment. The controls below match the original flow
            to keep existing automation intact.
          </p>
        </header>

        <section className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm">
          <form className="space-y-6" onSubmit={handleSubmit}>
            <div className="grid gap-5 md:grid-cols-2">
              <label className="flex flex-col gap-2 text-sm font-medium text-slate-700">
                Protocol
                <select
                  className="rounded-xl border border-slate-300 px-3 py-2 text-base font-normal text-slate-900 focus:border-slate-500 focus:outline-none focus:ring-2 focus:ring-slate-200"
                  value={formState.protocol}
                  onChange={(event) =>
                    setFormState((current) => ({ ...current, protocol: event.target.value as BenchRunRequest["protocol"] }))
                  }
                >
                  <option value="rest">REST</option>
                  <option value="grpc">gRPC</option>
                </select>
              </label>

              <label className="flex flex-col gap-2 text-sm font-medium text-slate-700">
                Security Mode
                <select
                  className="rounded-xl border border-slate-300 px-3 py-2 text-base font-normal text-slate-900 focus:border-slate-500 focus:outline-none focus:ring-2 focus:ring-slate-200"
                  value={formState.security}
                  onChange={(event) =>
                    setFormState((current) => ({
                      ...current,
                      security: event.target.value as SecurityProfile,
                    }))
                  }
                >
                  {SECURITY_OPTIONS.map((option) => (
                    <option key={option.value} value={option.value}>
                      {option.label}
                    </option>
                  ))}
                </select>
                <span className="text-xs font-normal text-slate-500">
                  {SECURITY_OPTIONS.find((option) => option.value === formState.security)?.description}
                </span>
              </label>

              <label className="flex flex-col gap-2 text-sm font-medium text-slate-700 md:col-span-2">
                Workload
                <input
                  className="rounded-xl border border-slate-300 px-3 py-2 text-base font-normal text-slate-900 focus:border-slate-500 focus:outline-none focus:ring-2 focus:ring-slate-200"
                  type="text"
                  value={formState.workload}
                  onChange={(event) =>
                    setFormState((current) => ({ ...current, workload: event.target.value }))
                  }
                  list="workload-presets"
                  placeholder="orders-create"
                  required
                />
                <datalist id="workload-presets">
                  {workloadOptions.map((workload) => (
                    <option key={workload} value={workload} />
                  ))}
                </datalist>
              </label>
            </div>

            <div className="grid gap-5 md:grid-cols-3">
              <NumberField
                label="Requested RPS"
                value={formState.rps}
                min={1}
                onChange={(value) => setFormState((current) => ({ ...current, rps: value }))}
              />
              <NumberField
                label="Duration (sec)"
                value={formState.duration}
                min={1}
                onChange={(value) => setFormState((current) => ({ ...current, duration: value }))}
              />
              <NumberField
                label="Warmup (sec)"
                value={formState.warmup}
                min={0}
                onChange={(value) => setFormState((current) => ({ ...current, warmup: value }))}
              />
              <NumberField
                label="Connections"
                value={formState.connections}
                min={1}
                onChange={(value) => setFormState((current) => ({ ...current, connections: value }))}
                helper="Concurrent connections per pod"
              />
            </div>

            {statusMessage && (
              <div className="rounded-xl border border-slate-200 bg-slate-50 px-4 py-3 text-sm text-slate-700">
                {statusMessage}
              </div>
            )}
            {errorMessage && (
              <div className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">{errorMessage}</div>
            )}

            <div className="flex flex-wrap gap-3">
              <button
                type="submit"
                disabled={!isFormValid || isSubmitting}
                className="inline-flex items-center justify-center rounded-xl bg-slate-900 px-5 py-3 text-base font-semibold text-white transition hover:bg-slate-800 disabled:cursor-not-allowed disabled:bg-slate-300"
              >
                {isSubmitting ? `Running… (${elapsedSeconds}s)` : "Trigger BenchRunner"}
              </button>
              <button
                type="button"
                onClick={() => setFormState({ ...DEFAULT_REQUEST })}
                className="inline-flex items-center justify-center rounded-xl border border-slate-300 px-5 py-3 text-base font-semibold text-slate-700 transition hover:border-slate-400 hover:bg-slate-50"
              >
                Reset to defaults
              </button>
            </div>
          </form>
        </section>
      </div>

      {latestSummary && (
        <div className="fixed bottom-6 right-6 z-40">
          <RunSummaryCard
            summary={latestSummary}
            metrics={latestMetrics}
            onClose={() => setLatestSummary(null)}
            onViewHistory={() => setActiveTab("history")}
            onCompare={handleCompareShortcut}
          />
        </div>
      )}
    </>
  );
}

interface NumberFieldProps {
  label: string;
  value: number;
  min: number;
  onChange: (value: number) => void;
  helper?: string;
}

function NumberField({ label, value, min, onChange, helper }: NumberFieldProps) {
  return (
    <label className="flex flex-col gap-2 text-sm font-medium text-slate-700">
      {label}
      <input
        type="number"
        min={min}
        value={value}
        onChange={(event) => onChange(Number(event.target.value))}
        className="rounded-xl border border-slate-300 px-3 py-2 text-base font-normal text-slate-900 focus:border-slate-500 focus:outline-none focus:ring-2 focus:ring-slate-200"
        required
      />
      {helper && <span className="text-xs font-normal text-slate-500">{helper}</span>}
    </label>
  );
}

export default RunExperiments;
