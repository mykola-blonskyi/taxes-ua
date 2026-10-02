import { describe, expect, it } from "vitest";
import type { DashboardResponse } from "@/data/dashboard/useDashboard";
import { renderApp, screen, stubFetch } from "@/test/harness";
import { SyncHealth } from "./SyncHealth";

type Sync = NonNullable<DashboardResponse["sync"]>;

// 03:05 Kyiv on 2 October 2026; the year is spelled differently per locale, so the tests match the day.
const lastSyncedAt = "2026-10-02T00:05:00Z";

function renderSync(sync: Sync, locale: "uk" | "ru" = "uk") {
  stubFetch({});
  return renderApp(<SyncHealth sync={sync} />, { locale });
}

describe("SyncHealth", () => {
  it("says when the bank last synced and raises no alarm while healthy", () => {
    renderSync({ state: "Healthy", lastSyncedAt });

    expect(screen.getByText(/^Банк: останній успішний обмін 2 жовт\. 2026 р\., 03:05$/)).toBeVisible();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(screen.queryByRole("link")).not.toBeInTheDocument();
  });

  it("says the history is still loading while no account has caught up", () => {
    renderSync({ state: "Healthy", lastSyncedAt: null });

    expect(screen.getByText("Банк: історію ще завантажено не повністю")).toBeVisible();
  });

  it("flags stale data with the last success and links to the monobank settings", () => {
    renderSync({ state: "Stale", lastSyncedAt });

    const alert = screen.getByRole("alert");
    expect(alert).toHaveTextContent("Дані з банку застаріли");
    expect(alert).toHaveTextContent("Оновлень із банку немає з 2 жовт. 2026 р., 03:05.");
    expect(screen.getByRole("link", { name: "Відкрити налаштування monobank" })).toHaveAttribute(
      "href",
      "/settings?tab=monobank",
    );
  });

  it("flags a rejected token and links to the fix", () => {
    renderSync({ state: "TokenRejected", lastSyncedAt });

    expect(screen.getByRole("alert")).toHaveTextContent("monobank відхилив токен");
    expect(screen.getByRole("alert")).toHaveTextContent("Збережіть новий токен.");
    expect(screen.getByRole("link", { name: "Відкрити налаштування monobank" })).toHaveAttribute(
      "href",
      "/settings?tab=monobank",
    );
  });

  it("flags an unreadable token even before any account has caught up", () => {
    renderSync({ state: "TokenUnreadable", lastSyncedAt: null });

    expect(screen.getByRole("alert")).toHaveTextContent("Токен monobank не вдалося прочитати");
    expect(screen.getByRole("link", { name: "Відкрити налаштування monobank" })).toBeVisible();
  });

  it("reads the same in Russian", () => {
    const { unmount } = renderSync({ state: "Healthy", lastSyncedAt }, "ru");
    expect(screen.getByText(/^Банк: последний успешный обмен 2 окт\. 2026 г\., 03:05$/)).toBeVisible();
    unmount();

    renderSync({ state: "Stale", lastSyncedAt }, "ru");
    expect(screen.getByRole("alert")).toHaveTextContent("Данные из банка устарели");
    expect(screen.getByRole("alert")).toHaveTextContent("Обновлений из банка нет с 2 окт. 2026 г., 03:05.");
    expect(screen.getByRole("link", { name: "Открыть настройки monobank" })).toHaveAttribute(
      "href",
      "/settings?tab=monobank",
    );
  });
});
