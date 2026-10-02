import { describe, expect, it } from "vitest";
import { renderApp, reply, screen, stubFetch } from "@/test/harness";
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
  missingDetails: [],
} as const;

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
