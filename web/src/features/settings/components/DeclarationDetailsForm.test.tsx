import { describe, expect, it } from "vitest";
import { renderApp, reply, screen, stubFetch, waitFor } from "@/test/harness";
import { DeclarationDetailsForm } from "./DeclarationDetailsForm";

const read = "GET /api/settings/declaration" as const;
const write = "PUT /api/settings/declaration" as const;

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
  confirmedEmail: null as string | null,
  missingDetails: [],
};

describe("DeclarationDetailsForm contacts", () => {
  it("leaves the report email empty, offers the notification address in one click, and saves the three fields", async () => {
    const api = stubFetch({
      [read]: { ...details, confirmedEmail: "fop@example.com" },
      [write]: { ...details, fullName: "Тестенко Тест Тестович", phone: "+380501234567", reportEmail: "fop@example.com" },
    });
    const { user } = renderApp(<DeclarationDetailsForm />);

    const email = await screen.findByLabelText("Пошта для звітності");
    expect(email).toHaveValue("");
    expect(email).toHaveAttribute("type", "email");
    await user.click(screen.getByRole("button", { name: "Взяти адресу для сповіщень: fop@example.com" }));
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
    stubFetch({
      [read]: { ...details, fullName: "Тестенко Тест Тестович", phone: "+380501234567", reportEmail: "reports@example.com", confirmedEmail: "fop@example.com" },
    });
    renderApp(<DeclarationDetailsForm />);

    const email = await screen.findByLabelText("Пошта для звітності");
    expect(email).toHaveValue("reports@example.com");
    expect(screen.getByText("Друкується в шапці декларації.")).toBeVisible();
    expect(screen.queryByRole("button", { name: /Взяти адресу для сповіщень/ })).not.toBeInTheDocument();
    expect(screen.getByLabelText("Телефон")).toHaveValue("+380501234567");
    expect(screen.getByLabelText("Повне ім’я для декларації")).toHaveValue("Тестенко Тест Тестович");
  });

  it("leaves the email empty when there is no confirmed address", async () => {
    stubFetch({ [read]: details });
    renderApp(<DeclarationDetailsForm />);

    expect(await screen.findByLabelText("Пошта для звітності")).toHaveValue("");
    expect(screen.queryByRole("button", { name: /Взяти адресу для сповіщень/ })).not.toBeInTheDocument();
  });

  it("words a rejected phone by its code and ties the message to the input", async () => {
    stubFetch({
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
