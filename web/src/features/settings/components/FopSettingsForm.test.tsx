import { describe, expect, it } from "vitest";
import { renderApp, reply, screen, stubFetch } from "@/test/harness";
import { FopSettingsForm } from "./FopSettingsForm";

const read = "GET /api/settings" as const;
const write = "PUT /api/settings" as const;

const settings = {
  fopRegistrationDate: "2026-01-10",
  paymentMode: "Quarterly",
  esvRegistrationMonthPolicy: "FullMonth",
  esvExempt: false,
  taxPaymentCountsFromStatutoryDeclarationDate: false,
  shiftTaxPaymentFromWeekend: false,
  weekendDays: ["Saturday", "Sunday"],
  locale: "uk",
  theme: "system",
  defaultCurrency: "USD",
  backOnGroup3From: null,
} as const;

describe("FopSettingsForm rejection", () => {
  it("words a registration date after the group 3 receipt by its code, in the owner's language", async () => {
    stubFetch({
      [read]: settings,
      [write]: reply(400, {
        title: "One or more validation errors occurred.",
        code: "validation_failed",
        errors: { fopRegistrationDate: ["fopRegistrationDate must not be after the group 3 confirmation date."] },
        errorCodes: { fopRegistrationDate: ["registration_date_after_group3_receipt"] },
      }),
    });
    const { user } = renderApp(<FopSettingsForm />);

    await user.click(await screen.findByRole("button", { name: "Зберегти" }));

    expect(await screen.findByText("Дата реєстрації не може бути пізніше за дату квитанції ДПС про групу 3.")).toBeVisible();
    expect(screen.getByText("Перевірте введені дані:")).toBeVisible();
    expect(screen.queryByText(/must not be after/)).not.toBeInTheDocument();
  });

  it("reads the same rejection in Russian", async () => {
    stubFetch({
      [read]: settings,
      [write]: reply(400, {
        code: "validation_failed",
        errorCodes: { fopRegistrationDate: ["registration_date_after_group3_receipt"] },
      }),
    });
    const { user } = renderApp(<FopSettingsForm />, { locale: "ru" });

    await user.click(await screen.findByRole("button", { name: "Сохранить" }));

    expect(await screen.findByText("Дата регистрации не может быть позже даты квитанции ГНС о группе 3.")).toBeVisible();
  });

  it("shows the generic sentence, never the English, for a code this build has no words for", async () => {
    stubFetch({
      [read]: settings,
      [write]: reply(400, {
        code: "validation_failed",
        errors: { theme: ["theme must be one of light, dark, system."] },
        errorCodes: { theme: ["a_code_from_the_future"] },
      }),
    });
    const { user } = renderApp(<FopSettingsForm />);

    await user.click(await screen.findByRole("button", { name: "Зберегти" }));

    expect(await screen.findByText("Не вдалося виконати дію. Спробуйте ще раз.")).toBeVisible();
    expect(screen.queryByText(/theme must be/)).not.toBeInTheDocument();
  });
});
