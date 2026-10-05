import { describe, expect, it } from "vitest";
import { useUpdateClient } from "@/data/clients/useClients";
import { useDashboard } from "@/data/dashboard/useDashboard";
import { useCancelInvoice, useDeleteInvoice, useInvoices, useIssueInvoice } from "@/data/invoices/useInvoices";
import { monobankQueryKey, useMonobankConnection } from "@/data/monobank/useMonobank";
import { act, renderApp, reply, screen, stubFetch, waitFor, type Routes } from "@/test/harness";

const dashboard = "GET /api/dashboard" as const;
const invoices = "GET /api/invoices" as const;

function DashboardProbe() {
  useDashboard();
  useMonobankConnection();

  return null;
}

function InvoiceMutations() {
  const issue = useIssueInvoice();
  const cancel = useCancelInvoice();
  const remove = useDeleteInvoice();
  useDashboard();
  useInvoices({});

  return (
    <>
      <button onClick={() => issue.mutate("inv-1")}>issue</button>
      <button onClick={() => cancel.mutate({ id: "inv-1", reason: "duplicate" })}>cancel</button>
      <button onClick={() => remove.mutate("inv-1")}>delete</button>
    </>
  );
}

function ClientRename() {
  const update = useUpdateClient();
  useInvoices({});

  return <button onClick={() => update.mutate({ id: "c-1", body: { name: "Acme Ltd", address: null, country: null, vatId: null, email: null, defaultCurrency: null, notes: null } })}>rename</button>;
}

describe("what a mutation refreshes", () => {
  it("refetches the dashboard when a monobank sync finishes", async () => {
    let pending = true;
    const api = stubFetch({
      [dashboard]: {},
      "GET /api/monobank/connection": () => ({ accounts: [{ syncPending: pending }] }),
    });
    const { queryClient } = renderApp(<DashboardProbe />);
    await waitFor(() => expect(api.requestsTo("GET /api/monobank/connection")).toHaveLength(1));
    expect(api.requestsTo(dashboard)).toHaveLength(1);

    pending = false;
    await act(() => queryClient.invalidateQueries({ queryKey: monobankQueryKey }));

    await waitFor(() => expect(api.requestsTo(dashboard)).toHaveLength(2));
  });

  const invoiceActions: [string, string, Routes][] = [
    ["issues", "issue", { "POST /api/invoices/{id}/issue": {} }],
    ["cancels", "cancel", { "POST /api/invoices/{id}/cancel": {} }],
    ["deletes", "delete", { "DELETE /api/invoices/{id}": reply(204) }],
  ];

  it.each(invoiceActions)(
    "refetches the dashboard's overdue notice when the owner %s an invoice",
    async (_, button, route) => {
      const api = stubFetch({ [dashboard]: {}, [invoices]: [], ...route });
      const { user } = renderApp(<InvoiceMutations />);
      await waitFor(() => expect(api.requestsTo(dashboard)).toHaveLength(1));

      await user.click(screen.getByRole("button", { name: button }));

      await waitFor(() => expect(api.requestsTo(dashboard)).toHaveLength(2));
    },
  );

  it("refetches invoices when a client is renamed", async () => {
    const api = stubFetch({ [invoices]: [], "PUT /api/clients/{id}": {} });
    const { user } = renderApp(<ClientRename />);
    await waitFor(() => expect(api.requestsTo(invoices)).toHaveLength(1));

    await user.click(screen.getByRole("button", { name: "rename" }));

    await waitFor(() => expect(api.requestsTo(invoices)).toHaveLength(2));
  });
});
