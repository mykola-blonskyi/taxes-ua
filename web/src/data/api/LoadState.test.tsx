import { describe, expect, it, vi } from "vitest";
import { renderApp, reply, screen, stubFetch, waitFor } from "@/test/harness";
import { ApiError } from "./client";
import { LoadState, type QueryStatus } from "./LoadState";
import { useDashboard } from "@/data/dashboard/useDashboard";

function query(overrides: Partial<QueryStatus> = {}): QueryStatus {
  return { isLoading: false, isError: false, error: null, isFetching: false, refetch: vi.fn(), ...overrides };
}

const locales = [
  { locale: "uk", loading: "Завантаження…", retry: "Спробувати ще раз", retrying: "Повторюємо…" },
  { locale: "ru", loading: "Загрузка…", retry: "Повторить", retrying: "Повторяем…" },
] as const;

describe.each(locales)("LoadState in $locale", ({ locale, loading, retry, retrying }) => {
  it("announces loading as a polite status with the default words", () => {
    renderApp(<LoadState query={query({ isLoading: true })} failed="Failed." />, { locale });

    expect(screen.getByRole("status")).toHaveTextContent(loading);
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("uses the screen's own loading words when it gives them", () => {
    renderApp(<LoadState query={query({ isLoading: true })} loading="Own words" failed="Failed." />, { locale });

    expect(screen.getByRole("status")).toHaveTextContent("Own words");
  });

  it("announces a failure as an alert and refetches on retry", async () => {
    const failed = query({ isError: true, error: new ApiError(500) });
    const { user } = renderApp(<LoadState query={failed} failed="Не вдалося завантажити." />, { locale });

    expect(screen.getByRole("alert")).toHaveTextContent("Не вдалося завантажити.");
    await user.click(screen.getByRole("button", { name: retry }));

    expect(failed.refetch).toHaveBeenCalledTimes(1);
  });

  it("disables the retry while it refetches", () => {
    renderApp(<LoadState query={query({ isError: true, isFetching: true })} failed="Failed." />, { locale });

    expect(screen.getByRole("button", { name: retrying })).toBeDisabled();
  });

  it("refetches only the queries that failed", async () => {
    const ok = query();
    const broken = query({ isError: true });
    const { user } = renderApp(<LoadState query={[ok, broken]} failed="Failed." />, { locale });

    await user.click(screen.getByRole("button", { name: retry }));

    expect(broken.refetch).toHaveBeenCalledTimes(1);
    expect(ok.refetch).not.toHaveBeenCalled();
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
    renderApp(<LoadState query={query({ isError: true, error: new TypeError("Failed to fetch") })} failed="Не вдалося завантажити." />);

    expect(screen.getByRole("alert").textContent).toContain("Не вдалося завантажити.");
    expect(screen.getByRole("alert")).not.toHaveTextContent("Не вдалося виконати дію");
  });
});

function DashboardProbe() {
  const dashboard = useDashboard();

  return dashboard.data ? <p>Loaded {dashboard.data.today}</p> : <LoadState query={dashboard} failed="Failed to load." />;
}

describe("LoadState with a real query", () => {
  it("loads again when retry is pressed after a failure", async () => {
    let calls = 0;
    stubFetch({
      "GET /api/dashboard": () => (++calls === 1 ? reply(500, { title: "Boom" }) : { today: "2026-10-02" }),
    });
    const { user } = renderApp(<DashboardProbe />);

    expect(await screen.findByRole("alert")).toHaveTextContent("Failed to load.");
    await user.click(screen.getByRole("button", { name: "Спробувати ще раз" }));

    await waitFor(() => expect(screen.getByText("Loaded 2026-10-02")).toBeInTheDocument());
    expect(calls).toBe(2);
  });
});
