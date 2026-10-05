import { describe, expect, it } from "vitest";
import { renderApp, reply, screen, stubFetch } from "@/test/harness";
import { InvoicingForm } from "./InvoicingForm";

const read = "GET /api/settings/invoicing" as const;
const write = "PUT /api/settings/invoicing" as const;
const connection = "GET /api/monobank/connection" as const;

const details = {
  sellerNameUk: "Іваненко Іван",
  sellerNameEn: "Ivan Ivanenko",
  rnokpp: "1234567890",
  addressUk: "Київ",
  addressEn: "Kyiv",
  acceptanceClauseEn: "a",
  acceptanceClauseUk: "а",
  feesClauseEn: "f",
  feesClauseUk: "ф",
  taxStatusClauseEn: "t",
  taxStatusClauseUk: "т",
  paymentDetails: [
    {
      currency: "USD",
      iban: "UA573220010000026007233566002",
      beneficiaryBank: "JSC Universal Bank, Kyiv",
      swift: "UNJSUAUKXXX",
      intermediaryBank: "",
      intermediarySwift: "",
      intermediaryAccount: "",
    },
  ],
  hasSignature: false,
  signatureUpdatedAt: null,
  defaults: {
    acceptanceClauseEn: "a",
    acceptanceClauseUk: "а",
    feesClauseEn: "f",
    feesClauseUk: "ф",
    taxStatusClauseEn: "t",
    taxStatusClauseUk: "т",
  },
} as const;

describe("InvoicingForm rejection", () => {
  it("words an IBAN, a SWIFT code and a tax number by their codes under the right field", async () => {
    stubFetch({
      [read]: details,
      [connection]: { connected: false, accounts: [] },
      [write]: reply(400, {
        code: "validation_failed",
        errors: {
          rnokpp: ["rnokpp must be 10 digits."],
          "paymentDetails[0].iban": ["iban checksum is wrong."],
          "paymentDetails[0].swift": ["swift must be 8 or 11 letters and digits."],
        },
        errorCodes: {
          rnokpp: ["rnokpp_invalid"],
          "paymentDetails[0].iban": ["iban_checksum"],
          "paymentDetails[0].swift": ["swift_invalid"],
        },
      }),
    });
    const { user } = renderApp(<InvoicingForm />);

    await user.click(await screen.findByRole("button", { name: "Зберегти" }));

    expect(await screen.findByText("РНОКПП має складатися з 10 цифр.")).toBeVisible();
    expect(screen.getByText("Контрольна сума IBAN не збігається: перевірте, чи немає помилки в цифрах.")).toBeVisible();
    expect(screen.getByText("SWIFT має складатися з 8 або 11 літер і цифр.")).toBeVisible();
    expect(screen.queryByText(/checksum is wrong/)).not.toBeInTheDocument();
  });

  it("words a repeated currency and a missing bank in Russian", async () => {
    stubFetch({
      [read]: details,
      [connection]: { connected: false, accounts: [] },
      [write]: reply(400, {
        code: "validation_failed",
        errorCodes: { "paymentDetails[0].beneficiaryBank": ["required"], "paymentDetails[0].currency": ["currency_duplicate"] },
      }),
    });
    const { user } = renderApp(<InvoicingForm />, { locale: "ru" });

    await user.click(await screen.findByRole("button", { name: "Сохранить" }));

    expect(await screen.findByText("Заполните это поле.")).toBeVisible();
  });
});

describe("InvoicingForm network failure", () => {
  it.each([
    ["uk", "Зберегти", "Не вдалося зберегти: Немає зв'язку з сервером. Перевірте мережу й спробуйте ще раз."],
    ["ru", "Сохранить", "Не удалось сохранить: Нет связи с сервером. Проверьте сеть и попробуйте ещё раз."],
  ] as const)("says so in %s when the save never reaches the api", async (locale, button, message) => {
    stubFetch({
      [read]: details,
      [connection]: { connected: false, accounts: [] },
      [write]: () => {
        throw new TypeError("Failed to fetch");
      },
    });
    const { user } = renderApp(<InvoicingForm />, { locale });

    await user.click(await screen.findByRole("button", { name: button }));

    expect(await screen.findByText(message)).toBeVisible();
  });
});
