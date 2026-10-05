import { describe, expect, it } from "vitest";
import type { PaymentDetails } from "@/data/payments/usePaymentDetails";
import type { PeriodsResponse } from "@/data/periods/usePeriods";
import { act, renderApp, reply, screen, stubFetch, waitFor } from "@/test/harness";
import { ObligationsPanel } from "./ObligationsPanel";

const route = "GET /api/payment-details" as const;
const iban = "UA213223130000026007233566001";

const settled = { remainingKop: 0, dueDate: "2026-10-19" };
const quarters = [
  { quarter: 3, obligations: { singleTax: settled, militaryLevy: settled, esv: settled } },
] as unknown as PeriodsResponse["quarters"];

const details: PaymentDetails = {
  kind: "SingleTax",
  periodYear: 2026,
  periodQuarter: 3,
  periodMonth: null,
  amountKop: null,
  purpose: "*;101;1234567890;Єдиний податок;",
  recipient: { iban, name: "ГУК у м.Києві/Київ", code: "37993783", source: "Manual" },
  missing: [],
  qrContent: null,
  expiry: null,
};

describe("ObligationsPanel pay dialog", () => {
  it("keeps the recipient details when a background refetch fails", async () => {
    let calls = 0;
    stubFetch({ [route]: () => (++calls === 1 ? details : reply(500, { title: "Boom" })) });
    const { user, queryClient } = renderApp(<ObligationsPanel year={2026} quarters={quarters} />);
    await user.click(screen.getAllByRole("button", { name: "Сплатити" })[0]);
    expect(await screen.findByText(iban)).toBeVisible();

    await act(() => queryClient.invalidateQueries());
    await waitFor(() => expect(queryClient.isFetching()).toBe(0), { timeout: 4000 });

    expect(calls).toBeGreaterThan(1);
    expect(screen.getByText(iban)).toBeVisible();
    expect(screen.queryByText("Не вдалося завантажити реквізити.")).not.toBeInTheDocument();
  });

  it("shows the failure when the details never loaded", async () => {
    stubFetch({ [route]: reply(500, { title: "Boom" }) });
    const { user } = renderApp(<ObligationsPanel year={2026} quarters={quarters} />);

    await user.click(screen.getAllByRole("button", { name: "Сплатити" })[0]);

    expect(await screen.findByText("Не вдалося завантажити реквізити.", {}, { timeout: 4000 })).toBeVisible();
  });
});
