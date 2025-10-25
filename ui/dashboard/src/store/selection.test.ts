import { beforeEach, describe, expect, it } from "vitest";
import { MAX_SELECTION, useRunSelection } from "./selection";

beforeEach(() => {
  useRunSelection.setState({ selectedIds: [] });
});

describe("run selection store", () => {
  it("adds unique runs up to the max limit", () => {
    const { select } = useRunSelection.getState();
    for (let index = 0; index < MAX_SELECTION + 2; index += 1) {
      select(`run-${index}`);
    }
    expect(useRunSelection.getState().selectedIds).toHaveLength(MAX_SELECTION);
  });

  it("toggle removes runs that are already selected", () => {
    const { toggle } = useRunSelection.getState();
    toggle("run-1");
    expect(useRunSelection.getState().selectedIds).toContain("run-1");
    toggle("run-1");
    expect(useRunSelection.getState().selectedIds).not.toContain("run-1");
  });

  it("clear removes every selection", () => {
    const { select, clear } = useRunSelection.getState();
    select("run-1");
    select("run-2");
    clear();
    expect(useRunSelection.getState().selectedIds).toEqual([]);
  });

  it("selectMany enforces the max limit and deduplicates entries", () => {
    const { selectMany } = useRunSelection.getState();
    selectMany(["run-1", "run-1", "run-2", "run-3", "run-4", "run-5", "run-6", "run-7"]);
    const ids = useRunSelection.getState().selectedIds;
    const unique = Array.from(new Set(ids));
    expect(ids).toEqual(unique);
    expect(ids.length).toBeLessThanOrEqual(MAX_SELECTION);
  });
});
