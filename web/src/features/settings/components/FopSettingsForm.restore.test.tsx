import { describe, expect, it } from "vitest";
import { useRestoreBackup } from "@/data/backup/backup";
import { act, renderApp, screen, stubFetch, waitFor } from "@/test/harness";
import { FopSettingsForm } from "./FopSettingsForm";

function RestoreButton() {
  const restore = useRestoreBackup();

  return (
    <button onClick={() => restore.mutate(new File(["{}"], "backup.json", { type: "application/json" }))}>
      restore
    </button>
  );
}

describe("a settings form open across a restore", () => {
  const settings = {
    fopRegistrationDate: "2026-01-10",
    paymentMode: "Quarterly",
    esvRegistrationMonthPolicy: "FullMonth",
    esvExempt: false,
    taxPaymentCountsFromStatutoryDeclarationDate: false,
    shiftTaxPaymentFromWeekend: false,
    weekendDays: ["Saturday", "Sunday"],
    locale: "uk",
    theme: "system",
    defaultCurrency: "USD",
    backOnGroup3From: null,
  } as const;

  function stubSettings() {
    let restored = false;

    return stubFetch({
      "GET /api/settings": () => ({ ...settings, defaultCurrency: restored ? "EUR" : "USD" }),
      "POST /api/restore": () => {
        restored = true;

        return {};
      },
    });
  }

  it("shows the restored values", async () => {
    stubSettings();
    const { user } = renderApp(
      <>
        <FopSettingsForm />
        <RestoreButton />
      </>,
    );
    expect(await screen.findByLabelText("Валюта за замовчуванням")).toHaveValue("USD");

    await user.click(screen.getByRole("button", { name: "restore" }));

    await waitFor(() => expect(screen.getByLabelText("Валюта за замовчуванням")).toHaveValue("EUR"));
  });

  it("keeps an edit when a background refetch returns the same data", async () => {
    const api = stubSettings();
    const { user, queryClient } = renderApp(<FopSettingsForm />);
    await user.selectOptions(await screen.findByLabelText("Валюта за замовчуванням"), "UAH");

    await act(() => queryClient.invalidateQueries({ queryKey: ["settings"] }));

    await waitFor(() => expect(api.requestsTo("GET /api/settings")).toHaveLength(2));
    expect(screen.getByLabelText("Валюта за замовчуванням")).toHaveValue("UAH");
  });
});
