import { describe, expect, it } from "vitest";
import { renderApp, reply, screen, stubFetch } from "@/test/harness";
import { DraftEditor } from "./DraftEditor";

const clients = "GET /api/clients" as const;

const locales = [
  { locale: "uk", failed: /Не вдалося завантажити клієнтів/, retry: "Спробувати ще раз", noClients: /Спершу додайте клієнта/ },
  { locale: "ru", failed: /Не удалось загрузить клиентов/, retry: "Повторить", noClients: /Сначала добавьте клиента/ },
] as const;

describe.each(locales)("DraftEditor client list in $locale", ({ locale, failed, retry, noClients }) => {
  it("says the clients could not load, instead of staying silent, and loads them again on retry", async () => {
    let calls = 0;
    stubFetch({
      [clients]: () => (++calls === 1 ? reply(500, { title: "Boom" }) : [{ id: "c1", name: "Acme", defaultCurrency: "UAH" }]),
    });
    const { user } = renderApp(<DraftEditor onBack={() => undefined} onOpen={() => undefined} />, { locale });

    expect(await screen.findByRole("alert")).toHaveTextContent(failed);
    expect(screen.queryByText(noClients)).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: retry }));

    expect(await screen.findByRole("option", { name: "Acme" })).toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});
