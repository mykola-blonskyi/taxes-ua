import { describe, expect, it } from "vitest";
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
  validUntilSource: null,
  learned: null,
  hasLearned: true,
  missing: [],
  notice: null,
};

describe("TreasuryValidUntil", () => {
  it("shows no control for a kind with no account", () => {
    renderApp(<TreasuryValidUntil account={{ ...levy, source: "None", iban: null }} />);

    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });

  it("says an account without an end has none", () => {
    renderApp(<TreasuryValidUntil account={levy} />);

    expect(screen.getByText("без обмеження")).toBeVisible();
  });

  it("marks an end that is the tax year's default as such", () => {
    renderApp(<TreasuryValidUntil account={{ ...levy, validUntil: "2026-12-31", validUntilSource: "Default" }} />);

    expect(
      screen.getByText("31 груд. 2026 р. (за замовчуванням для рахунків військового збору 2026 року)"),
    ).toBeVisible();
  });

  it("marks the default in Russian too", () => {
    renderApp(<TreasuryValidUntil account={{ ...levy, validUntil: "2026-12-31", validUntilSource: "Default" }} />, {
      locale: "ru",
    });

    expect(screen.getByText(/по умолчанию для счетов военного сбора 2026 года/)).toBeVisible();
  });

  it("shows an end the owner set as a plain date", () => {
    renderApp(<TreasuryValidUntil account={{ ...levy, validUntil: "2026-11-30", validUntilSource: "Owner" }} />);

    expect(screen.getByText("30 лист. 2026 р.")).toBeVisible();
    expect(screen.queryByText(/за замовчуванням/)).not.toBeInTheDocument();
  });

  it("removes the end, and with it the default, by clearing the date and saving", async () => {
    const api = stubFetch({ [save]: { ...levy, validUntil: null, validUntilSource: null } });
    const { user } = renderApp(
      <TreasuryValidUntil account={{ ...levy, validUntil: "2026-12-31", validUntilSource: "Default" }} />,
    );

    await user.click(screen.getByRole("button", { name: "Вказати строк дії" }));
    expect(screen.getByLabelText("Діє до")).toHaveValue("2026-12-31");
    expect(screen.getByText(/не підставить і строк за замовчуванням/)).toBeVisible();
    await user.click(screen.getByRole("button", { name: "Прибрати строк" }));
    await user.click(screen.getByRole("button", { name: "Зберегти" }));

    await waitFor(() => expect(api.requestsTo(save)).toHaveLength(1));
    expect(api.requestsTo(save)[0]?.body).toEqual({ validUntil: null });
  });

  it("saves a date the owner types", async () => {
    const api = stubFetch({ [save]: { ...levy, validUntil: "2026-11-30", validUntilSource: "Owner" } });
    const { user } = renderApp(<TreasuryValidUntil account={levy} />);

    await user.click(screen.getByRole("button", { name: "Вказати строк дії" }));
    await user.type(screen.getByLabelText("Діє до"), "2026-11-30");
    await user.click(screen.getByRole("button", { name: "Зберегти" }));

    await waitFor(() => expect(api.requestsTo(save)).toHaveLength(1));
    expect(api.requestsTo(save)[0]?.body).toEqual({ validUntil: "2026-11-30" });
  });
});
