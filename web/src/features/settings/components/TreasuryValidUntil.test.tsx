import { afterEach, describe, expect, it, vi } from "vitest";
import type { TreasuryAccount } from "@/data/treasury/useTreasuryAccounts";
import { renderApp, screen, stubFetch, waitFor } from "@/test/harness";
import { TreasuryValidUntil } from "./TreasuryValidUntil";

const save = "PUT /api/settings/treasury-accounts/{kind}/valid-until" as const;

const levy: TreasuryAccount = {
  kind: "MilitaryLevy",
  source: "Learned",
  iban: "UA213223130000026007233566001",
  recipientName: "ГУК",
  recipientCode: "37993783",
  updatedAt: "2026-07-02T08:00:00Z",
  validUntil: null,
  learned: null,
  hasLearned: true,
  missing: [],
  notice: null,
};

function today(iso: string) {
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date(`${iso}T09:00:00Z`));
}

describe("TreasuryValidUntil", () => {
  afterEach(() => vi.useRealTimers());

  it("shows no control for a kind with no account", () => {
    renderApp(<TreasuryValidUntil account={{ ...levy, source: "None", iban: null }} />);

    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });

  it("says an account without an end has none", () => {
    renderApp(<TreasuryValidUntil account={levy} />);

    expect(screen.getByText("без обмеження")).toBeVisible();
  });

  it("fills the end of the temporary levy accounts in one tap and saves it", async () => {
    today("2026-10-02");
    const api = stubFetch({ [save]: { ...levy, validUntil: "2026-12-31" } });
    const { user } = renderApp(<TreasuryValidUntil account={levy} />);

    await user.click(screen.getByRole("button", { name: "Вказати строк дії" }));
    await user.click(screen.getByRole("button", { name: "Тимчасовий рахунок: до 31.12.2026" }));
    expect(screen.getByLabelText("Діє до")).toHaveValue("2026-12-31");
    await user.click(screen.getByRole("button", { name: "Зберегти" }));

    await waitFor(() => expect(api.requestsTo(save)).toHaveLength(1));
    expect(api.requestsTo(save)[0]?.body).toEqual({ validUntil: "2026-12-31" });
  });

  it("offers the one-tap end only while the temporary accounts have not ended", async () => {
    today("2027-01-05");
    const late = renderApp(<TreasuryValidUntil account={levy} />);
    await late.user.click(screen.getByRole("button", { name: "Вказати строк дії" }));
    expect(screen.queryByRole("button", { name: /Тимчасовий рахунок/ })).not.toBeInTheDocument();
    late.unmount();

    today("2026-12-31");
    const lastDay = renderApp(<TreasuryValidUntil account={levy} />);
    await lastDay.user.click(screen.getByRole("button", { name: "Вказати строк дії" }));
    expect(screen.getByRole("button", { name: /Тимчасовий рахунок/ })).toBeVisible();
  });

  it("does not offer the one-tap end when the account already has one", async () => {
    today("2026-10-02");
    const { user } = renderApp(<TreasuryValidUntil account={{ ...levy, validUntil: "2026-11-30" }} />);

    await user.click(screen.getByRole("button", { name: "Вказати строк дії" }));

    expect(screen.getByLabelText("Діє до")).toHaveValue("2026-11-30");
    expect(screen.queryByRole("button", { name: /Тимчасовий рахунок/ })).not.toBeInTheDocument();
  });

  it("offers the one-tap end for the military levy only, in Russian too", async () => {
    today("2026-10-02");
    const { user } = renderApp(<TreasuryValidUntil account={{ ...levy, kind: "Esv" }} />, { locale: "ru" });

    await user.click(screen.getByRole("button", { name: "Указать срок действия" }));

    expect(screen.queryByRole("button", { name: /Временный счёт/ })).not.toBeInTheDocument();
    expect(screen.getByLabelText("Действует до")).toBeVisible();
  });
});
