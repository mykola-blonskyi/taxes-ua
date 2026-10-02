import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import type { InvoiceSummary } from "@/data/invoices/useInvoices";
import { reply, renderApp, screen, stubFetch, waitFor } from "@/test/harness";
import { InvoiceSuggestion } from "./InvoiceSuggestion";

const rowName = "Надходження 120,00 ₴";
const receiptId = "r-1";
const acme = "c-acme";

const invoice: InvoiceSummary = {
  id: "inv-1",
  status: "Issued",
  standing: "Issued",
  number: "INV-7",
  clientId: acme,
  clientName: "Acme Ltd",
  issueDate: "2026-06-01",
  dueDate: "2026-06-30",
  currency: "UAH",
  totalMinor: 12_000,
  paidMinor: 0,
  dueMinor: 12_000,
};

const second: InvoiceSummary = { ...invoice, id: "inv-2", number: "INV-8", clientId: "c-other", clientName: "Other LLC" };

const linkRoute = "POST /api/invoices/{id}/receipts/{receiptId}" as const;

// The review screen keeps the failure message, because a failed link reloads the offers and the panel may unmount.
function Host({
  invoices = [invoice],
  receiptClientId = acme,
  onDismiss = () => {},
}: {
  invoices?: InvoiceSummary[];
  receiptClientId?: string | null;
  onDismiss?: () => void;
}) {
  const [failure, setFailure] = useState<string | null>(null);

  return (
    <>
      <InvoiceSuggestion
        receiptId={receiptId}
        invoices={invoices}
        rowName={rowName}
        receiptClientId={receiptClientId}
        onDismiss={onDismiss}
        onFailure={setFailure}
      />
      {failure ? <p role="alert">{failure}</p> : null}
    </>
  );
}

const conflictUk = "Не вдалося прив’язати: інвойс уже сплачено, змінено або надходження вже прив’язане.";
const failedUk = "Не вдалося виконати дію. Спробуйте ще раз.";

describe("InvoiceSuggestion", () => {
  it("offers the invoice and links nothing until the owner confirms it", async () => {
    const api = stubFetch({ [linkRoute]: {} });
    const { user } = renderApp(<Host />);

    expect(screen.getByText("Схоже, це оплата інвойсу")).toBeVisible();
    expect(screen.getByText("INV-7")).toBeVisible();
    expect(screen.getByText("Acme Ltd")).toBeVisible();
    expect(screen.getByText("120,00 ₴")).toBeVisible();
    expect(screen.getByText(/^Оплатити до /)).toBeVisible();
    expect(api.requests).toHaveLength(0);

    await user.click(screen.getByRole("button", { name: `Підтвердити оплату INV-7: ${rowName}` }));

    await waitFor(() => expect(api.requestsTo(linkRoute)).toHaveLength(1));
    expect(api.requests[0]).toMatchObject({ method: "POST", path: "/api/invoices/inv-1/receipts/r-1", body: undefined });
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("links the invoice the owner picked when several are offered", async () => {
    const api = stubFetch({ [linkRoute]: {} });
    const { user } = renderApp(<Host invoices={[invoice, second]} />);

    expect(screen.getByText("Схоже, це оплата одного з інвойсів")).toBeVisible();
    await user.click(screen.getByRole("button", { name: `Підтвердити оплату INV-8: ${rowName}` }));

    await waitFor(() => expect(api.requests).toHaveLength(1));
    expect(api.requests[0].path).toBe("/api/invoices/inv-2/receipts/r-1");
  });

  it("only hides the offer when dismissed, sending nothing", async () => {
    const api = stubFetch({});
    const onDismiss = vi.fn();
    const { user } = renderApp(<Host onDismiss={onDismiss} />);

    await user.click(screen.getByRole("button", { name: `Не прив’язувати: ${rowName}` }));

    expect(onDismiss).toHaveBeenCalledTimes(1);
    expect(api.requests).toHaveLength(0);
  });

  it("warns for an invoice of another client, compared by id, and for no other", () => {
    stubFetch({});
    renderApp(<Host invoices={[invoice, second]} />);

    const notes = screen.getAllByRole("note");
    expect(notes).toHaveLength(1);
    expect(notes[0]).toHaveTextContent("Платник відрізняється від клієнта інвойсу");
    // The warning sits in the card of the other client's invoice.
    expect(notes[0].closest("li")).toHaveTextContent("Other LLC");
  });

  it("does not take a renamed client for another payer", () => {
    stubFetch({});
    // Same id as the receipt's client, another name: only the id decides.
    renderApp(<Host invoices={[{ ...invoice, clientName: "Acme Limited" }]} />);

    expect(screen.getByText("Acme Limited")).toBeVisible();
    expect(screen.queryByRole("note")).not.toBeInTheDocument();
  });

  it("does not warn when the receipt has no client", () => {
    stubFetch({});
    renderApp(<Host receiptClientId={null} />);

    expect(screen.queryByRole("note")).not.toBeInTheDocument();
  });

  it("keeps the conflict message on screen after a 409 on the link", async () => {
    stubFetch({ [linkRoute]: reply(409, { title: "Already paid" }) });
    const { user } = renderApp(<Host />);

    await user.click(screen.getByRole("button", { name: `Підтвердити оплату INV-7: ${rowName}` }));

    expect(await screen.findByRole("alert")).toHaveTextContent(conflictUk);
  });

  it("keeps a generic message after any other failure, and clears it on the next try", async () => {
    let calls = 0;
    const api = stubFetch({ [linkRoute]: () => (++calls === 1 ? reply(500, { title: "Boom" }) : {}) });
    const { user } = renderApp(<Host />);
    const confirm = screen.getByRole("button", { name: `Підтвердити оплату INV-7: ${rowName}` });

    await user.click(confirm);
    expect(await screen.findByRole("alert")).toHaveTextContent(failedUk);

    await user.click(confirm);
    await waitFor(() => expect(screen.queryByRole("alert")).not.toBeInTheDocument());
    expect(api.requestsTo(linkRoute)).toHaveLength(2);
  });

  it("shows the offer, the warning and the failure in Russian", async () => {
    stubFetch({ [linkRoute]: reply(409, { title: "Already paid" }) });
    const { user } = renderApp(<Host invoices={[second]} />, { locale: "ru" });

    expect(screen.getByText("Похоже, это оплата инвойса")).toBeVisible();
    expect(screen.getByRole("note")).toHaveTextContent("Плательщик отличается от клиента инвойса");
    expect(screen.getByText(/^Оплатить до /)).toBeVisible();
    expect(screen.getByRole("button", { name: `Не привязывать: ${rowName}` })).toBeVisible();

    await user.click(screen.getByRole("button", { name: `Подтвердить оплату INV-8: ${rowName}` }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Не удалось привязать: инвойс уже оплачен, изменён или поступление уже привязано.",
    );
  });
});
