import { act } from "@testing-library/react";
import { vi } from "vitest";

type FakeTimerMethods = NonNullable<NonNullable<Parameters<typeof vi.useFakeTimers>[0]>["toFake"]>;

/**
 * Fake timers for a component test. Testing Library only knows how to wait under Jest's fake timers, and
 * hangs under Vitest's, so this also tells it how to move the clock. The clock moves only through
 * `advance`, which runs the timers and the promises they unblock inside `act`. Restored after each test.
 */
export function useFakeTimers(toFake: FakeTimerMethods = ["setTimeout", "clearTimeout", "Date"]) {
  vi.useFakeTimers({ toFake });
  vi.stubGlobal("jest", { advanceTimersByTime: (ms: number) => vi.advanceTimersByTime(ms) });

  return {
    advance: (ms: number) => act(() => vi.advanceTimersByTimeAsync(ms)),
    /** For `renderApp`'s third argument, so user-event's pauses move the fake clock. */
    userOptions: { advanceTimers: (ms: number) => vi.advanceTimersByTime(ms) },
  };
}

export function resetTimers() {
  vi.useRealTimers();
  Reflect.deleteProperty(globalThis, "jest");
}
