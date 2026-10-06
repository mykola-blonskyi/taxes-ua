import { describe, expect, it } from "vitest";
import { renderApp, reply, screen, stubFetch, waitFor } from "@/test/harness";
import { KeepUntilLine } from "./KeepUntilLine";

const keepUntil = "GET /api/declarations/{year}/keep-until" as const;

describe("KeepUntilLine", () => {
  it("says to keep a year's documents until a fixed date", async () => {
    stubFetch({ [keepUntil]: { year: 2015, keepUntil: { state: "Fixed", date: "2019-01-31", daysAfterSuspension: null } } });
    renderApp(<KeepUntilLine year={2015} />);

    expect(await screen.findByText("Документи за 2015 рік зберігати до 31.01.2019")).toBeVisible();
    expect(screen.getByText(/ст\. 44 Податкового кодексу/)).toBeVisible();
  });

  it("says the period is extended while martial law lasts, and how long it runs after it", async () => {
    stubFetch({ [keepUntil]: { year: 2026, keepUntil: { state: "ExtendedWhileSuspended", date: "2030-02-08", daysAfterSuspension: 1095 } } });
    renderApp(<KeepUntilLine year={2026} />);

    expect(await screen.findByText(/Документи за 2026 рік зберігати не менше ніж до 08\.02\.2030\./)).toBeVisible();
    expect(screen.getByText(/Строк продовжено на час воєнного стану/)).toBeVisible();
    expect(screen.getByText(/ще 1\D?095 днів\./)).toBeVisible();
  });

  it("reads in Russian", async () => {
    stubFetch({ [keepUntil]: { year: 2026, keepUntil: { state: "ExtendedWhileSuspended", date: "2030-02-08", daysAfterSuspension: 1095 } } });
    renderApp(<KeepUntilLine year={2026} />, { locale: "ru" });

    expect(await screen.findByText(/Документы за 2026 год хранить не менее чем до 08\.02\.2030\./)).toBeVisible();
    expect(screen.getByText(/Срок продлён на время военного положения/)).toBeVisible();
    expect(screen.getByText(/ещё 1\D?095 дней\./)).toBeVisible();
  });

  it("reads a fixed date in Russian", async () => {
    stubFetch({ [keepUntil]: { year: 2015, keepUntil: { state: "Fixed", date: "2019-01-31", daysAfterSuspension: null } } });
    renderApp(<KeepUntilLine year={2015} />, { locale: "ru" });

    expect(await screen.findByText("Документы за 2015 год хранить до 31.01.2019")).toBeVisible();
  });

  it("shows nothing for a year without a group 3 declaration", async () => {
    const api = stubFetch({ [keepUntil]: { year: 2024, keepUntil: null } });
    const { container } = renderApp(<KeepUntilLine year={2024} />);

    await waitFor(() => expect(api.requestsTo(keepUntil)).toHaveLength(1));
    await waitFor(() => expect(container).toBeEmptyDOMElement());
  });

  it("shows nothing when the date cannot be loaded", async () => {
    const api = stubFetch({ [keepUntil]: reply(404, { code: "tax_year_not_found" }) });
    const { container } = renderApp(<KeepUntilLine year={1999} />);

    await waitFor(() => expect(api.requestsTo(keepUntil)).toHaveLength(1));
    expect(container).toBeEmptyDOMElement();
  });
});
