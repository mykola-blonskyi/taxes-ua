import { describe, expect, it } from "vitest";
import type { Reserve } from "@/data/dashboard/useDashboard";
import { reply, renderApp, screen, stubFetch, waitFor } from "@/test/harness";
import { ReserveCard } from "./ReserveCard";

type Jar = NonNullable<Reserve["jar"]>;

const today = "2026-10-02";
const refresh = "POST /api/monobank/reserve-jar/refresh" as const;

// Amounts stay under a thousand hryvnias, so no grouping separator (which ICU spells differently) is involved.
const covering: Jar = {
  title: "Податки",
  balanceKop: 40_000,
  fetchedAt: "2026-10-02T08:30:00Z",
  stale: false,
  surplusKop: 0,
  shortfallKop: 0,
  topUpBy: null,
  topUpKop: 0,
  topUpDaysLeft: null,
};

function reserve(jar: Jar | null, canChooseJar = false): Reserve {
  return {
    totalKop: 65_000,
    dues: [
      {
        dueDate: "2026-10-19",
        status: "Upcoming",
        daysLeft: 17,
        singleTaxKop: 50_000,
        militaryLevyKop: 10_000,
        esvKop: 5_000,
        totalKop: 65_000,
      },
    ],
    jar,
    canChooseJar,
  };
}

function renderCard(value: Reserve, locale: "uk" | "ru" = "uk") {
  return renderApp(<ReserveCard reserve={value} today={today} limitCrossing={null} />, { locale });
}

