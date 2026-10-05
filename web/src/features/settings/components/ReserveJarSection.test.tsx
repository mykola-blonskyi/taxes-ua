import { describe, expect, it } from "vitest";
import type { JarChoice, ReserveJar } from "@/data/monobank/useReserveJar";
import { act, reply, renderApp, screen, stubFetch, waitFor } from "@/test/harness";
import { ReserveJarSection } from "./ReserveJarSection";

const stored = "GET /api/monobank/reserve-jar" as const;
const jars = "GET /api/monobank/jars" as const;
const choose = "PUT /api/monobank/reserve-jar" as const;
const refresh = "POST /api/monobank/reserve-jar/refresh" as const;
const clear = "DELETE /api/monobank/reserve-jar" as const;

const jar: ReserveJar = { jarId: "jar-1", title: "Податки", balanceKop: 40_000, fetchedAt: "2026-10-02T08:30:00Z", stale: false };
const choices: JarChoice[] = [
  { id: "jar-1", title: "Податки", balanceKop: 40_000 },
  { id: "jar-2", title: "Відпустка", balanceKop: 90_000 },
];

const rejected = "monobank_token_rejected";

function renderSection(canRead: boolean, locale: "uk" | "ru" = "uk") {
  return renderApp(<ReserveJarSection canRead={canRead} />, { locale });
}

