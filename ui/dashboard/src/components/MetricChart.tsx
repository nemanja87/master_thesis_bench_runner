import {
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  Legend,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
  type TooltipProps,
} from "recharts";
import type { RunMetrics } from "../types";

export interface ComparisonRun {
  id: string;
  label: string;
  color: string;
  metrics: RunMetrics;
}

type AxisScale = "linear" | "log";

interface LatencyBucket {
  id: "p50" | "p90" | "p99";
  label: string;
}

interface MetricChartProps {
  title: string;
  variant: "latency" | "bar";
  runs: ComparisonRun[];
  axisScale: AxisScale;
  latencyBuckets?: LatencyBucket[];
  metricAccessor?: (run: ComparisonRun) => number | undefined;
  metricLabel?: string;
  emptyMessage?: string;
}

function MetricChart({
  title,
  variant,
  runs,
  axisScale,
  latencyBuckets = [],
  metricAccessor,
  metricLabel = "value",
  emptyMessage = "No data available for this metric.",
}: MetricChartProps) {
  const sanitizedRuns = runs.filter((run) => !!run);

  if (sanitizedRuns.length === 0) {
    return null;
  }

  if (variant === "latency") {
    if (latencyBuckets.length === 0) {
      return null;
    }

    const data = latencyBuckets.map((bucket) => {
      const entry: Record<string, string | number | null> = { bucket: bucket.label };
      for (const run of sanitizedRuns) {
        const rawValue =
          bucket.id === "p50" ? run.metrics.latency.p50 : bucket.id === "p90" ? run.metrics.latency.p90 : run.metrics.latency.p99;
        entry[run.id] = sanitizeValue(rawValue, axisScale);
      }
      return entry;
    });

    const hasValues = data.some((entry) => sanitizedRuns.some((run) => entry[run.id] !== null));
    if (!hasValues) {
      return <EmptyMetric title={title} message={emptyMessage} />;
    }

    return (
      <ChartWrapper title={title}>
        <ResponsiveContainer width="100%" height={320}>
          <LineChart data={data}>
            <CartesianGrid strokeDasharray="4 4" stroke="#e2e8f0" />
            <XAxis dataKey="bucket" stroke="#94a3b8" />
            <YAxis
              stroke="#94a3b8"
              tickFormatter={(value) => (typeof value === "number" ? value.toFixed(0) : value)}
              scale={axisScale === "log" ? "log" : "auto"}
              domain={["auto", "auto"]}
            />
            <Tooltip content={<MetricTooltipContent unit="ms" />} />
            <Legend />
            {sanitizedRuns.map((run) => (
              <Line
                key={run.id}
                type="monotone"
                dataKey={run.id}
                name={`${run.label} (${run.id})`}
                stroke={run.color}
                strokeWidth={2}
                dot={{ r: 3 }}
              />
            ))}
          </LineChart>
        </ResponsiveContainer>
      </ChartWrapper>
    );
  }

  const data = sanitizedRuns.map((run) => ({
    runId: run.id,
    label: run.label,
    value: sanitizeValue(metricAccessor?.(run), axisScale),
    color: run.color,
  }));

  const hasValues = data.some((entry) => entry.value !== null);
  if (!hasValues) {
    return <EmptyMetric title={title} message={emptyMessage} />;
  }

  return (
    <ChartWrapper title={title}>
      <ResponsiveContainer width="100%" height={280}>
        <BarChart data={data}>
          <CartesianGrid strokeDasharray="4 4" stroke="#e2e8f0" />
          <XAxis dataKey="label" stroke="#94a3b8" />
          <YAxis
            stroke="#94a3b8"
            scale={axisScale === "log" ? "log" : "auto"}
            domain={["auto", "auto"]}
            tickFormatter={(value) => (typeof value === "number" ? value.toFixed(0) : value)}
          />
          <Tooltip content={<MetricTooltipContent unit={metricLabel} />} />
          <Bar dataKey="value" radius={[8, 8, 0, 0]}>
            {data.map((entry) => (
              <Cell key={entry.runId} fill={entry.color} />
            ))}
          </Bar>
        </BarChart>
      </ResponsiveContainer>
      <RunLegend runs={sanitizedRuns} />
    </ChartWrapper>
  );
}

function sanitizeValue(value: number | undefined, axisScale: AxisScale) {
  if (!Number.isFinite(value ?? NaN)) {
    return null;
  }
  if (axisScale === "log" && value !== undefined && value <= 0) {
    return null;
  }
  return value ?? null;
}

function ChartWrapper({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm" role="figure" aria-label={title}>
      <h3 className="text-lg font-semibold text-slate-900">{title}</h3>
      <div className="mt-4">{children}</div>
    </section>
  );
}

function MetricTooltipContent({ active, payload, label, unit }: TooltipProps<number, string> & { unit: string }) {
  if (!active || !payload?.length) {
    return null;
  }
  return (
    <div className="rounded-lg border border-slate-200 bg-white px-3 py-2 text-xs text-slate-700 shadow-lg">
      {label && <p className="font-semibold text-slate-900">{label}</p>}
      <ul className="mt-1 space-y-1">
        {payload.map((entry) => (
          <li key={String(entry.dataKey)} className="flex items-center gap-2">
            <span className="h-2 w-2 rounded-full" style={{ backgroundColor: entry.color }} />
            <span>
              {entry.name ?? entry.dataKey}: {typeof entry.value === "number" ? entry.value.toFixed(2) : entry.value}{" "}
              {unit === "ms" ? "ms" : ""}
            </span>
          </li>
        ))}
      </ul>
    </div>
  );
}

function RunLegend({ runs }: { runs: ComparisonRun[] }) {
  return (
    <div className="mt-4 flex flex-wrap gap-3">
      {runs.map((run) => (
        <div key={run.id} className="inline-flex items-center gap-2 rounded-full border border-slate-200 px-3 py-1 text-xs">
          <span className="h-2 w-2 rounded-full" style={{ backgroundColor: run.color }} />
          <span className="font-medium text-slate-700">
            {run.label} ({run.id})
          </span>
        </div>
      ))}
    </div>
  );
}

function EmptyMetric({ title, message }: { title: string; message: string }) {
  return (
    <section className="rounded-2xl border border-dashed border-slate-200 bg-white p-4 text-center text-sm text-slate-500">
      <h3 className="text-base font-semibold text-slate-900">{title}</h3>
      <p className="mt-2">{message}</p>
    </section>
  );
}

export default MetricChart;
