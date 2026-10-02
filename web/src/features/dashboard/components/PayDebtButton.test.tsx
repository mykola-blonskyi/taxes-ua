import { describe, expect, it } from "vitest";
import type { KindDebt } from "@/data/dashboard/useDashboard";
import type { PaymentDetails } from "@/data/payments/usePaymentDetails";
import { act, reply, renderApp, screen, stubFetch, useFakeTimers, waitFor } from "@/test/harness";
import { PayDebtButton } from "./PayDebtButton";

const debt: KindDebt = {
  kind: "SingleTax",
  fromYear: 2026,
  fromQuarter: 2,
  toYear: 2026,
  toQuarter: 2,
  amountKop: 123_456,
  dueDate: "2026-08-19",
  status: "Due",
  daysLeft: 12,
  advanceMonth: null,
};

const recipient: NonNullable<PaymentDetails["recipient"]> = { iban: "UA213223130000026007233566001", name: "ГУК у м.Києві/Київ", code: "37993783", source: "Manual" };

// The api answers without a QR until it is asked for an amount, as the real endpoint does.
function paymentDetails(request: { query: Record<string, string> }): PaymentDetails {
  const amountKop = request.query.amountKop ? Number(request.query.amountKop) : null;

  return {
    kind: "SingleTax",
    periodYear: 2026,
    periodQuarter: 2,
    periodMonth: null,
    amountKop,
    purpose: "*;101;1234567890;Єдиний податок;",
    recipient,
    missing: [],
    qrContent: amountKop === null ? null : `https://qr.bank.gov.ua/${amountKop}`,
  };
}

describe("PayDebtButton", () => {
  it("asks for the details of the debt's period once opened, then for the amount on screen", async () => {
    const api = stubFetch({ "GET /api/payment-details": paymentDetails });
    const { user } = renderApp(<PayDebtButton debt={debt} />);

    // A request would start in an effect, so let effects and promises run before saying none was sent.
    await act(() => new Promise((resolve) => setTimeout(resolve, 20)));
    expect(api.requests).toHaveLength(0);

    await user.click(screen.getByRole("button", { name: "Сплатити" }));

    expect(await screen.findByRole("img", { name: "QR-код для оплати" })).toBeVisible();
    // The catalog glues "кв." to the year with a no-break space, which the test does not care about.
    expect(screen.getByRole("dialog", { name: (name) => name.replace(/\s/g, " ") === "Єдиний податок, 2 кв. 2026" })).toBeVisible();
    expect(screen.getByLabelText("Сума, ₴")).toHaveValue("1234.56");
    expect(api.requests.map((request) => request.query)).toEqual([
      { kind: "SingleTax", periodYear: "2026", periodQuarter: "2" },
      { kind: "SingleTax", periodYear: "2026", periodQuarter: "2", amountKop: "123456" },
    ]);
  });

  it("keeps the old code off the screen while a typed amount is awaited, then asks once for it", async () => {
    const timers = useFakeTimers();
    const api = stubFetch({ "GET /api/payment-details": paymentDetails });
    const { user } = renderApp(<PayDebtButton debt={debt} />, {}, timers.userOptions);
    // The responses settle on promises, so the clock steps until the code shows.
    const settle = async (done: () => boolean) => {
      for (let step = 0; step < 200 && !done(); step++) {
        await timers.advance(1);
      }
    };
    const hasCode = () => screen.queryByRole("img", { name: "QR-код для оплати" }) !== null;
    await user.click(screen.getByRole("button", { name: "Сплатити" }));
    await settle(hasCode);
    expect(hasCode()).toBe(true);

    const amount = screen.getByLabelText("Сума, ₴");
    await user.clear(amount);
    await user.type(amount, "2000,10");

    expect(screen.getByText("Оновлюємо QR-код…")).toBeVisible();
    expect(screen.queryByRole("img", { name: "QR-код для оплати" })).not.toBeInTheDocument();
    expect(api.requestsTo("GET /api/payment-details")).toHaveLength(2);

    await timers.advance(400);
    await settle(hasCode);

    expect(hasCode()).toBe(true);
    expect(api.requests.at(-1)?.query).toEqual({
      kind: "SingleTax",
      periodYear: "2026",
      periodQuarter: "2",
      amountKop: "200010",
    });
    expect(api.requestsTo("GET /api/payment-details")).toHaveLength(3);
  });

  it("shows the missing details without asking for a code", async () => {
    const api = stubFetch({
      "GET /api/payment-details": {
        ...paymentDetails({ query: {} }),
        recipient: null,
        missing: ["iban"],
      },
    });
    const { user } = renderApp(<PayDebtButton debt={debt} />, { locale: "ru" });

    await user.click(screen.getByRole("button", { name: "Оплатить" }));

    expect(await screen.findByText("IBAN счёта казначейства")).toBeVisible();
    expect(screen.getByRole("link", { name: "Указать в настройках" })).toBeVisible();
    expect(api.requests).toHaveLength(1);
  });

  it("says the period is not computed when the api answers 409", async () => {
    stubFetch({ "GET /api/payment-details": reply(409, { title: "Not computed" }) });
    const { user } = renderApp(<PayDebtButton debt={debt} />);

    await user.click(screen.getByRole("button", { name: "Сплатити" }));

    expect(
      await screen.findByText("Цей період застосунок не розраховує, тому реквізитів для нього немає."),
    ).toBeVisible();
  });

  it("asks for the month of an advance", async () => {
    const api = stubFetch({ "GET /api/payment-details": paymentDetails });
    const { user } = renderApp(<PayDebtButton debt={{ ...debt, advanceMonth: 9 }} />);

    await user.click(screen.getByRole("button", { name: "Сплатити" }));
    await waitFor(() => expect(api.requests).toHaveLength(2));

    expect(api.requests[0].query).toEqual({ kind: "SingleTax", periodYear: "2026", periodMonth: "9" });
  });
});
