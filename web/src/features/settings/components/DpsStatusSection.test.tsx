import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { DpsStatusResponse } from "@/data/settings/useDpsStatus";
import { reply, renderApp, screen, stubFetch, waitFor } from "@/test/harness";
import { DpsStatusSection } from "./DpsStatusSection";
import { SettingsTabs } from "./SettingsTabs";

const read = "GET /api/settings/dps-status" as const;
const write = "PUT /api/settings/dps-status" as const;

const unconfirmed: DpsStatusResponse = {
  group3Since: null,
  confirmation: null,
  fopRegistered: false,
  esvRegistered: false,
  accountsRegistered: false,
  fopRegistrationDate: "2026-09-28",
  group3Start: "2026-09-28",
  applicationDeadline: "2026-10-08",
};

// Only Date is faked: the deadline line compares against the Kyiv "today", while requests and user
// events keep their real timers.
beforeEach(() => {
  vi.useFakeTimers({ toFake: ["Date"], now: new Date("2026-10-02T09:00:00Z") });
});

afterEach(() => {
  vi.useRealTimers();
});

describe("DpsStatusSection", () => {
  it("loads the stored status with the registration date and the application deadline", async () => {
    stubFetch({ [read]: unconfirmed });
    renderApp(<DpsStatusSection />);

    expect(await screen.findByRole("checkbox", { name: "ФОП зареєстровано" })).not.toBeChecked();
    expect(screen.getByRole("checkbox", { name: "Заяву про обрання 3 групи прийнято" })).not.toBeChecked();
    expect(screen.getByRole("checkbox", { name: "Зареєстровано платником ЄСВ" })).not.toBeChecked();
    expect(screen.getByRole("checkbox", { name: "Рахунки зареєстровано" })).not.toBeChecked();
    expect(screen.getByText(/^Дата реєстрації: 28/)).toBeVisible();
    expect(screen.getByRole("radio", { name: "З дати реєстрації (заяву подано протягом 10 днів)" })).toBeChecked();
    expect(screen.getByText(/^Заяву треба подати до 8 /)).toBeVisible();
    expect(screen.queryByLabelText("Номер квитанції")).not.toBeInTheDocument();
  });

  it("links to the FOP tab while the registration date is not set", async () => {
    stubFetch({ [read]: { ...unconfirmed, fopRegistrationDate: null, group3Start: null, applicationDeadline: null } });
    renderApp(<DpsStatusSection />);

    expect(await screen.findByText(/Дату реєстрації ще не вказано/)).toBeVisible();
    expect(screen.getByRole("link", { name: "Вказати дату" })).toHaveAttribute("href", "/settings?tab=fop");
    expect(screen.getByRole("radio", { name: "З першого дня кварталу (заяву подано пізніше)" })).toBeDisabled();
  });

  it("reveals the receipt fields when the application is ticked, and sends every tick", async () => {
    const api = stubFetch({ [read]: unconfirmed, [write]: (request) => ({ ...unconfirmed, ...(request.body as object) }) });
    const { user } = renderApp(<DpsStatusSection />);

    await user.click(await screen.findByRole("checkbox", { name: "ФОП зареєстровано" }));
    await user.click(screen.getByRole("checkbox", { name: "Заяву про обрання 3 групи прийнято" }));
    expect(screen.queryByText(/^Заяву треба подати до/)).not.toBeInTheDocument();

    await user.type(screen.getByLabelText("Дата квитанції"), "2026-10-01");
    await user.type(screen.getByLabelText("Номер квитанції"), "9154123456");
    await user.click(screen.getByRole("checkbox", { name: "Рахунки зареєстровано" }));
    await user.click(screen.getByRole("button", { name: "Зберегти" }));

    await waitFor(() => expect(api.requestsTo(write)).toHaveLength(1));
    expect(api.requestsTo(write)[0].body).toEqual({
      group3Since: null,
      confirmation: { confirmedOn: "2026-10-01", receiptNumber: "9154123456" },
      fopRegistered: true,
      esvRegistered: false,
      accountsRegistered: true,
    });
    expect(await screen.findByRole("status")).toHaveTextContent("Збережено.");
  });

  it("sends the first day of the quarter picked", async () => {
    const api = stubFetch({ [read]: unconfirmed, [write]: unconfirmed });
    const { user } = renderApp(<DpsStatusSection />);

    await user.click(await screen.findByRole("radio", { name: "З першого дня кварталу (заяву подано пізніше)" }));
    const quarter = screen.getByRole("combobox", { name: "Квартал" });
    expect(quarter).toHaveValue("2026-10-01");
    await user.selectOptions(quarter, "2027-01-01");
    await user.click(screen.getByRole("button", { name: "Зберегти" }));

    await waitFor(() => expect(api.requestsTo(write)).toHaveLength(1));
    expect(api.requestsTo(write)[0].body).toMatchObject({ group3Since: "2027-01-01", confirmation: null });
  });

  it("shows a stored later start as the quarter choice", async () => {
    stubFetch({ [read]: { ...unconfirmed, group3Since: "2027-01-01", group3Start: "2027-01-01" } });
    renderApp(<DpsStatusSection />);

    expect(await screen.findByRole("radio", { name: "З першого дня кварталу (заяву подано пізніше)" })).toBeChecked();
    expect(screen.getByRole("combobox", { name: "Квартал" })).toHaveValue("2027-01-01");
    expect(screen.queryByText(/^Заяву треба подати до/)).not.toBeInTheDocument();
  });

  it("shows the server's rejection beside the receipt field in the owner's language", async () => {
    stubFetch({
      // Blank once the api trims it; the browser's own "required" check lets spaces through.
      [read]: { ...unconfirmed, confirmation: { confirmedOn: "2026-10-01", receiptNumber: "  " } },
      [write]: reply(400, {
        title: "One or more validation errors occurred.",
        code: "validation_failed",
        errors: { "confirmation.receiptNumber": ["receiptNumber is required."] },
        errorCodes: { "confirmation.receiptNumber": ["required"] },
      }),
    });
    const { user } = renderApp(<DpsStatusSection />);

    await user.click(await screen.findByRole("button", { name: "Зберегти" }));

    expect(await screen.findByText("Заповніть це поле.")).toBeVisible();
    expect(screen.getByText("Перевірте введені дані:")).toBeVisible();
  });

  it("explains a receipt dated after today", async () => {
    stubFetch({
      [read]: { ...unconfirmed, confirmation: { confirmedOn: "2026-10-01", receiptNumber: "9154001234" } },
      [write]: reply(400, { code: "validation_failed", errorCodes: { "confirmation.confirmedOn": ["date_in_future"] } }),
    });
    const { user } = renderApp(<DpsStatusSection />);

    await user.click(await screen.findByRole("button", { name: "Зберегти" }));

    expect(await screen.findByText("Дата не може бути пізніше за сьогодні.")).toBeVisible();
  });

  it("reads the same in Russian, with the server's rejection of the start date", async () => {
    stubFetch({
      [read]: unconfirmed,
      [write]: reply(400, {
        code: "validation_failed",
        errorCodes: { group3Since: ["quarter_start_required"] },
      }),
    });
    const { user } = renderApp(<DpsStatusSection />, { locale: "ru" });

    expect(await screen.findByRole("checkbox", { name: "ФОП зарегистрирован" })).toBeVisible();
    expect(screen.getByRole("checkbox", { name: "Заявление о выборе 3 группы принято" })).toBeVisible();
    expect(screen.getByRole("radio", { name: "С даты регистрации (заявление подано в течение 10 дней)" })).toBeChecked();
    expect(screen.getByText(/^Заявление нужно подать до 8 /)).toBeVisible();

    await user.click(screen.getByRole("button", { name: "Сохранить" }));

    expect(
      await screen.findByText("Группа 3 может действовать только с даты регистрации или с первого дня более позднего квартала."),
    ).toBeVisible();
  });
});

