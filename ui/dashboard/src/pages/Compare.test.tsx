import { render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import Compare from "./Compare";
import { useRunSelection } from "../store/selection";
import type { BenchmarkRunDetails, BenchmarkRunListItem } from "../types";
import { getRun } from "../lib/api";

vi.mock("../lib/api", () => ({
  getRun: vi.fn(),
}));

const mockedGetRun = vi.mocked(getRun);

function buildRun(overrides: Partial<BenchmarkRunListItem>): BenchmarkRunListItem {
  return {
    id: "run-x",
    startedAt: new Date().toISOString(),
    protocol: "grpc",
    securityProfile: "S2",
    callPath: "gateway",
    workload: "orders-create",
    rps: 50,
    p50Ms: 10,
    p95Ms: 15,
    p99Ms: 20,
    throughput: 45,
    errorRatePct: 1,
    ...overrides,
  };
}

describe("Compare screen", () => {
  beforeEach(() => {
    useRunSelection.setState({ selectedIds: [] });
    mockedGetRun.mockReset();
  });

  it("shows empty state when fewer than two runs are selected", () => {
    useRunSelection.setState({ selectedIds: ["alpha"] });
    render(<Compare runs={[]} onNavigate={vi.fn()} />);
    expect(screen.getByText(/Select 2–6 runs/i)).toBeInTheDocument();
  });

  it("renders latency and throughput charts when data is available", async () => {
    const runA = buildRun({ id: "run-a", workload: "checkout" });
    const runB = buildRun({ id: "run-b", workload: "catalog", p50Ms: 12, p95Ms: 18, p99Ms: 30, throughput: 38 });
    useRunSelection.setState({ selectedIds: [runA.id, runB.id] });

    mockedGetRun.mockResolvedValueOnce(runA as BenchmarkRunDetails);
    mockedGetRun.mockResolvedValueOnce(runB as BenchmarkRunDetails);

    render(<Compare runs={[runA, runB]} onNavigate={vi.fn()} />);

    await waitFor(() => expect(mockedGetRun).toHaveBeenCalledTimes(2));
    expect(await screen.findByText(/Latency Percentiles/i)).toBeInTheDocument();
    expect(screen.getByText(/Throughput \(requests\/sec\)/i)).toBeInTheDocument();
    expect(await screen.findByText(/Metric Details/i)).toBeInTheDocument();
  });
});
