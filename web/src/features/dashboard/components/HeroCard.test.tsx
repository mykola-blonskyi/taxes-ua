import { describe, expect, it } from "vitest";
import type { KindDebt } from "@/data/dashboard/useDashboard";
import type { PaymentResponse } from "@/data/payments/usePayments";
import { renderApp, reply, screen, stubFetch, waitFor } from "@/test/harness";
import { HeroCard } from "./HeroCard";

const today = "2026-10-02";

const singleTax: KindDebt = {
  kind: "SingleTax",
  fromYear: 2026,
  fromQuarter: 3,
  toYear: 2026,
  toQuarter: 3,
  amountKop: 123_456,
  dueDate: "2026-10-19",
  status: "Due",
  daysLeft: 17,
  advanceMonth: null,
};

const levy: KindDebt = { ...singleTax, kind: "MilitaryLevy", amountKop: 20_000 };

const created: PaymentResponse = {
  id: "00000000-0000-0000-0000-000000000001",
  paidOn: today,
  kind: "SingleTax",
  amountKop: 123_456,
  periodYear: 2026,
  periodQuarter: 3,
  periodMonth: null,
  note: null,
  beforeRegistration: false,
};

async function setDate(user: ReturnType<typeof renderCard>["user"], value: string) {
  const field = screen.getByLabelText("Дата оплати");
  await user.clear(field);

  if (value) {
    await user.type(field, value);
  }
}

const post = "POST /api/payments" as const;

function renderCard(now: KindDebt[], locale: "uk" | "ru" = "uk") {
  return renderApp(<HeroCard now={now} today={today} busy={false} />, { locale });
}

describe("HeroCard mark paid", () => {
  it("starts from today and the debt's amount, and records exactly those", async () => {
    const api = stubFetch({ [post]: created });
    const { user } = renderCard([singleTax]);

    await user.click(screen.getByRole("button", { name: "Позначити сплаченим" }));

    expect(screen.getByLabelText("Дата оплати")).toHaveValue(today);
    expect(screen.getByLabelText("Сума, ₴")).toHaveValue("1234.56");

    await user.click(screen.getByRole("button", { name: "Так, записати" }));

    await waitFor(() => expect(api.requestsTo(post)).toHaveLength(1));
    expect(api.requests[0].body).toEqual({
      paidOn: today,
      kind: "SingleTax",
      amountKop: 123_456,
      periodYear: 2026,
      periodQuarter: 3,
      periodMonth: null,
      note: null,
    });
  });

  it("records the date and the amount the owner sets", async () => {
    const api = stubFetch({ [post]: created });
    const { user } = renderCard([singleTax]);

    await user.click(screen.getByRole("button", { name: "Позначити сплаченим" }));
    await setDate(user, "2026-10-01");
    await user.clear(screen.getByLabelText("Сума, ₴"));
    await user.type(screen.getByLabelText("Сума, ₴"), "1000,50");
    await user.click(screen.getByRole("button", { name: "Так, записати" }));

    await waitFor(() => expect(api.requestsTo(post)).toHaveLength(1));
    expect(api.requests[0].body).toMatchObject({ paidOn: "2026-10-01", amountKop: 100_050 });
  });

  it("starts from the amount edited in the pay panel", async () => {
    stubFetch({ "GET /api/payment-details": { kind: "SingleTax", missing: ["iban"], recipient: null }, [post]: created });
    const { user } = renderCard([singleTax]);

    await user.click(screen.getByRole("button", { name: "Сплатити" }));
    const panelAmount = await screen.findByLabelText("Сума, ₴");
    await user.clear(panelAmount);
    await user.type(panelAmount, "777,70");
    await user.click(screen.getByRole("button", { name: "Закрити" }));
    await user.click(screen.getByRole("button", { name: "Позначити сплаченим" }));

    expect(screen.getByLabelText("Сума, ₴")).toHaveValue("777.70");
  });

  it("names each amount by its tax when several are recorded at once", async () => {
    const api = stubFetch({ [post]: created });
    const { user } = renderCard([singleTax, levy], "ru");

    await user.click(screen.getByRole("button", { name: "Отметить оплаченным" }));
    await user.clear(screen.getByLabelText("Сумма: Военный сбор, ₴"));
    await user.type(screen.getByLabelText("Сумма: Военный сбор, ₴"), "150");
    await user.click(screen.getByRole("button", { name: "Да, записать" }));

    await waitFor(() => expect(api.requestsTo(post)).toHaveLength(2));
    expect(api.requests.map((request) => request.body)).toMatchObject([
      { kind: "SingleTax", amountKop: 123_456, paidOn: today },
      { kind: "MilitaryLevy", amountKop: 15_000, paidOn: today },
    ]);
  });

  it("refuses a date after today, a missing date and an amount that is not above zero", async () => {
    const api = stubFetch({ [post]: created });
    const { user } = renderCard([singleTax]);
    const confirm = () => screen.getByRole("button", { name: "Так, записати" });

    await user.click(screen.getByRole("button", { name: "Позначити сплаченим" }));
    expect(confirm()).toBeEnabled();

    await setDate(user, "2026-10-03");
    expect(screen.getByText(/Вкажіть дату оплати/)).toBeVisible();
    expect(confirm()).toBeDisabled();

    await setDate(user, "");
    expect(confirm()).toBeDisabled();

    await setDate(user, today);
    expect(confirm()).toBeEnabled();

    for (const bad of ["0", "abc"]) {
      await user.clear(screen.getByLabelText("Сума, ₴"));
      await user.type(screen.getByLabelText("Сума, ₴"), bad);
      expect(screen.getByText("Введіть суму більше нуля, наприклад 1234,56.")).toBeVisible();
      expect(confirm()).toBeDisabled();
    }

    expect(api.requests).toHaveLength(0);
  });

  it("says it failed, and keeps no form open, when the api refuses the payment", async () => {
    stubFetch({ [post]: reply(400, { title: "One or more validation errors occurred." }) });
    const { user } = renderCard([singleTax]);

    await user.click(screen.getByRole("button", { name: "Позначити сплаченим" }));
    await user.click(screen.getByRole("button", { name: "Так, записати" }));

    expect(await screen.findByText("Не вдалося записати платіж.")).toBeVisible();
  });

  it("closes the form on cancel without recording", async () => {
    const api = stubFetch({});
    const { user } = renderCard([singleTax]);

    await user.click(screen.getByRole("button", { name: "Позначити сплаченим" }));
    await user.click(screen.getByRole("button", { name: "Скасувати" }));

    expect(screen.getByRole("button", { name: "Позначити сплаченим" })).toBeVisible();
    expect(api.requests).toHaveLength(0);
  });
});
