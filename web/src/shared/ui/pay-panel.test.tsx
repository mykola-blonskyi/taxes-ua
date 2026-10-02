import type { ComponentProps } from "react";
import { describe, expect, it, vi } from "vitest";
import { renderApp, screen, within } from "@/test/harness";
import { PayPanel } from "./pay-panel";

type Props = ComponentProps<typeof PayPanel>;
type Details = NonNullable<Props["details"]>;

const recipient = { iban: "UA213223130000026007233566001", name: "ГУК у м.Києві/Київ", code: "37993783" };

function detailsFor(overrides: Partial<Details> = {}): Details {
  return {
    purpose: "*;101;1234567890;Єдиний податок;",
    recipient,
    missing: [],
    qrContent: "https://qr.bank.gov.ua/abc",
    amountKop: 123_456,
    qrPending: false,
    ...overrides,
  };
}

function panel(props: Partial<Props> = {}) {
  return (
    <PayPanel
      open
      onOpenChange={() => {}}
      title="Єдиний податок"
      initialAmountKop={123_456}
      onAmountChange={() => {}}
      details={detailsFor()}
      loading={false}
      failed={false}
      notComputed={false}
      {...props}
    />
  );
}

describe("PayPanel", () => {
  describe("with complete details", () => {
    it("shows each field to copy, the amount on screen and the QR code for it", () => {
      renderApp(panel());

      const dialog = screen.getByRole("dialog", { name: "Єдиний податок" });
      expect(within(dialog).getByLabelText("Сума, ₴")).toHaveValue("1234.56");
      expect(within(dialog).getByText(recipient.name)).toBeVisible();
      expect(within(dialog).getByText(recipient.code)).toBeVisible();
      expect(within(dialog).getByText(recipient.iban)).toBeVisible();
      expect(within(dialog).getByText("1234.56")).toBeVisible();
      expect(within(dialog).getByText("*;101;1234567890;Єдиний податок;")).toBeVisible();
      expect(within(dialog).getByRole("img", { name: "QR-код для оплати" })).toBeVisible();
      expect(within(dialog).getByRole("button", { name: "Збільшити QR-код" })).toBeVisible();
    });

    it("copies a field with its own button", async () => {
      const { user } = renderApp(panel());

      await user.click(screen.getByRole("button", { name: "Копіювати: IBAN" }));

      expect(await navigator.clipboard.readText()).toBe(recipient.iban);
    });

    it("copies the amount as plain digits for the bank", async () => {
      const { user } = renderApp(panel());

      await user.click(screen.getByRole("button", { name: "Копіювати: Сума" }));

      expect(await navigator.clipboard.readText()).toBe("1234.56");
    });

    it("opens the QR code larger and closes it again", async () => {
      const { user } = renderApp(panel());

      await user.click(screen.getByRole("button", { name: "Збільшити QR-код" }));

      const enlarged = screen.getByRole("dialog", { name: "QR-код для оплати" });
      expect(within(enlarged).getByRole("img", { name: "QR-код для оплати" })).toBeVisible();

      await user.click(within(enlarged).getByRole("button", { name: "Закрити" }));

      expect(screen.queryByRole("dialog", { name: "QR-код для оплати" })).not.toBeInTheDocument();
    });

    it("reads in Russian", () => {
      renderApp(panel({ title: "Единый налог" }), { locale: "ru" });

      const dialog = screen.getByRole("dialog", { name: "Единый налог" });
      expect(within(dialog).getByLabelText("Сумма, ₴")).toBeVisible();
      expect(within(dialog).getByText("Получатель")).toBeVisible();
      expect(within(dialog).getByText("Назначение платежа")).toBeVisible();
      expect(within(dialog).getByRole("button", { name: "Копировать: IBAN" })).toBeVisible();
      expect(within(dialog).getByRole("img", { name: "QR-код для оплаты" })).toBeVisible();
      expect(within(dialog).getByRole("button", { name: "Увеличить QR-код" })).toBeVisible();
    });
  });

  describe("QR code states", () => {
    it("shows the code only for the amount on screen, and says it is updating for a stale one", async () => {
      const onAmountChange = vi.fn();
      const { user } = renderApp(panel({ onAmountChange }));

      const amount = screen.getByLabelText("Сума, ₴");
      await user.clear(amount);
      await user.type(amount, "2000,10");

      expect(onAmountChange).toHaveBeenLastCalledWith(200_010);
      expect(screen.getByText("Оновлюємо QR-код…")).toBeVisible();
      expect(screen.queryByRole("img", { name: "QR-код для оплати" })).not.toBeInTheDocument();
      expect(screen.getByText("2000.10")).toBeVisible();
    });

    it("shows the code again once the details answer for the new amount", async () => {
      const { user, rerender } = renderApp(panel());
      const amount = screen.getByLabelText("Сума, ₴");
      await user.clear(amount);
      await user.type(amount, "2000,10");
      expect(screen.queryByRole("img", { name: "QR-код для оплати" })).not.toBeInTheDocument();

      rerender(panel({ details: detailsFor({ amountKop: "200010", qrContent: "https://qr.bank.gov.ua/def" }) }));

      expect(screen.getByRole("img", { name: "QR-код для оплати" })).toBeVisible();
      expect(screen.queryByText("Оновлюємо QR-код…")).not.toBeInTheDocument();
    });

    it("says it is updating in Russian", async () => {
      const { user } = renderApp(panel(), { locale: "ru" });

      const amount = screen.getByLabelText("Сумма, ₴");
      await user.clear(amount);
      await user.type(amount, "5");

      expect(screen.getByText("Обновляем QR-код…")).toBeVisible();
    });

    it("hides the code and asks for an amount when the field is cleared or not above zero", async () => {
      const onAmountChange = vi.fn();
      const { user } = renderApp(panel({ onAmountChange }));

      const amount = screen.getByLabelText("Сума, ₴");
      await user.clear(amount);

      expect(onAmountChange).toHaveBeenLastCalledWith(null);
      expect(screen.getByText("Введіть суму більше нуля, наприклад 1234,56.")).toBeVisible();
      expect(screen.getByText("Оновлюємо QR-код…")).toBeVisible();
      expect(screen.getByRole("button", { name: "Копіювати: Сума" })).toBeDisabled();

      await user.type(amount, "0");

      expect(onAmountChange).toHaveBeenLastCalledWith(null);
      expect(screen.getByText("Введіть суму більше нуля, наприклад 1234,56.")).toBeVisible();
    });

    it("says it is updating, not unavailable, while the amount's request is pending", () => {
      renderApp(panel({ details: detailsFor({ qrContent: null, amountKop: null, qrPending: true }) }));

      expect(screen.getByText("Оновлюємо QR-код…")).toBeVisible();
      expect(screen.queryByText(/не вміщуються в формат QR-коду НБУ/)).not.toBeInTheDocument();
    });

    it("does not call the details unavailable when no amount is typed and there is no code", () => {
      renderApp(panel({ initialAmountKop: null, details: detailsFor({ qrContent: null, amountKop: null }) }));

      expect(screen.queryByText(/не вміщуються в формат QR-коду НБУ/)).not.toBeInTheDocument();
      expect(screen.getByText("Введіть суму більше нуля, наприклад 1234,56.")).toBeVisible();
    });

    it("says in Russian that the code is updating, not unavailable, while the request is pending", () => {
      renderApp(panel({ details: detailsFor({ qrContent: null, amountKop: null, qrPending: true }) }), {
        locale: "ru",
      });

      expect(screen.getByText("Обновляем QR-код…")).toBeVisible();
      expect(screen.queryByText(/не помещаются в формат QR-кода НБУ/)).not.toBeInTheDocument();
    });

    it("explains a missing code when the server answered with none", () => {
      renderApp(panel({ details: detailsFor({ qrContent: null, amountKop: null }) }));

      expect(screen.getByText("Ці реквізити не вміщуються в формат QR-коду НБУ. Скористайтеся кнопками копіювання.")).toBeVisible();
      expect(screen.queryByRole("img", { name: "QR-код для оплати" })).not.toBeInTheDocument();
    });

    it("explains a code too long for the NBU format and keeps the copy buttons", () => {
      renderApp(panel({ details: detailsFor({ qrContent: "a".repeat(505) }) }));

      expect(screen.getByText("Ці реквізити не вміщуються в формат QR-коду НБУ. Скористайтеся кнопками копіювання.")).toBeVisible();
      expect(screen.queryByRole("img", { name: "QR-код для оплати" })).not.toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Копіювати: IBAN" })).toBeEnabled();
    });

    it("explains it in Russian", () => {
      renderApp(panel({ details: detailsFor({ qrContent: "a".repeat(505) }) }), { locale: "ru" });

      expect(screen.getByText("Эти реквизиты не помещаются в формат QR-кода НБУ. Воспользуйтесь кнопками копирования.")).toBeVisible();
    });
  });

  describe("missing details", () => {
    const incomplete = detailsFor({
      recipient: null,
      missing: ["iban", "recipientCode"],
      qrContent: null,
      amountKop: null,
    });

    it("lists what is missing, links to the settings and shows no field to copy", () => {
      renderApp(panel({ details: incomplete }));

      expect(screen.getByText("Реквізити неповні, тому застосунок їх не показує. Не вистачає:")).toBeVisible();
      expect(screen.getByText("IBAN рахунку казначейства")).toBeVisible();
      expect(screen.getByText("Код отримувача")).toBeVisible();
      expect(screen.queryByText("Назва отримувача")).not.toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Вказати в налаштуваннях" })).toHaveAttribute(
        "href",
        "/settings?tab=treasury",
      );
      expect(screen.queryByRole("button", { name: /Копіювати/ })).not.toBeInTheDocument();
      expect(screen.queryByRole("img")).not.toBeInTheDocument();
    });

    it("says the same in Russian", () => {
      renderApp(panel({ details: incomplete }), { locale: "ru" });

      expect(screen.getByText("Реквизиты неполные, поэтому приложение их не показывает. Не хватает:")).toBeVisible();
      expect(screen.getByText("IBAN счёта казначейства")).toBeVisible();
      expect(screen.getByRole("link", { name: "Указать в настройках" })).toBeVisible();
    });

    it("shows an unknown field by its own name", () => {
      renderApp(panel({ details: detailsFor({ recipient: null, missing: ["somethingNew"] }) }));

      expect(screen.getByText("somethingNew")).toBeVisible();
    });
  });

  describe("an account with an end", () => {
    const expired = detailsFor({
      recipient: null,
      qrContent: null,
      amountKop: null,
      expiry: { validUntil: "2026-12-31", state: "Expired" },
    });

    it("says the account is closed, links to the settings and shows no details, copy button or QR", () => {
      renderApp(panel({ details: expired }));

      expect(screen.getByText("Рахунок діяв до 31.12.2026. Введіть новий рахунок з Електронного кабінету.")).toBeVisible();
      expect(screen.getByRole("link", { name: "Ввести новий рахунок в налаштуваннях" })).toHaveAttribute(
        "href",
        "/settings?tab=treasury",
      );
      expect(screen.queryByRole("button", { name: /Копіювати/ })).not.toBeInTheDocument();
      expect(screen.queryByRole("img")).not.toBeInTheDocument();
      expect(screen.queryByText(recipient.iban)).not.toBeInTheDocument();
    });

    it("says the same in Russian", () => {
      renderApp(panel({ details: expired }), { locale: "ru" });

      expect(screen.getByText("Счёт действовал до 31.12.2026. Введите новый счёт из Электронного кабинета.")).toBeVisible();
      expect(screen.getByRole("link", { name: "Ввести новый счёт в настройках" })).toBeVisible();
      expect(screen.queryByRole("button", { name: /Копировать/ })).not.toBeInTheDocument();
      expect(screen.queryByRole("img")).not.toBeInTheDocument();
    });

    it("still shows the details and the QR, with a note, while the account ends before the due date", () => {
      renderApp(panel({ details: detailsFor({ expiry: { validUntil: "2026-12-31", state: "ExpiresBeforeDue" } }) }));

      expect(
        screen.getByText("Рахунок діє до 31.12.2026. Сплатіть до цієї дати або після неї введіть новий рахунок."),
      ).toBeVisible();
      expect(screen.getByRole("button", { name: "Копіювати: IBAN" })).toBeVisible();
      expect(screen.getByRole("img", { name: "QR-код для оплати" })).toBeVisible();
    });

    it("says the warning in Russian too", () => {
      renderApp(panel({ details: detailsFor({ expiry: { validUntil: "2026-12-31", state: "ExpiresBeforeDue" } }) }), {
        locale: "ru",
      });

      expect(
        screen.getByText("Счёт действует до 31.12.2026. Оплатите до этой даты или после неё введите новый счёт."),
      ).toBeVisible();
      expect(screen.getByRole("button", { name: "Копировать: IBAN" })).toBeVisible();
    });

    it("shows nothing about an end when there is none", () => {
      renderApp(panel({ details: detailsFor({ expiry: null }) }));

      expect(screen.queryByText(/діє до/)).not.toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Копіювати: IBAN" })).toBeVisible();
    });
  });

  describe("before the details are known", () => {
    it("says it is loading while the first answer is awaited", () => {
      renderApp(panel({ details: undefined, loading: true }));

      expect(screen.getByText("Завантаження реквізитів…")).toBeVisible();
    });

    it("says the details could not be loaded", () => {
      renderApp(panel({ details: undefined, failed: true }));

      expect(screen.getByText("Не вдалося завантажити реквізити.")).toBeVisible();
    });

    it("says the period is not one the app computes", () => {
      renderApp(panel({ details: undefined, notComputed: true }), { locale: "ru" });

      expect(
        screen.getByText("Этот период приложение не рассчитывает, поэтому реквизитов для него нет."),
      ).toBeVisible();
    });
  });
});
