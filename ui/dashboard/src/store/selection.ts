import { create } from "zustand";

const MAX_SELECTION = 6;

export interface RunSelectionState {
  selectedIds: string[];
  select: (id: string) => void;
  selectMany: (ids: string[]) => void;
  remove: (id: string) => void;
  toggle: (id: string) => void;
  clear: () => void;
  setSelection: (ids: string[]) => void;
  isSelected: (id: string) => boolean;
}

export const useRunSelection = create<RunSelectionState>((set, get) => ({
  selectedIds: [],
  select: (id) => {
    const { selectedIds } = get();
    if (selectedIds.includes(id) || selectedIds.length >= MAX_SELECTION) {
      return;
    }
    set({ selectedIds: [...selectedIds, id] });
  },
  selectMany: (ids) => {
    const deduped = Array.from(new Set(ids));
    const next = [...get().selectedIds];
    for (const id of deduped) {
      if (next.length >= MAX_SELECTION) {
        break;
      }
      if (!next.includes(id)) {
        next.push(id);
      }
    }
    set({ selectedIds: next });
  },
  remove: (id) => {
    set({ selectedIds: get().selectedIds.filter((entry) => entry !== id) });
  },
  toggle: (id) => {
    const { selectedIds, select, remove } = get();
    if (selectedIds.includes(id)) {
      remove(id);
    } else {
      select(id);
    }
  },
  clear: () => set({ selectedIds: [] }),
  setSelection: (ids) => {
    const deduped = Array.from(new Set(ids));
    set({ selectedIds: deduped.slice(0, MAX_SELECTION) });
  },
  isSelected: (id) => get().selectedIds.includes(id),
}));

export { MAX_SELECTION };
