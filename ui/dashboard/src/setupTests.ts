import "@testing-library/jest-dom";

class ResizeObserverMock implements ResizeObserver {
  observe(): void {}
  unobserve(): void {}
  disconnect(): void {}
}

if (typeof window !== "undefined" && !("ResizeObserver" in window)) {
  // @ts-expect-error polyfill for jsdom
  window.ResizeObserver = ResizeObserverMock;
}
