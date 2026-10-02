import { describe, expect, it, vi } from "vitest";
import { useTranslations } from "next-intl";
import { renderApp, screen, useFakeTimers } from "@/test/harness";
import { CopyField } from "./copy-field";

// The labels come from the real catalog the way pay-panel passes them, so a renamed key fails here too.
function IbanField({ value, copyValue }: { value: string; copyValue?: string | null }) {
  const t = useTranslations("pay");

  return (
    <CopyField
      label={t("iban")}
      value={value}
      copyValue={copyValue}
      copyLabel={t("copy", { field: t("iban") })}
      copiedLabel={t("copied")}
      failedLabel={t("copyFailed")}
    />
  );
}

const iban = "UA213223130000026007233566001";

describe("CopyField", () => {
  it("shows the value and copies it to the clipboard", async () => {
    const { user } = renderApp(<IbanField value={iban} />);

    expect(screen.getByText(iban)).toBeVisible();
    expect(screen.queryByRole("status")).toBeEmptyDOMElement();

    await user.click(screen.getByRole("button", { name: "Копіювати: IBAN" }));

    expect(await navigator.clipboard.readText()).toBe(iban);
    expect(screen.getByRole("status")).toHaveTextContent("Скопійовано");
  });

  it("says so in Russian", async () => {
    const { user } = renderApp(<IbanField value={iban} />, { locale: "ru" });

    await user.click(screen.getByRole("button", { name: "Копировать: IBAN" }));

    expect(await navigator.clipboard.readText()).toBe(iban);
    expect(screen.getByRole("status")).toHaveTextContent("Скопировано");
  });

  it("copies a different value than the one it shows", async () => {
    const { user } = renderApp(<IbanField value="1 234,56" copyValue="1234.56" />);

    await user.click(screen.getByRole("button", { name: "Копіювати: IBAN" }));

    expect(screen.getByText("1 234,56")).toBeVisible();
    expect(await navigator.clipboard.readText()).toBe("1234.56");
  });

  it("forgets the confirmation after two seconds", async () => {
    const timers = useFakeTimers();
    const { user } = renderApp(<IbanField value={iban} />, {}, timers.userOptions);

    await user.click(screen.getByRole("button", { name: "Копіювати: IBAN" }));
    // The clipboard answers on a promise; the clock steps until the confirmation shows.
    for (let step = 0; step < 100 && screen.getByRole("status").textContent === ""; step++) {
      await timers.advance(1);
    }
    expect(screen.getByRole("status")).toHaveTextContent("Скопійовано");
    const copiedAt = Date.now();

    await timers.advance(1_900);
    expect(screen.getByRole("status")).toHaveTextContent("Скопійовано");

    await timers.advance(copiedAt + 2_001 - Date.now());
    expect(screen.getByRole("status")).toBeEmptyDOMElement();
  });

  it("reports a clipboard the browser refuses", async () => {
    const { user } = renderApp(<IbanField value={iban} />);
    vi.spyOn(navigator.clipboard, "writeText").mockRejectedValue(new DOMException("denied", "NotAllowedError"));

    await user.click(screen.getByRole("button", { name: "Копіювати: IBAN" }));

    expect(screen.getByRole("status")).toHaveTextContent("Не вдалося скопіювати");
  });

  it("disables the button when there is nothing to copy", () => {
    renderApp(<IbanField value="—" copyValue={null} />);

    expect(screen.getByRole("button", { name: "Копіювати: IBAN" })).toBeDisabled();
  });
});