describe("ReserveJarSection", () => {
  it("shows nothing when there is no jar and monobank cannot be read", async () => {
    const api = stubFetch({ [stored]: { jar: null } });
    renderSection(false);

    await waitFor(() => expect(api.requestsTo(stored)).toHaveLength(1));
    expect(screen.queryByRole("heading", { name: "Скарбничка на податки" })).not.toBeInTheDocument();
  });

  it("says no jar is chosen and offers to list the owner's jars", async () => {
    stubFetch({ [stored]: { jar: null } });
    renderSection(true);

    expect(await screen.findByText("Скарбничку не обрано.")).toBeVisible();
    expect(screen.getByRole("button", { name: "Показати мої скарбнички" })).toBeVisible();
    expect(screen.queryByRole("button", { name: "Прибрати" })).not.toBeInTheDocument();
  });

  it("asks the bank only when the owner asks, then saves the jar picked", async () => {
    const api = stubFetch({
      [stored]: { jar: null },
      [jars]: { jars: choices, readAt: "2026-10-02T09:00:00Z" },
      [choose]: jar,
    });
    const { user } = renderSection(true);
    await user.click(await screen.findByRole("button", { name: "Показати мої скарбнички" }));
    expect(api.requestsTo(jars)).toHaveLength(1);

    const pick = await screen.findByRole("combobox", { name: "Скарбничка в гривнях" });
    expect(screen.getByRole("option", { name: /^Відпустка · 900,00\s₴$/ })).toBeVisible();
    expect(screen.getByRole("button", { name: "Обрати" })).toBeDisabled();
    await user.selectOptions(pick, "jar-1");
    await user.click(screen.getByRole("button", { name: "Обрати" }));

    expect(await screen.findByText("Податки: 400,00 ₴")).toBeVisible();
    expect(api.requestsTo(choose)).toHaveLength(1);
    expect(api.requests.find((request) => request.method === "PUT")?.body).toEqual({ jarId: "jar-1" });
    expect(screen.queryByRole("combobox", { name: "Скарбничка в гривнях" })).not.toBeInTheDocument();
  });

  it("says when monobank has no jar in hryvnias", async () => {
    stubFetch({ [stored]: { jar: null }, [jars]: { jars: [], readAt: "2026-10-02T09:00:00Z" } });
    const { user } = renderSection(true);

    await user.click(await screen.findByRole("button", { name: "Показати мої скарбнички" }));

    expect(await screen.findByText("У monobank немає скарбничок у гривнях.")).toBeVisible();
  });

  it("shows the stored jar, when its balance was read, and refreshes it", async () => {
    const api = stubFetch({ [stored]: { jar }, [refresh]: { ...jar, balanceKop: 45_000 } });
    const { user } = renderSection(true);

    expect(await screen.findByText("Податки: 400,00 ₴")).toBeVisible();
    expect(screen.getByText(/^Станом на /)).toBeVisible();
    await user.click(screen.getByRole("button", { name: "Оновити баланс" }));

    expect(await screen.findByText("Податки: 450,00 ₴")).toBeVisible();
    expect(api.requestsTo(refresh)).toHaveLength(1);
  });

  it("warns that a stale balance is old", async () => {
    stubFetch({ [stored]: { jar: { ...jar, stale: true } } });
    renderSection(true);

    expect(await screen.findByText(/^Баланс застарів: востаннє оновлено /)).toBeVisible();
    expect(screen.queryByText(/^Станом на /)).not.toBeInTheDocument();
  });

  it("lets the owner remove the stored jar, and the section goes away once monobank is disconnected", async () => {
    const api = stubFetch({ [stored]: { jar }, [clear]: reply(204) });
    const { user } = renderSection(false);

    expect(await screen.findByText("Податки: 400,00 ₴")).toBeVisible();
    expect(screen.queryByRole("button", { name: "Оновити баланс" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Змінити" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Показати мої скарбнички" })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Прибрати" }));

    await waitFor(() => expect(screen.queryByRole("heading", { name: "Скарбничка на податки" })).not.toBeInTheDocument());
    expect(api.requestsTo(clear)).toHaveLength(1);
    expect(api.requests.map((request) => request.path)).not.toContain("/api/monobank/jars");
  });

  it("keeps the remove button when the bank rejected the token and says to connect again", async () => {
    stubFetch({ [stored]: { jar }, [refresh]: reply(409, { code: rejected }) });
    const { user } = renderSection(true);

    await user.click(await screen.findByRole("button", { name: "Оновити баланс" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("monobank відхилив токен, підключіть знову.");
    expect(screen.getByRole("button", { name: "Прибрати" })).toBeVisible();
  });

  it("gives the rejected token its own message when listing jars, apart from a missing jar", async () => {
    stubFetch({ [stored]: { jar: null }, [jars]: reply(409, { code: rejected }) });
    const { user } = renderSection(true);

    await user.click(await screen.findByRole("button", { name: "Показати мої скарбнички" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("monobank відхилив токен, підключіть знову.");
    expect(screen.queryByText(/такої скарбнички вже немає/)).not.toBeInTheDocument();
  });

  it("asks to choose again when the jar is gone, and to wait when the bank limits calls", async () => {
    stubFetch({ [stored]: { jar }, [refresh]: reply(409, { code: "reserve_jar_not_offered" }) });
    const { user } = renderSection(true);

    await user.click(await screen.findByRole("button", { name: "Оновити баланс" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("monobank не підключено або такої скарбнички вже немає. Оберіть її ще раз.");
  });

  it("asks to wait a minute after a 429", async () => {
    stubFetch({ [stored]: { jar: null }, [jars]: reply(429, { code: "monobank_rate_limited" }, { "retry-after": "60" }) });
    const { user } = renderSection(true);

    await user.click(await screen.findByRole("button", { name: "Показати мої скарбнички" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("monobank дозволяє один запит на хвилину. Спробуйте за хвилину.");
  });

  it("shows the stored jar and the rejected token in Russian", async () => {
    stubFetch({ [stored]: { jar }, [refresh]: reply(409, { code: rejected }) });
    const { user } = renderSection(true, "ru");

    expect(await screen.findByText("Податки: 400,00 ₴")).toBeVisible();
    expect(screen.getByRole("heading", { name: "Копилка на налоги" })).toBeVisible();
    expect(screen.getByRole("button", { name: "Убрать" })).toBeVisible();
    await user.click(screen.getByRole("button", { name: "Обновить баланс" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("monobank отклонил токен, подключите снова.");
  });

  it.each([
    ["uk", "Не вдалося завантажити скарбничку.", "Скарбничку не обрано.", "Спробувати ще раз"],
    ["ru", "Не удалось загрузить копилку.", "Копилка не выбрана.", "Повторить"],
  ] as const)(
    "does not read a failed load as no jar chosen, and retries, in %s",
    async (locale, failedText, noJarText, retry) => {
      let calls = 0;
      stubFetch({ [stored]: () => (++calls === 1 ? reply(500, { title: "Boom" }) : { jar }) });
      const { user } = renderSection(true, locale);

      expect(await screen.findByText(new RegExp(failedText))).toBeVisible();
      expect(screen.queryByText(noJarText)).not.toBeInTheDocument();

      await user.click(screen.getByRole("button", { name: retry }));

      expect(await screen.findByText("Податки: 400,00 ₴")).toBeVisible();
      expect(calls).toBe(2);
    },
  );

  it("shows the failure even when monobank cannot be read, so a stored jar is not hidden", async () => {
    stubFetch({ [stored]: reply(500, { title: "Boom" }) });
    renderSection(false);

    expect(await screen.findByText(/Не вдалося завантажити скарбничку/)).toBeVisible();
  });

  it("lists jars in Russian", async () => {
    stubFetch({ [stored]: { jar: null }, [jars]: { jars: choices, readAt: "2026-10-02T09:00:00Z" } });
    const { user } = renderSection(true, "ru");

    await user.click(await screen.findByRole("button", { name: "Показать мои копилки" }));

    expect(await screen.findByRole("combobox", { name: "Копилка в гривнах" })).toBeVisible();
    expect(screen.getByRole("button", { name: "Выбрать" })).toBeVisible();
  });

  it("keeps saying no jar is chosen when a background refetch of the stored jar fails", async () => {
    let calls = 0;
    stubFetch({ [stored]: () => (++calls === 1 ? { jar: null } : reply(500, { title: "Boom" })) });
    const { queryClient } = renderSection(true);
    expect(await screen.findByText("Скарбничку не обрано.")).toBeVisible();

    await act(() => queryClient.invalidateQueries());
    await waitFor(() => expect(queryClient.isFetching()).toBe(0));

    expect(calls).toBe(2);
    expect(screen.getByText("Скарбничку не обрано.")).toBeVisible();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});
