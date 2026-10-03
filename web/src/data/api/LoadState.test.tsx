import { describe, expect, it, vi } from "vitest";
import { useDashboard } from "@/data/dashboard/useDashboard";
import { renderApp, reply, screen, stubFetch, waitFor } from "@/test/harness";
import { ApiError } from "./client";
import { LoadState, type QueryStatus } from "./LoadState";

function query(overrides: Partial<QueryStatus> = {}): QueryStatus {
  return { isLoading: false, isError: false, error: null, isFetching: false, refetch: vi.fn(), ...overrides };
}

const locales = [
  { locale: "uk", loading: "Завантаження…", offline: /Немає мережі/, retry: "Спробувати ще раз", retrying: "Повторюємо…" },
  { locale: "ru", loading: "Загрузка…", offline: /Нет сети/, retry: "Повторить", retrying: "Повторяем…" },
] as const;

describe.each(locales)("LoadState in $locale", ({ locale, loading, offline, retry, retrying }) => {
  it("announces loading as a polite status with the default words", () => {
    renderApp(<LoadState query={query({ isLoading: true })} failed="Failed." />, { locale });

    expect(screen.getByRole("status")).toHaveTextContent(loading);
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("uses the screen's own loading words when it gives them", () => {
    renderApp(<LoadState query={query({ isLoading: true })} loading="Own words" failed="Failed." />, { locale });

    expect(screen.getByRole("status")).toHaveTextContent("Own words");
  });

  it("says it is offline, with no retry that would do nothing, when the query waits for the network", () => {
    renderApp(<LoadState query={query({ isLoading: true, isPaused: true })} failed="Failed." />, { locale });

    expect(screen.getByRole("status")).toHaveTextContent(offline);
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });

  it("announces a failure as an alert and refetches on retry", async () => {
    const failed = query({ isError: true, error: new ApiError(500) });
    const { user } = renderApp(<LoadState query={failed} failed="Не вдалося завантажити." />, { locale });

    expect(screen.getByRole("alert")).toHaveTextContent("Не вдалося завантажити.");
    await user.click(screen.getByRole("button", { name: retry }));

    expect(failed.refetch).toHaveBeenCalledTimes(1);
  });

  it("keeps the alert and the button, busy and once only, while the refetch runs", async () => {
    const failed = query({ isError: true, refetch: vi.fn(() => new Promise(() => undefined)) });
    const { user } = renderApp(<LoadState query={failed} failed="Failed." />, { locale });

    await user.click(screen.getByRole("button", { name: retry }));
    const busy = screen.getByRole("button", { name: retrying });
    await user.click(busy);

    expect(busy).toHaveAttribute("aria-disabled", "true");
    expect(busy).not.toBeDisabled();
    expect(failed.refetch).toHaveBeenCalledTimes(1);
  });

  it("offers the failure and a retry, not an endless loading line, when nothing is pending and data is missing", async () => {
    const idle = query();
    const { user } = renderApp(<LoadState query={idle} failed="Missing." />, { locale });

    expect(screen.queryByRole("status")).not.toBeInTheDocument();
    expect(screen.getByRole("alert")).toHaveTextContent("Missing.");
    await user.click(screen.getByRole("button", { name: retry }));

    expect(idle.refetch).toHaveBeenCalledTimes(1);
  });

  it("still reads as loading while a query is fetching", () => {
    renderApp(<LoadState query={query({ isFetching: true })} failed="Missing." />, { locale });

    expect(screen.getByRole("status")).toHaveTextContent(loading);
  });

  it("refetches only the queries that failed", async () => {
    const ok = query();
    const broken = query({ isError: true });
    const { user } = renderApp(<LoadState query={[ok, broken]} failed="Failed." />, { locale });

    await user.click(screen.getByRole("button", { name: retry }));

    expect(broken.refetch).toHaveBeenCalledTimes(1);
    expect(ok.refetch).not.toHaveBeenCalled();
  });

  it("ties the retry button to the failure text it belongs to", () => {
    renderApp(<LoadState query={query({ isError: true })} failed="Failure text." />, { locale });

    const button = screen.getByRole("button", { name: retry });

    expect(button).toHaveAccessibleDescription("Failure text.");
  });
});

describe("LoadState resetKey", () => {
  const failedQuery = query({ isError: true, error: new ApiError(500) });
  const loadingQuery = query({ isLoading: true, isFetching: true });

  it("reads a new load after an earlier failure as loading, not as a retry, when the key changes", () => {
    const { rerender } = renderApp(<LoadState query={failedQuery} resetKey={2026} failed="Failed." />);
    expect(screen.getByRole("alert")).toBeInTheDocument();

    rerender(<LoadState query={loadingQuery} resetKey={2025} failed="Failed." />);

    expect(screen.getByRole("status")).toHaveTextContent("Завантаження…");
    expect(screen.queryByRole("button", { name: "Повторюємо…" })).not.toBeInTheDocument();
  });

  it("keeps showing the failure as a retry under the same key", () => {
    const { rerender } = renderApp(<LoadState query={failedQuery} resetKey={2026} failed="Failed." />);

    rerender(<LoadState query={loadingQuery} resetKey={2026} failed="Failed." />);

    expect(screen.getByRole("button", { name: "Повторюємо…" })).toBeInTheDocument();
  });
});

describe("LoadState quiet", () => {
  it("shows nothing while a side query loads or waits for the network", () => {
    const { container, rerender } = renderApp(<LoadState quiet query={query({ isLoading: true })} failed="Failed." />);

    expect(container).toBeEmptyDOMElement();

    rerender(<LoadState quiet query={query({ isLoading: true, isPaused: true })} failed="Failed." />);

    expect(container).toBeEmptyDOMElement();
  });

  it("shows the failure and its retry when the side query fails", () => {
    renderApp(<LoadState quiet query={query({ isError: true })} failed="Side failed." />);

    expect(screen.getByRole("alert")).toHaveTextContent("Side failed.");
    expect(screen.getByRole("button", { name: "Спробувати ще раз" })).toBeInTheDocument();
  });
});

describe("LoadState failure reason", () => {
  it("adds the api's coded reason in the owner's language", () => {
    const error = new ApiError(413, { code: "payload_too_large", message: "English only for logs." });
    renderApp(<LoadState query={query({ isError: true, error })} failed="Не вдалося завантажити." />);

    expect(screen.getByRole("alert")).toHaveTextContent("Не вдалося завантажити. Файл завеликий.");
    expect(screen.queryByText(/English only/)).not.toBeInTheDocument();
  });

  it("adds the reason in Russian", () => {
    const error = new ApiError(413, { code: "payload_too_large" });
    renderApp(<LoadState query={query({ isError: true, error })} failed="Не удалось загрузить." />, { locale: "ru" });

    expect(screen.getByRole("alert")).toHaveTextContent("Не удалось загрузить. Файл слишком большой.");
  });

  it("says nothing more for a failure without a code", () => {
    renderApp(
      <LoadState query={query({ isError: true, error: new TypeError("Failed to fetch") })} failed="Не вдалося завантажити." />,
    );

    expect(screen.getByRole("alert").textContent).toContain("Не вдалося завантажити.");
    expect(screen.getByRole("alert")).not.toHaveTextContent("Не вдалося виконати дію");
  });
});

function DashboardProbe() {
  const dashboard = useDashboard();

  return dashboard.data ? <p>Loaded {dashboard.data.today}</p> : <LoadState query={dashboard} failed="Failed to load." />;
}

describe.each(locales)("LoadState with a real query in $locale", ({ locale, retry, retrying }) => {
  it("loads again when retry is pressed, and keeps focus on the button while it does", async () => {
    let calls = 0;
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    stubFetch({
      "GET /api/dashboard": async () => {
        if (++calls === 1) {
          return reply(500, { title: "Boom" });
        }
        await gate;

        return { today: "2026-10-02" };
      },
    });
    const { user } = renderApp(<DashboardProbe />, { locale });

    const button = await screen.findByRole("button", { name: retry });
    await user.click(button);

    const busy = await screen.findByRole("button", { name: retrying });
    expect(busy).toBe(button);
    expect(busy).toHaveFocus();
    expect(screen.getByRole("alert")).toBeInTheDocument();

    release();

    await waitFor(() => expect(screen.getByText("Loaded 2026-10-02")).toBeInTheDocument());
    expect(calls).toBe(2);
  });

  it("shows the failure again, with the button ready, when the retry fails too", async () => {
    stubFetch({ "GET /api/dashboard": reply(500, { title: "Boom" }) });
    const { user } = renderApp(<DashboardProbe />, { locale });

    await user.click(await screen.findByRole("button", { name: retry }));

    await waitFor(() => expect(screen.getByRole("button", { name: retry })).not.toHaveAttribute("aria-disabled", "true"));
    expect(screen.getByRole("alert")).toBeInTheDocument();
  });
});
