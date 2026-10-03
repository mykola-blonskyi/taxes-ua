import { describe, expect, it } from "vitest";
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
