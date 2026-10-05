import { describe, expect, it } from "vitest";
import { renderApp, reply, screen, stubFetch, waitFor } from "@/test/harness";
import { DeclarationDetailsForm } from "./DeclarationDetailsForm";

const read = "GET /api/settings/declaration" as const;
const write = "PUT /api/settings/declaration" as const;
const classes = "GET /api/settings/declaration/kved-classes" as const;
const channels = "GET /api/notifications/channels" as const;

const classifier = {
  [classes]: [
    { code: "62.01", name: "Комп'ютерне програмування" },
    { code: "62.02", name: "Консультування з питань інформатизації" },
    { code: "85.59", name: "Інші види освіти, н.в.і.у." },
  ],
  [channels]: [],
};

const telegram = { kind: "Telegram" as const, linked: true, confirmed: true, address: null };

const details = {
  name: "Іваненко Іван",
  rnokpp: "1234567890",
  taxOfficeRegion: 26,
  taxOfficeDistrict: 5,
  taxOfficeName: "ГУ ДПС",
  kvedCodes: ["62.01", "62.02"],
  address: "Київ",
  fullName: "",
  phone: "",
  reportEmail: "",
  missingDetails: [],
  unknownKvedCodes: [] as string[],
};

describe("DeclarationDetailsForm contacts", () => {
  it("leaves the report email empty, offers the confirmed email channel's address in one click, and saves the three fields", async () => {
    const api = stubFetch({
      ...classifier,
      [channels]: [telegram, { kind: "Email", linked: true, confirmed: true, address: "fop@example.com" }],
      [read]: details,
      [write]: { ...details, fullName: "Тестенко Тест Тестович", phone: "+380501234567", reportEmail: "fop@example.com" },
    });
    const { user } = renderApp(<DeclarationDetailsForm />);

    const email = await screen.findByLabelText("Пошта для звітності");
    expect(email).toHaveValue("");
    expect(email).toHaveAttribute("type", "email");
    await user.click(await screen.findByRole("button", { name: "Взяти адресу для сповіщень: fop@example.com" }));
    expect(email).toHaveValue("fop@example.com");
    expect(screen.queryByRole("button", { name: /Взяти адресу для сповіщень/ })).not.toBeInTheDocument();
    const phone = screen.getByLabelText("Телефон");
    expect(phone).toHaveAttribute("type", "tel");
    expect(phone).toHaveAttribute("autocomplete", "tel");
    expect(screen.getByText("Формат +380 XX XXX XX XX.")).toBeVisible();

    await user.type(screen.getByLabelText("Повне ім’я для декларації"), "Тестенко Тест Тестович");
    await user.type(phone, "050 123 45 67");
    await user.click(screen.getByRole("button", { name: "Зберегти" }));

    await waitFor(() => expect(api.requestsTo(write)).toHaveLength(1));
    expect(api.requestsTo(write)[0].body).toMatchObject({
      fullName: "Тестенко Тест Тестович",
      phone: "050 123 45 67",
      reportEmail: "fop@example.com",
    });
    expect(await screen.findByText("Збережено.")).toBeVisible();
    expect(screen.getByText("Друкується в шапці декларації.")).toBeVisible();
  });

  it("keeps the stored report email over the notification address", async () => {
    const api = stubFetch({
      ...classifier,
      [channels]: [{ kind: "Email", linked: true, confirmed: true, address: "fop@example.com" }],
      [read]: { ...details, fullName: "Тестенко Тест Тестович", phone: "+380501234567", reportEmail: "reports@example.com" },
    });
    renderApp(<DeclarationDetailsForm />);

    const email = await screen.findByLabelText("Пошта для звітності");
    expect(email).toHaveValue("reports@example.com");
    await waitFor(() => expect(api.requestsTo(channels)).toHaveLength(1));
    expect(screen.getByText("Друкується в шапці декларації.")).toBeVisible();
    expect(screen.queryByRole("button", { name: /Взяти адресу для сповіщень/ })).not.toBeInTheDocument();
    expect(screen.getByLabelText("Телефон")).toHaveValue("+380501234567");
    expect(screen.getByLabelText("Повне ім’я для декларації")).toHaveValue("Тестенко Тест Тестович");
  });

  it("offers no address the owner has not confirmed yet", async () => {
    const api = stubFetch({
      ...classifier,
      [channels]: [telegram, { kind: "Email", linked: true, confirmed: false, address: "fop@example.com" }],
      [read]: details,
    });
    renderApp(<DeclarationDetailsForm />);

    expect(await screen.findByLabelText("Пошта для звітності")).toHaveValue("");
    await waitFor(() => expect(api.requestsTo(channels)).toHaveLength(1));
    expect(screen.queryByRole("button", { name: /Взяти адресу для сповіщень/ })).not.toBeInTheDocument();
  });

  it("keeps the form usable without a suggestion when the channels fail to load", async () => {
    const api = stubFetch({ ...classifier, [channels]: reply(500, { code: "internal_error" }), [read]: details });
    renderApp(<DeclarationDetailsForm />);

    const email = await screen.findByLabelText("Пошта для звітності");
    await waitFor(() => expect(api.requestsTo(channels)).not.toHaveLength(0));
    expect(email).toHaveValue("");
    expect(email).toBeEnabled();
    expect(screen.queryByRole("button", { name: /Взяти адресу для сповіщень/ })).not.toBeInTheDocument();
  });

  it("words a rejected phone by its code and ties the message to the input", async () => {
    stubFetch({
      ...classifier,
      [read]: details,
      [write]: reply(400, { code: "validation_failed", errorCodes: { phone: ["phone_invalid"] } }),
    });
    const { user } = renderApp(<DeclarationDetailsForm />);

    await user.type(await screen.findByLabelText("Телефон"), "12345");
    await user.click(screen.getByRole("button", { name: "Зберегти" }));

    expect(await screen.findByText("Вкажіть український номер у форматі +380 XX XXX XX XX.")).toBeVisible();
  });

  it("words a rejected phone in Russian", async () => {
    stubFetch({
      ...classifier,
      [read]: details,
      [write]: reply(400, { code: "validation_failed", errorCodes: { phone: ["phone_invalid"] } }),
    });
    const { user } = renderApp(<DeclarationDetailsForm />, { locale: "ru" });

    await user.click(await screen.findByRole("button", { name: "Сохранить" }));

    expect(await screen.findByText("Укажите украинский номер в формате +380 XX XXX XX XX.")).toBeVisible();
  });
});

