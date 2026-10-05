import { describe, expect, it } from "vitest";
import { act, renderApp, reply, screen, stubFetch, waitFor } from "@/test/harness";
import { InvoicesScreen } from "./InvoicesScreen";

const draft = {
  id: "3f1c1e0e-7a52-4d0e-9c1b-0c9a5a3a0001",
  status: "Draft",
  standing: "Draft",
  number: null,
  clientId: "3f1c1e0e-7a52-4d0e-9c1b-0c9a5a3a0002",
  clientName: "Acme",
  issueDate: "2026-03-01",
  dueDate: "2026-03-15",
  currency: "USD",
  lines: [
    { descriptionEn: "Consulting", descriptionUk: "Консультація", unit: "Service", quantityThousandths: 1000, rateMinor: 100000, amountMinor: 100000 },
  ],
  totalMinor: 100000,
  paidMinor: 0,
  dueMinor: null,
  receipts: [],
  cancelReason: null,
  issuedAt: null,
  cancelledAt: null,
  pdfFileName: "draft.pdf",
} as const;

describe("InvoicesScreen background refetch failure", () => {
  it("keeps the draft editor and the owner's unsaved edit when a refetch fails", async () => {
    let reads = 0;
    stubFetch({
      "GET /api/invoices": [{ ...draft }],
      "GET /api/clients": [{ id: draft.clientId, name: "Acme", defaultCurrency: "USD" }],
      "GET /api/invoices/{id}": () => (++reads === 1 ? draft : reply(500, { title: "Boom" })),
    });
    const { user, queryClient } = renderApp(<InvoicesScreen />);

    await user.click(await screen.findByRole("button", { name: /Acme/ }));
    const description = await screen.findByLabelText("Опис англійською");
    await user.clear(description);
    await user.type(description, "Edited by the owner");

    await act(() => queryClient.invalidateQueries({ queryKey: ["invoices", "detail"] }));
    await waitFor(() => expect(reads).toBe(2));

    expect(screen.getByLabelText("Опис англійською")).toHaveValue("Edited by the owner");
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});
