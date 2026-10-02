import { describe, expect, it } from "vitest";
import type { DashboardResponse } from "@/data/dashboard/useDashboard";
import { renderApp, screen, stubFetch } from "@/test/harness";
import { Group3Status } from "./Group3Status";

type Group3 = DashboardResponse["group3"];

const today = "2026-10-02";

const unconfirmed: Group3 = {
  group3Start: "2026-09-28",
  confirmed: false,
  applicationDeadline: "2026-10-08",
  applicationDaysLeft: 6,
  beforeGroup3: null,
};
const confirmed: Group3 = { ...unconfirmed, confirmed: true, applicationDeadline: null, applicationDaysLeft: null };

// Amounts stay under a thousand hryvnias, so no grouping separator (which ICU spells differently) is involved.
const lateStart: Group3 = {
  ...confirmed,
  group3Start: "2026-10-01",
  beforeGroup3: { from: "2026-09-28", to: "2026-09-30", incomeKop: 50_000 },
};

function renderStatus(group3: Group3, locale: "uk" | "ru" = "uk") {
  stubFetch({});
  return renderApp(<Group3Status group3={group3} today={today} />, { locale });
}

describe("Group3Status", () => {
  it("says group 3 is unconfirmed, links to the checklist and counts down to the application deadline", () => {
    renderStatus(unconfirmed);

    expect(screen.getByRole("status")).toHaveTextContent(
      "Групу 3 ще не підтверджено в ДПС. Суми орієнтовні, доки ДПС не внесе вас до реєстру платників єдиного податку.",
    );
    expect(screen.getByRole("link", { name: "Перевірити статус" })).toHaveAttribute("href", "/settings?tab=dps");
    expect(screen.getByRole("status")).toHaveTextContent(/Заяву про обрання 3 групи подайте до 8 \D+ · Залишилося 6 днів/);
  });

  it("keeps the banner without a deadline once the application window is over", () => {
    renderStatus({ ...unconfirmed, applicationDeadline: null, applicationDaysLeft: null });

    expect(screen.getByRole("status")).toBeVisible();
    expect(screen.queryByText(/Заяву про обрання 3 групи подайте/)).not.toBeInTheDocument();
  });

  it("shows nothing once group 3 is confirmed", () => {
    const { container } = renderStatus(confirmed);

    expect(screen.queryByRole("status")).not.toBeInTheDocument();
    expect(container).toBeEmptyDOMElement();
  });

  it("shows nothing before a registration date is set", () => {
    const { container } = renderStatus({ ...unconfirmed, group3Start: null, applicationDeadline: null, applicationDaysLeft: null });

    expect(container).toBeEmptyDOMElement();
  });

  it("names the stretch on the general system and its income, even when group 3 is confirmed", () => {
    renderStatus(lateStart);

    expect(screen.queryByRole("status")).not.toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "До початку 3 групи" })).toBeVisible();
    const note = screen.getByRole("note");
    expect(note).toHaveTextContent(/^До початку 3 групиЗ 28 \D+ по 30 \D+ ви на загальній системі оподаткування й отримали за цей час 500,00 ₴\./);
    expect(note).toHaveTextContent("Податок на доходи фізичних осіб і військовий збір загальної системи застосунок не рахує");
    expect(note).toHaveTextContent("ЄСВ застосунок рахує");
  });

  it("reads the same in Russian", () => {
    renderStatus({ ...unconfirmed, beforeGroup3: lateStart.beforeGroup3 }, "ru");

    expect(screen.getByRole("status")).toHaveTextContent(
      "Группа 3 ещё не подтверждена в ДПС. Суммы ориентировочные, пока ДПС не внесёт вас в реестр плательщиков единого налога.",
    );
    expect(screen.getByRole("link", { name: "Проверить статус" })).toHaveAttribute("href", "/settings?tab=dps");
    expect(screen.getByRole("status")).toHaveTextContent(/Заявление о выборе 3 группы подайте до 8 \D+ · Осталось 6 дней/);
    expect(screen.getByRole("heading", { name: "До начала 3 группы" })).toBeVisible();
    expect(screen.getByRole("note")).toHaveTextContent("ЕСВ приложение считает");
  });
});
