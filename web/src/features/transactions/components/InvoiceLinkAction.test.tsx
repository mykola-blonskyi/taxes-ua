import { describe, expect, it } from "vitest";
import type { InvoiceSummary } from "@/data/invoices/useInvoices";
import type { TransactionResponse } from "@/data/transactions/useTransactions";
import { act, renderApp, reply, screen, stubFetch, waitFor } from "@/test/harness";
import { InvoiceLinkAction } from "./InvoiceLinkAction";

const payable = "GET /api/invoices/payable-by/{receiptId}" as const;

const transaction = {
  id: "33333333-3333-4333-8333-333333333333",
  invoiceId: null,
} as unknown as TransactionResponse;

const invoice: InvoiceSummary = {
  id: "11111111-1111-4111-8111-111111111111",
  status: "Issued",
  standing: "Issued",
  number: "INV-7",
  clientId: "22222222-2222-4222-8222-222222222222",
  clientName: "Acme Ltd",
  issueDate: "2026-09-01",
  dueDate: "2026-10-01",
  currency: "USD",
  totalMinor: 100_000,
  paidMinor: 0,
  dueMinor: 100_000,
};

const open = "Прив’язати до інвойсу: Рядок";

describe("InvoiceLinkAction invoice picker", () => {
  it("keeps the invoices to pick from when a background refetch fails", async () => {
    let calls = 0;
    stubFetch({ [payable]: () => (++calls === 1 ? [invoice] : reply(500, { title: "Boom" })) });
    const { user, queryClient } = renderApp(<InvoiceLinkAction transaction={transaction} rowName="Рядок" />);
    await user.click(screen.getByRole("button", { name: open }));
    expect(await screen.findByText("INV-7")).toBeVisible();

    await act(() => queryClient.invalidateQueries());
    await waitFor(() => expect(queryClient.isFetching()).toBe(0));

    expect(calls).toBe(2);
    expect(screen.getByText("INV-7")).toBeVisible();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("shows the failure when the invoices never loaded", async () => {
    stubFetch({ [payable]: reply(500, { title: "Boom" }) });
    const { user } = renderApp(<InvoiceLinkAction transaction={transaction} rowName="Рядок" />);

    await user.click(screen.getByRole("button", { name: open }));

    expect(await screen.findByRole("alert")).toBeVisible();
  });
});
