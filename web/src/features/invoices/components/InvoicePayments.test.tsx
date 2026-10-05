import { describe, expect, it } from "vitest";
import type { InvoiceResponse, ReceiptOption } from "@/data/invoices/useInvoices";
import { act, renderApp, reply, screen, stubFetch, waitFor } from "@/test/harness";
import { InvoicePayments } from "./InvoicePayments";

const options = "GET /api/invoices/{id}/receipt-options" as const;

const invoice = {
  id: "11111111-1111-4111-8111-111111111111",
  status: "Issued",
  standing: "Issued",
  clientId: "22222222-2222-4222-8222-222222222222",
  currency: "USD",
  paidMinor: 0,
  dueMinor: 100_000,
  receipts: [],
} as unknown as InvoiceResponse;

const receipt: ReceiptOption = {
  id: "33333333-3333-4333-8333-333333333333",
  valueDate: "2026-09-30",
  amountMinor: 100_000,
  currency: "USD",
  clientName: "Acme Ltd",
  clientId: invoice.clientId,
};

describe("InvoicePayments receipt picker", () => {
  it("keeps the receipts to pick from when a background refetch fails", async () => {
    let calls = 0;
    stubFetch({ [options]: () => (++calls === 1 ? [receipt] : reply(500, { title: "Boom" })) });
    const { user, queryClient } = renderApp(<InvoicePayments invoice={invoice} />);
    await user.click(screen.getByRole("button", { name: "Прив’язати надходження" }));
    expect(await screen.findByText("Acme Ltd")).toBeVisible();

    await act(() => queryClient.invalidateQueries());
    await waitFor(() => expect(queryClient.isFetching()).toBe(0));

    expect(calls).toBe(2);
    expect(screen.getByText("Acme Ltd")).toBeVisible();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("shows the failure when the receipts never loaded", async () => {
    stubFetch({ [options]: reply(500, { title: "Boom" }) });
    const { user } = renderApp(<InvoicePayments invoice={invoice} />);

    await user.click(screen.getByRole("button", { name: "Прив’язати надходження" }));

    expect(await screen.findByRole("alert")).toBeVisible();
  });
});
