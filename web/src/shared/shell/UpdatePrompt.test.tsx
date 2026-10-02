import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, renderApp, screen } from "@/test/harness";
import { UpdatePrompt } from "./UpdatePrompt";

type WorkerEvent = { isUpdate?: boolean };
type Listener = (event: WorkerEvent) => void;

// The provider that registers the real worker is not under test; this stands in for its Serwist instance.
const worker = vi.hoisted(() => {
  const listeners = new Map<string, Set<Listener>>();

  return {
    listeners,
    update: vi.fn(() => Promise.resolve()),
    messageSkipWaiting: vi.fn(),
    addEventListener: (type: string, listener: Listener) => {
      listeners.set(type, (listeners.get(type) ?? new Set()).add(listener));
    },
    removeEventListener: (type: string, listener: Listener) => {
      listeners.get(type)?.delete(listener);
    },
    emit: (type: string, event: WorkerEvent = { isUpdate: true }) =>
      listeners.get(type)?.forEach((listener) => listener(event)),
  };
});

vi.mock("@serwist/next/react", () => ({ useSerwist: () => ({ serwist: worker }) }));

const reload = vi.fn();

beforeEach(() => {
  worker.listeners.clear();
  worker.update.mockClear();
  worker.messageSkipWaiting.mockClear();
  reload.mockClear();
  vi.spyOn(window, "location", "get").mockReturnValue({ ...window.location, reload });
});

describe("UpdatePrompt", () => {
  it("shows nothing until a new version is waiting", () => {
    renderApp(<UpdatePrompt />);

    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });

  it("offers the update when a new version is waiting and does not reload by itself", () => {
    renderApp(<UpdatePrompt />);

    act(() => worker.emit("waiting"));

    expect(screen.getByRole("status")).toHaveTextContent("Доступна нова версія застосунку");
    expect(worker.messageSkipWaiting).not.toHaveBeenCalled();
    expect(reload).not.toHaveBeenCalled();
  });

  it("activates the waiting version when the owner accepts, and reloads once it takes over", async () => {
    const { user } = renderApp(<UpdatePrompt />);
    act(() => worker.emit("waiting"));

    await user.click(screen.getByRole("button", { name: "Оновити" }));
    expect(worker.messageSkipWaiting).toHaveBeenCalledTimes(1);
    expect(reload).not.toHaveBeenCalled();

    act(() => worker.emit("controlling"));
    expect(reload).toHaveBeenCalledTimes(1);
  });

  it("prompts instead of reloading when another tab updated the worker", () => {
    renderApp(<UpdatePrompt />);

    act(() => worker.emit("controlling"));

    expect(reload).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "Оновити" })).toBeVisible();
  });

  it("stays quiet when the first install takes control of the page", () => {
    renderApp(<UpdatePrompt />);

    act(() => worker.emit("controlling", { isUpdate: false }));

    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });

  it("says so in Russian", () => {
    renderApp(<UpdatePrompt />, { locale: "ru" });

    act(() => worker.emit("waiting"));

    expect(screen.getByRole("status")).toHaveTextContent("Доступна новая версия приложения");
    expect(screen.getByRole("button", { name: "Обновить" })).toBeVisible();
  });

  it("checks for a new version when the tab comes back to the front", () => {
    renderApp(<UpdatePrompt />);

    document.dispatchEvent(new Event("visibilitychange"));

    expect(worker.update).toHaveBeenCalledTimes(1);
  });
});
