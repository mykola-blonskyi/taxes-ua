import { describe, expect, it } from "vitest";
import { renderApp, reply, screen, stubFetch } from "@/test/harness";
import { ClientsSection } from "./ClientsSection";

const list = "GET /api/clients" as const;
const create = "POST /api/clients" as const;

describe("ClientsSection rejection", () => {
  it("words a taken name and a bad email by their codes, not the api's English", async () => {
    stubFetch({
      [list]: [],
      [create]: reply(400, {
        title: "One or more validation errors occurred.",
        code: "validation_failed",
        errors: {
          name: ["A client with this name already exists."],
          email: ["email must be an address such as name@example.com."],
        },
        errorCodes: { name: ["name_taken"], email: ["email_invalid"] },
      }),
    });
    const { user } = renderApp(<ClientsSection />);

    await user.click(await screen.findByRole("button", { name: "Додати клієнта" }));
    await user.type(screen.getByLabelText(/^Назва/), "Acme");
    await user.click(screen.getByRole("button", { name: "Зберегти" }));

    expect(await screen.findByText("Клієнт із такою назвою вже є.")).toBeVisible();
    expect(screen.getByText("Вкажіть адресу пошти на кшталт name@example.com.")).toBeVisible();
    expect(screen.getByText("Перевірте позначені поля.")).toBeVisible();
    expect(screen.queryByText(/already exists/)).not.toBeInTheDocument();
  });

  it("appends the reason of a failure that is not about a field, by its code", async () => {
    stubFetch({
      [list]: [],
      [create]: reply(503, { title: "Boom.", code: "monobank_unavailable" }),
    });
    const { user } = renderApp(<ClientsSection />, { locale: "ru" });

    await user.click(await screen.findByRole("button", { name: "Добавить клиента" }));
    await user.type(screen.getByLabelText(/^Название/), "Acme");
    await user.click(screen.getByRole("button", { name: "Сохранить" }));

    expect(await screen.findByText("Не удалось сохранить: monobank сейчас недоступен.")).toBeVisible();
    expect(screen.queryByText(/Boom/)).not.toBeInTheDocument();
  });
});