describe("DeclarationDetailsForm rejection", () => {
  it("words each rejected field by its code, including a KVED by its position", async () => {
    stubFetch({
      ...classifier,
      [read]: details,
      [write]: reply(400, {
        code: "validation_failed",
        errors: {
          "kvedCodes[1]": ["A KVED code must not repeat."],
          taxOfficeRegion: ["taxOfficeRegion must be 1 to 99."],
        },
        errorCodes: { "kvedCodes[1]": ["kved_duplicate"], taxOfficeRegion: ["tax_office_region_range"] },
      }),
    });
    const { user } = renderApp(<DeclarationDetailsForm />);

    await user.click(await screen.findByRole("button", { name: "Зберегти" }));

    expect(await screen.findByText("Цей КВЕД уже є у списку.")).toBeVisible();
    expect(screen.getByText("Код області — число від 1 до 99.")).toBeVisible();
    expect(screen.queryByText(/must not repeat/)).not.toBeInTheDocument();
  });

  it("words an over-long address and a stray control character in Russian", async () => {
    stubFetch({
      ...classifier,
      [read]: details,
      [write]: reply(400, {
        code: "validation_failed",
        errorCodes: { address: ["too_long", "control_character"] },
      }),
    });
    const { user } = renderApp(<DeclarationDetailsForm />, { locale: "ru" });

    await user.click(await screen.findByRole("button", { name: "Сохранить" }));

    expect(await screen.findByText("Текст слишком длинный.")).toBeVisible();
    expect(screen.getByText("Текст содержит недопустимый символ.")).toBeVisible();
  });
});

