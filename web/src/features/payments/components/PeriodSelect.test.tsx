import { useState } from "react";
import { describe, expect, it } from "vitest";
import { renderApp, screen, stubFetch, waitFor, type Routes } from "@/test/harness";
import type { PeriodValue } from "../period";
import { PeriodSelect } from "./PeriodSelect";

const hintUk = "Цей період поза групою 3: платіж не зарахується в борги групи 3 і буде показаний окремо.";
const hintRu = "Этот период вне группы 3: платёж не засчитается в долги группы 3 и будет показан отдельно.";

function Host({ initial = "q1" }: { initial?: PeriodValue }) {
  const [value, setValue] = useState<PeriodValue>(initial);

  return <PeriodSelect id="period" year={2026} value={value} onChange={setValue} />;
}

// The owner moved to group 3 in the third quarter: only quarters 3 and 4 are in the group 3 ledger.
const fromThirdQuarter: Routes = {
  "GET /api/periods/{year}": { year: 2026, balances: {}, group3Quarters: [3, 4] },
};

describe("PeriodSelect", () => {
  it("offers the quarters and the months of the year, labelled by its field", () => {
    stubFetch(fromThirdQuarter);
    renderApp(<Host />);

    const select = screen.getByRole("combobox", { name: "Період" });
    expect(select).toHaveValue("q1");
    expect(screen.getByRole("group", { name: "Квартали" })).toBeVisible();
    expect(screen.getByRole("group", { name: "Місяці" })).toBeVisible();
    expect(screen.getAllByRole("option")).toHaveLength(16);
    expect(screen.getByRole("option", { name: "3 квартал" })).toHaveValue("q3");
  });

  it("asks for the periods of the year and warns once they show the quarter is outside group 3", async () => {
    const api = stubFetch(fromThirdQuarter);
    renderApp(<Host />);

    expect(screen.queryByText(hintUk)).not.toBeInTheDocument();
    expect(await screen.findByText(hintUk)).toBeVisible();
    expect(api.requests.map((request) => `${request.method} ${request.path}`)).toEqual(["GET /api/periods/2026"]);
  });

  it("stops warning when the owner picks a quarter in group 3, and starts again for one before it", async () => {
    stubFetch(fromThirdQuarter);
    const { user } = renderApp(<Host />);
    const select = screen.getByRole("combobox", { name: "Період" });
    await screen.findByText(hintUk);

    await user.selectOptions(select, "3 квартал");
    expect(select).toHaveValue("q3");
    expect(screen.queryByText(hintUk)).not.toBeInTheDocument();

    await user.selectOptions(select, "2 квартал");
    expect(screen.getByText(hintUk)).toBeVisible();
  });

  it("judges a month by the quarter it falls in", async () => {
    stubFetch(fromThirdQuarter);
    const { user } = renderApp(<Host initial="m3" />);
    const select = screen.getByRole("combobox", { name: "Період" });
    await screen.findByText(hintUk);

    await user.selectOptions(select, "m7");
    expect(screen.queryByText(hintUk)).not.toBeInTheDocument();

    await user.selectOptions(select, "m6");
    expect(screen.getByText(hintUk)).toBeVisible();
  });

  it("never warns for a year without balances", async () => {
    const api = stubFetch({ "GET /api/periods/{year}": { year: 2026, balances: null, group3Quarters: [] } });
    renderApp(<Host />);

    await waitFor(() => expect(api.requests).toHaveLength(1));
    await waitFor(() => expect(screen.getByRole("combobox", { name: "Період" })).toHaveValue("q1"));
    expect(screen.queryByText(hintUk)).not.toBeInTheDocument();
  });

  it("shows the field, the options and the warning in Russian", async () => {
    stubFetch(fromThirdQuarter);
    renderApp(<Host />, { locale: "ru" });

    expect(screen.getByRole("combobox", { name: "Период" })).toBeVisible();
    expect(screen.getByRole("group", { name: "Кварталы" })).toBeVisible();
    expect(screen.getByRole("group", { name: "Месяцы" })).toBeVisible();
    expect(screen.getByRole("option", { name: "4 квартал" })).toBeVisible();
    expect(await screen.findByText(hintRu)).toBeVisible();
  });
});
