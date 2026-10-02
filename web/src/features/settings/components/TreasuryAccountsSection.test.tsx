import { describe, expect, it } from "vitest";
import type { TreasuryAccount } from "@/data/treasury/useTreasuryAccounts";
import { renderApp, reply, screen, stubFetch, waitFor, within } from "@/test/harness";
import { TreasuryAccountsSection } from "./TreasuryAccountsSection";

const list = "GET /api/settings/treasury-accounts" as const;
const save = "PUT /api/settings/treasury-accounts/{kind}" as const;

const iban = "UA213223130000026007233566001";
const otherIban = "UA678999980313191000026007234";

function account(overrides: Partial<TreasuryAccount>): TreasuryAccount {
  return {
    kind: "MilitaryLevy",
    source: "Learned",
    iban,
    recipientName: "ГУК у м.Києві",
    recipientCode: null,
    updatedAt: "2026-07-02T08:00:00Z",
    validUntil: "2026-12-31",
    learned: null,
    hasLearned: true,
    missing: ["recipientCode"],
    notice: null,
    ...overrides,
  };
}

function others(): TreasuryAccount[] {
  return (["SingleTax", "Esv"] as const).map((kind) =>
    account({ kind, source: "None", iban: null, recipientName: null, validUntil: null, hasLearned: false, missing: [] }),
  );
}

async function editLevy(levy: TreasuryAccount) {
  const api = stubFetch({ [list]: [levy, ...others()], [save]: levy });
  const view = renderApp(<TreasuryAccountsSection />);
  const card = (await screen.findByRole("heading", { name: "Військовий збір" })).closest("li")!;
  await view.user.click(within(card).getByRole("button", { name: "Виправити" }));

  return { api, user: view.user };
}

describe("TreasuryAccountsSection editor", () => {
  it("keeps the end when the same IBAN is saved with a code added to a learned account", async () => {
    const { api, user } = await editLevy(account({}));

    await user.type(screen.getByLabelText(/^Код отримувача/), "37993783");
    await user.click(screen.getByRole("button", { name: "Зберегти" }));

    await waitFor(() => expect(api.requestsTo(save)).toHaveLength(1));
    expect(api.requestsTo(save)[0]?.body).toMatchObject({ iban, recipientCode: "37993783", validUntil: "2026-12-31" });
  });

  it("keeps the end for the same IBAN typed with spaces and in lowercase", async () => {
    const { api, user } = await editLevy(account({ source: "Manual", recipientCode: "37993783", missing: [] }));
    const field = screen.getByLabelText(/^IBAN/);

    await user.clear(field);
    await user.type(field, iban.toLowerCase().replace(/(.{4})/g, "$1 "));
    await user.click(screen.getByRole("button", { name: "Зберегти" }));

    await waitFor(() => expect(api.requestsTo(save)).toHaveLength(1));
    expect(api.requestsTo(save)[0]?.body).toMatchObject({ validUntil: "2026-12-31" });
  });

  it("drops the end when a manual account is replaced with another IBAN", async () => {
    const { api, user } = await editLevy(account({ source: "Manual", recipientCode: "37993783", missing: [] }));
    const field = screen.getByLabelText(/^IBAN/);

    await user.clear(field);
    await user.type(field, otherIban);
    await user.click(screen.getByRole("button", { name: "Зберегти" }));

    await waitFor(() => expect(api.requestsTo(save)).toHaveLength(1));
    expect(api.requestsTo(save)[0]?.body).toMatchObject({ iban: otherIban, validUntil: null });
  });

  it("words a rejected IBAN and recipient by their codes under the right field", async () => {
    const levy = account({ source: "Manual", recipientCode: "37993783", missing: [] });
    stubFetch({
      [list]: [levy, ...others()],
      [save]: reply(400, {
        code: "validation_failed",
        errors: { iban: ["iban bank id must be 899998."], recipientName: ["recipientName is required."] },
        errorCodes: { iban: ["iban_bank_id"], recipientName: ["required"] },
      }),
    });
    const view = renderApp(<TreasuryAccountsSection />);
    const card = (await screen.findByRole("heading", { name: "Військовий збір" })).closest("li")!;
    await view.user.click(within(card).getByRole("button", { name: "Виправити" }));

    await view.user.click(screen.getByRole("button", { name: "Зберегти" }));

    expect(await screen.findByText("Код банку в цьому IBAN не підходить. Для рахунку казначейства він 899998.")).toBeVisible();
    expect(screen.getByText("Заповніть це поле.")).toBeVisible();
    expect(screen.queryByText(/bank id must be/)).not.toBeInTheDocument();
  });
});