describe.each([
  { locale: "uk" as const, save: "Зберегти", unknown: "Такого коду немає в КВЕД ДК 009:2010.", label: "КВЕД 1 (основний)", second: "КВЕД 2", complete: "Усе потрібне для декларації заповнено.", unknownBanner: "У класифікаторі КВЕД ДК 009:2010 немає: 12.34. Виправте ці коди." },
  { locale: "ru" as const, save: "Сохранить", unknown: "Такого кода нет в КВЭД ДК 009:2010.", label: "КВЭД 1 (основной)", second: "КВЭД 2", complete: "Всё нужное для декларации заполнено.", unknownBanner: "В классификаторе КВЭД ДК 009:2010 нет: 12.34. Исправьте эти коды." },
])("DeclarationDetailsForm KVED names in $locale", ({ locale, unknown, label, second, complete, unknownBanner }) => {
  it("shows the Ukrainian name of every saved code under its input", async () => {
    stubFetch({ ...classifier, [read]: details });
    renderApp(<DeclarationDetailsForm />, { locale });

    await screen.findByText("Комп'ютерне програмування");
    expect(screen.getByLabelText(label)).toHaveAccessibleDescription("Комп'ютерне програмування");
    expect(screen.getByLabelText(second)).toHaveAccessibleDescription("Консультування з питань інформатизації");
  });

  it("shows the name of a code as it is typed and nothing for an unfinished one", async () => {
    stubFetch({ ...classifier, [read]: { ...details, kvedCodes: [] } });
    const { user } = renderApp(<DeclarationDetailsForm />, { locale });
    const input = await screen.findByLabelText(label);

    await user.type(input, "85.5");
    expect(screen.queryByText("Інші види освіти, н.в.і.у.")).not.toBeInTheDocument();
    expect(screen.queryByText(unknown)).not.toBeInTheDocument();

    await user.type(input, "9");
    expect(screen.getByText("Інші види освіти, н.в.і.у.")).toBeVisible();
    expect(input).not.toHaveAttribute("aria-invalid");
  });

  it("words a code of the right shape that is not a class as the owner types it", async () => {
    stubFetch({ ...classifier, [read]: { ...details, kvedCodes: [] } });
    const { user } = renderApp(<DeclarationDetailsForm />, { locale });
    const input = await screen.findByLabelText(label);

    await user.type(input, "12.34");

    expect(screen.getByText(unknown)).toBeVisible();
    expect(input).toHaveAttribute("aria-invalid", "true");
  });

  it("flags a saved code the classifier does not know on load and does not call the details complete", async () => {
    stubFetch({ ...classifier, [read]: { ...details, kvedCodes: ["62.01", "12.34"], unknownKvedCodes: ["12.34"] } });
    renderApp(<DeclarationDetailsForm />, { locale });

    expect(await screen.findByText(unknown)).toBeVisible();
    expect(screen.getByLabelText(second)).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByLabelText(label)).not.toHaveAttribute("aria-invalid");
    expect(screen.getByText(unknownBanner)).toBeVisible();
    expect(screen.queryByText(complete)).not.toBeInTheDocument();
  });

  it("shows no name and no error until the classifier has loaded, then names the saved code", async () => {
    let release: () => void = () => {};
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    stubFetch({
      [read]: details,
      [channels]: [],
      [classes]: async () => {
        await gate;

        return classifier[classes];
      },
    });
    renderApp(<DeclarationDetailsForm />, { locale });

    const input = await screen.findByLabelText(label);
    expect(input).toHaveValue("62.01");
    expect(screen.queryByText("Комп'ютерне програмування")).not.toBeInTheDocument();

    release();
    expect(await screen.findByText("Комп'ютерне програмування")).toBeVisible();
  });

  it("says the names could not be loaded and keeps the form usable", async () => {
    stubFetch({ [read]: details, [channels]: [], [classes]: reply(500, { code: "internal_error" }) });
    renderApp(<DeclarationDetailsForm />, { locale });

    expect(await screen.findByRole("alert")).toBeVisible();
    expect(screen.getByLabelText(label)).toHaveValue("62.01");
  });
});