describe("SettingsTabs", () => {
  // jsdom has no scrollIntoView, which the settings tabs call when the active tab changes.
  beforeEach(() => {
    Element.prototype.scrollIntoView = () => {};
  });
  afterEach(() => {
    Reflect.deleteProperty(Element.prototype, "scrollIntoView");
  });

  it("opens the DPS status tab from ?tab=dps", async () => {
    stubFetch({ [read]: unconfirmed, "GET /api/settings/treasury-accounts": [] });
    renderApp(<SettingsTabs initialTab="dps" />);

    expect(screen.getByRole("tab", { name: "Статус у ДПС" })).toHaveAttribute("aria-selected", "true");
    expect(await screen.findByRole("checkbox", { name: "ФОП зареєстровано" })).toBeVisible();
  });
});

describe("DpsStatusSection network failure", () => {
  it.each([
    ["uk", "ФОП зареєстровано", "Зберегти", "Не вдалося зберегти. Спробуйте ще раз. Немає зв'язку з сервером. Перевірте мережу й спробуйте ще раз."],
    ["ru", "ФОП зарегистрирован", "Сохранить", "Не удалось сохранить. Попробуйте ещё раз. Нет связи с сервером. Проверьте сеть и попробуйте ещё раз."],
  ] as const)("says so in %s when the save never reaches the api", async (locale, tick, button, message) => {
    stubFetch({
      [read]: unconfirmed,
      [write]: () => {
        throw new TypeError("Failed to fetch");
      },
    });
    const { user } = renderApp(<DpsStatusSection />, { locale });

    await user.click(await screen.findByRole("checkbox", { name: tick }));
    await user.click(screen.getByRole("button", { name: button }));

    expect(await screen.findByText(message)).toBeVisible();
  });
});
