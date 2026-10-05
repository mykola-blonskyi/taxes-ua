import { describe, expect, it } from "vitest";
import type { DeclarationResponse } from "@/data/declarations/useDeclarations";
import { renderApp, reply, screen, stubFetch, waitFor } from "@/test/harness";
import { DeclarationScreen } from "./DeclarationScreen";

const route = "GET /api/declarations/{year}/{quarter}" as const;

const locales = [
  { locale: "uk", failed: /Не вдалося завантажити декларацію/, retry: "Спробувати ще раз", retrying: "Повторюємо…" },
  { locale: "ru", failed: /Не удалось загрузить декларацию/, retry: "Повторить", retrying: "Повторяем…" },
] as const;

describe.each(locales)("DeclarationScreen load failure in $locale", ({ locale, failed, retry, retrying }) => {
  it("keeps focus on the retry button through a retry that fails again", async () => {
    let calls = 0;
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    stubFetch({
      [route]: async () => {
        if (++calls === 3) {
          await gate;
        }

        return reply(500, { title: "Boom" });
      },
    });
    const { user } = renderApp(<DeclarationScreen year="2026" quarter="2" />, { locale });

    const button = await screen.findByRole("button", { name: retry }, { timeout: 4000 });
    expect(screen.getByRole("alert")).toHaveTextContent(failed);
    await user.click(button);

    const busy = await screen.findByRole("button", { name: retrying });
    expect(busy).toBe(button);
    expect(busy).toHaveFocus();

    // The element must survive the whole round trip, not only the busy moment: a swap to a loading line
    // in between would be a new button and the focus would fall to the page body.
    const swapped: boolean[] = [];
    const watch = new MutationObserver(() => swapped.push(button.isConnected));
    watch.observe(document.body, { childList: true, subtree: true });
    release();

    await waitFor(() => expect(screen.getByRole("button", { name: retry })).toBeEnabled(), { timeout: 4000 });
    watch.disconnect();

    expect(swapped.every(Boolean)).toBe(true);
    expect(screen.getByRole("button", { name: retry })).toBe(button);
    expect(button).toHaveFocus();
    expect(calls).toBeGreaterThanOrEqual(3);
  }, 20_000);
});

const cached = (quarter: number) =>
  ({
    year: 2026,
    quarter,
    filing: { statutory: "2026-10-09", due: "2026-10-09" },
    payment: { statutory: "2026-10-19", due: "2026-10-19" },
    figures: { esvKop: null },
    limitCrossing: null,
    singleTaxRateBp: 500,
    excessRateBp: 1300,
    militaryLevyRateBp: 100,
    readiness: { ready: true, group3Confirmed: true, missingDetails: [], unknownKvedCodes: [], unpaid: { singleTaxKop: 0, militaryLevyKop: 0, esvKop: 0 } },
    filed: null,
    files: [],
    fileAvailable: true,
    fileAvailableFrom: "2026-07-01",
    cabinet: [],
  }) as unknown as DeclarationResponse;

describe("DeclarationScreen moving between cached quarters", () => {
  it("does not carry a failed download or a chosen type over to the other quarter", async () => {
    stubFetch({
      [route]: (request) => cached(Number(request.path.split("/").at(-1))),
      "POST /api/declarations/{year}/{quarter}/files": reply(409, { title: "Not ready" }),
    });
    const { user, rerender } = renderApp(<DeclarationScreen year="2025" quarter="1" />);
    await screen.findByRole("button", { name: "Завантажити XML" });
    rerender(<DeclarationScreen year="2025" quarter="2" />);
    await waitFor(() => expect(screen.getByRole("heading", { level: 2 })).toHaveTextContent(/півріччя/));
    await screen.findByRole("button", { name: "Завантажити XML" });
    // The filing form and the XML section each ask for the type; the XML one is the last.
    await user.selectOptions(screen.getAllByRole("combobox", { name: "Тип декларації" }).at(-1)!, "Уточнююча");
    await user.click(screen.getByRole("button", { name: "Завантажити XML" }));
    expect(await screen.findByRole("alert")).toBeVisible();

    // Both quarters are cached now, so the data is there at once and nothing remounts unless it is keyed.
    rerender(<DeclarationScreen year="2025" quarter="1" />);

    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    for (const select of screen.getAllByRole("combobox", { name: "Тип декларації" })) {
      expect(select).toHaveValue("Reporting");
    }
  });
});
