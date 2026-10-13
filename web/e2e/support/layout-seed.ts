import { randomUUID } from "node:crypto";
import { expect, type APIRequestContext, type APIResponse } from "@playwright/test";
import { dashboard, seedRegisteredOwner } from "./api";
import { seedInvoicingDetails } from "./seed";

// Long names and long descriptions on purpose: a table or a card that fits a short value is the usual way
// a screen starts scrolling sideways, so the layout check needs the values that stress it.

async function created(response: APIResponse) {
  expect(response.ok(), `${response.url()} answered ${response.status()}: ${await response.text()}`).toBe(true);
  return (await response.json()) as { id: string };
}

function shift(date: string, days: number) {
  const moved = new Date(`${date}T00:00:00Z`);
  moved.setUTCDate(moved.getUTCDate() + days);
  return moved.toISOString().slice(0, 10);
}

// Fills the screens a phone-width check visits, so tables and cards render rows and not empty states:
// income and a non-income transaction, a payment of each kind, a client, one overdue issued invoice and one
// draft. Everything is dated from the api's own "today", so the run does not depend on the calendar.
export async function seedScreensWithContent(owner: APIRequestContext) {
  // A suffix per run, so the seed can run again against a database that already holds an earlier run.
  const tag = randomUUID().slice(0, 6);
  const longClientName = `Дуже Довга Назва Клієнта Зі Сполучними Словами та Підприємства ТОВ ${tag}`;
  const longForeignClientName = `Acme Corporation Incorporated International Limited ${tag}`;
  const paymentNote = `Довга примітка до платежу, яка не вміщується в один рядок на телефоні ${tag}`;
  const { year, quarter } = await seedRegisteredOwner(owner);
  const { today } = await dashboard(owner);
  const month = Number(today.slice(5, 7));

  await created(
    await owner.post("/api/transactions", {
      data: {
        valueDate: today,
        amountMinor: 12345678,
        currency: "UAH",
        manualRateE4: null,
        kind: "Income",
        nonIncomeReason: null,
        clientName: longClientName,
        invoiceNumber: null,
        description: "Розробка програмного забезпечення в рамках договору про надання послуг №123 від початку року",
        refundsTransactionId: null,
      },
    }),
  );
  await created(
    await owner.post("/api/transactions", {
      data: {
        valueDate: today,
        amountMinor: 50000,
        currency: "UAH",
        manualRateE4: null,
        kind: "OwnTransfer",
        nonIncomeReason: "Переказ між власними рахунками, довге пояснення причини",
        clientName: null,
        invoiceNumber: null,
        description: null,
        refundsTransactionId: null,
      },
    }),
  );

  const payment = async (kind: string, amountKop: number, period: { periodQuarter: number | null; periodMonth: number | null }) =>
    created(
      await owner.post("/api/payments", { data: { paidOn: today, kind, amountKop, periodYear: year, note: paymentNote, ...period } }),
    );
  await payment("SingleTax", 500000, { periodQuarter: quarter, periodMonth: null });
  await payment("MilitaryLevy", 100000, { periodQuarter: quarter, periodMonth: null });
  // Small, so the ESV obligation still has a debt and its pay button stays on the payments screen.
  await payment("Esv", 10000, { periodQuarter: null, periodMonth: month });

  const client = await created(
    await owner.post("/api/clients", {
      data: {
        name: longForeignClientName,
        address: "1 Long Street, Springfield, Some State 12345, United States of America",
        country: "DE",
        vatId: null,
        email: "billing@example.com",
        defaultCurrency: null,
        notes: null,
      },
    }),
  );

  await seedInvoicingDetails(owner);

  const invoice = (issueDate: string, dueDate: string) => ({
    clientId: client.id,
    issueDate,
    dueDate,
    currency: "UAH",
    lines: [
      {
        descriptionEn: "Software development services for the reporting month, with a description long enough to wrap",
        descriptionUk: "Послуги з розробки програмного забезпечення за звітний місяць, опис достатньо довгий для переносу",
        unit: "Month",
        quantityThousandths: 1000,
        rateMinor: 150000,
      },
    ],
  });
  const overdue = await created(await owner.post("/api/invoices", { data: invoice(shift(today, -30), shift(today, -15)) }));
  expect((await owner.post(`/api/invoices/${overdue.id}/issue`)).ok()).toBe(true);
  await created(await owner.post("/api/invoices", { data: invoice(today, shift(today, 14)) }));

  return { longClientName, longForeignClientName, paymentNote, year };
}

// Stops the owner's sync with a rejected token, so the dashboard shows its warning card and the layout check
// measures that state. The stack's monobank stub accepts any token for client-info and refuses every statement.
export async function seedRejectedMonobankToken(owner: APIRequestContext) {
  const connected = await owner.put("/api/monobank/connection", { data: { token: "layout-rejected-token" } });
  expect(connected.ok(), `connecting answered ${connected.status()}: ${await connected.text()}`).toBe(true);
  expect((await owner.put("/api/monobank/accounts", { data: { followedExternalIds: ["layout-fop"] } })).ok()).toBe(true);

  await expect
    .poll(async () => ((await (await owner.get("/api/dashboard")).json()) as { sync: { state: string } | null }).sync?.state, {
      message: "the sync did not stop on the rejected token",
      // The api's rate gate holds the statement slot for a minute after the connection check.
      timeout: 75_000,
    })
    .toBe("TokenRejected");
}

// Removes the token again, so the specs after the layout check see an owner with no bank connection.
export async function removeMonobankToken(owner: APIRequestContext) {
  expect((await owner.delete("/api/monobank/connection")).ok()).toBe(true);
}