describe("ReserveCard", () => {
  it("shows the total to set aside and the amount for each tax by its deadline", () => {
    stubFetch({});
    renderCard(reserve(null));

    expect(screen.getByRole("heading", { name: "Скільки відкласти на податки" })).toBeVisible();
    expect(screen.getByText("650,00 ₴")).toBeVisible();
    expect(screen.getByText(/^До 19 \D+$/)).toBeVisible();
    expect(screen.getByText("500,00 ₴")).toBeVisible();
    expect(screen.getByText("100,00 ₴")).toBeVisible();
    expect(screen.getByText("50,00 ₴")).toBeVisible();
  });

  it("says there is nothing to set aside when everything accrued is paid", () => {
    stubFetch({});
    renderCard({ ...reserve(null), totalKop: 0, dues: [] });

    expect(screen.getByText("Відкладати нічого: усе нараховане вже сплачено.")).toBeVisible();
  });

  it("offers to pick a jar only when the owner can, and says nothing otherwise", () => {
    stubFetch({});
    const { unmount } = renderCard(reserve(null, true));

    expect(screen.getByRole("link", { name: "Обрати скарбничку monobank, щоб порівняти її баланс із цією сумою" })).toHaveAttribute(
      "href",
      "/settings?tab=monobank",
    );

    unmount();
    renderCard(reserve(null, false));
    expect(screen.queryByRole("link", { name: /Обрати скарбничку/ })).not.toBeInTheDocument();
  });

  describe("the jar", () => {
    it("asks for a top-up with its date when the jar falls short", () => {
      stubFetch({});
      renderCard(reserve({ ...covering, balanceKop: 40_000, shortfallKop: 25_000, topUpBy: "2026-10-19", topUpKop: 25_000, topUpDaysLeft: 17 }));

      expect(screen.getByText("Скарбничка «Податки»")).toBeVisible();
      expect(screen.getByText("400,00 ₴")).toBeVisible();
      expect(screen.getByText(/^Поповніть на 250,00 ₴ до 19 \D+\.$/)).toBeVisible();
      expect(screen.queryByText(/Загалом не вистачає/)).not.toBeInTheDocument();
      expect(screen.queryByText(/Вистачає/)).not.toBeInTheDocument();
    });

    it("also gives the whole shortfall when the first top-up is only part of it", () => {
      stubFetch({});
      renderCard(reserve({ ...covering, shortfallKop: 60_000, topUpBy: "2026-10-19", topUpKop: 25_000 }));

      expect(screen.getByText(/^Поповніть на 250,00 ₴ до 19 /)).toBeVisible();
      expect(screen.getByText("Загалом не вистачає 600,00 ₴.")).toBeVisible();
    });

    it("says how much is left over when the jar covers the reserve", () => {
      stubFetch({});
      renderCard(reserve({ ...covering, surplusKop: 15_000 }));

      expect(screen.getByText("Вистачає із запасом 150,00 ₴.")).toBeVisible();
      expect(screen.queryByText(/Поповніть/)).not.toBeInTheDocument();
    });

    it("says the jar covers the reserve exactly", () => {
      stubFetch({});
      renderCard(reserve(covering));

      expect(screen.getByText("Вистачає рівно.")).toBeVisible();
    });

    it("shows when the balance was read, and warns when it is stale", () => {
      stubFetch({});
      const { unmount } = renderCard(reserve(covering));
      expect(screen.getByText(/^Станом на /)).toBeVisible();
      expect(screen.queryByText(/Баланс застарів/)).not.toBeInTheDocument();

      unmount();
      renderCard(reserve({ ...covering, stale: true }));
      expect(screen.getByText(/^Баланс застарів: востаннє оновлено /)).toBeVisible();
      expect(screen.queryByText(/^Станом на /)).not.toBeInTheDocument();
    });

    it("refreshes the balance from the bank when asked", async () => {
      const api = stubFetch({ [refresh]: {} });
      const { user } = renderCard(reserve(covering));

      await user.click(screen.getByRole("button", { name: "Оновити баланс" }));

      await waitFor(() => expect(api.requestsTo(refresh)).toHaveLength(1));
      expect(api.requests[0]).toMatchObject({ method: "POST", path: "/api/monobank/reserve-jar/refresh", body: undefined });
    });

    it.each([
      [429, "monobank_rate_limited", "monobank дозволяє один запит на хвилину. Спробуйте за хвилину."],
      [409, "monobank_token_rejected", "monobank відхилив токен, підключіть знову."],
      [409, "reserve_jar_not_offered", "monobank не підключено або такої скарбнички вже немає. Оберіть її ще раз."],
      [502, "monobank_unavailable", "monobank зараз недоступний."],
      [500, undefined, "Не вдалося виконати запит."],
    ])("explains a failed refresh with status %i", async (status, code, message) => {
      stubFetch({ [refresh]: reply(status, { title: "Failed", code }) });
      const { user } = renderCard(reserve(covering));

      await user.click(screen.getByRole("button", { name: "Оновити баланс" }));

      expect(await screen.findByText(message)).toBeVisible();
    });
  });

  describe("in Russian", () => {
    it("shows the reserve and a jar that falls short in Russian", () => {
      stubFetch({});
      renderCard(reserve({ ...covering, shortfallKop: 60_000, topUpBy: "2026-10-19", topUpKop: 25_000 }), "ru");

      expect(screen.getByRole("heading", { name: "Сколько отложить на налоги" })).toBeVisible();
      expect(screen.getByText("Копилка «Податки»")).toBeVisible();
      expect(screen.getByText(/^Пополните на 250,00 ₴ до 19 /)).toBeVisible();
      expect(screen.getByText("Всего не хватает 600,00 ₴.")).toBeVisible();
      expect(screen.getByRole("button", { name: "Обновить баланс" })).toBeVisible();
    });

    it("shows a surplus, a stale balance and the rejected token in Russian", async () => {
      stubFetch({ [refresh]: reply(409, { code: "monobank_token_rejected" }) });
      const { user } = renderCard(reserve({ ...covering, surplusKop: 15_000, stale: true }), "ru");

      expect(screen.getByText("Хватает с запасом 150,00 ₴.")).toBeVisible();
      expect(screen.getByText(/^Баланс устарел: последний раз обновлён /)).toBeVisible();

      await user.click(screen.getByRole("button", { name: "Обновить баланс" }));

      expect(await screen.findByText("monobank отклонил токен, подключите снова.")).toBeVisible();
    });
  });
});
