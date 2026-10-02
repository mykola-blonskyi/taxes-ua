import { describe, expect, it } from "vitest";
import { renderApp, reply, screen, stubFetch, waitFor } from "@/test/harness";
import { TransactionForm } from "./TransactionForm";

const clients = "GET /api/clients" as const;
const receipts = "GET /api/transactions/receipts" as const;

const locales = [
  { locale: "uk", failed: /Не вдалося завантажити клієнтів і надходження/, retry: "Спробувати ще раз" },
  { locale: "ru", failed: /Не удалось загрузить клиентов и поступления/, retry: "Повторить" },
] as const;

describe.each(locales)("TransactionForm side lists in $locale", ({ locale, failed, retry }) => {
  it("says the clients and receipts could not load, and loads them again on retry", async () => {
    let calls = 0;
    stubFetch({
      [clients]: () => (++calls === 1 ? reply(500, { title: "Boom" }) : [{ id: "c1", name: "Acme", defaultCurrency: "UAH" }]),
      [receipts]: [],
    });
    const { user, container } = renderApp(
      <TransactionForm editing={null} onUpdated={() => undefined} onCancel={() => undefined} />,
      { locale },
    );

    expect(await screen.findByRole("alert")).toHaveTextContent(failed);

    await user.click(screen.getByRole("button", { name: retry }));

    await waitFor(() => expect(container.querySelector('datalist option[value="Acme"]')).not.toBeNull());
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("shows nothing extra while the lists load fine", async () => {
    stubFetch({ [clients]: [], [receipts]: [] });
    renderApp(<TransactionForm editing={null} onUpdated={() => undefined} onCancel={() => undefined} />, { locale });

    await screen.findByLabelText(/./, { selector: "#transaction-client-name" });

    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });
});
