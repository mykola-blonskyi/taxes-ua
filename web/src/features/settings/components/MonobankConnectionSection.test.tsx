import { describe, expect, it } from "vitest";
import { renderApp, reply, screen, stubFetch } from "@/test/harness";
import { MonobankConnectionSection } from "./MonobankConnectionSection";

const read = "GET /api/monobank/connection" as const;
const saveToken = "PUT /api/monobank/connection" as const;
const jar = "GET /api/monobank/reserve-jar" as const;

const disconnected = { connected: false, tokenRejectedAt: null, webhook: null, accounts: [] } as const;

describe("MonobankConnectionSection token rejection", () => {
  it("words a token the bank refused by its code", async () => {
    stubFetch({
      [read]: disconnected,
      [jar]: { jar: null },
      [saveToken]: reply(400, {
        code: "validation_failed",
        errors: { token: ["monobank rejected this token."] },
        errorCodes: { token: ["token_rejected"] },
      }),
    });
    const { user } = renderApp(<MonobankConnectionSection />);

    await user.type(await screen.findByLabelText(/^Особистий токен/), "bad-token");
    await user.click(screen.getByRole("button", { name: "Зберегти" }));

    expect(await screen.findByText("monobank відхилив цей токен.")).toBeVisible();
    expect(screen.queryByText(/rejected this token/)).not.toBeInTheDocument();
  });

  it("words a bank that cannot be reached, in Russian", async () => {
    stubFetch({
      [read]: disconnected,
      [jar]: { jar: null },
      [saveToken]: reply(502, { title: "monobank is temporarily unavailable.", code: "monobank_unavailable" }),
    });
    const { user } = renderApp(<MonobankConnectionSection />, { locale: "ru" });

    await user.type(await screen.findByLabelText(/^Личный токен/), "a-token");
    await user.click(screen.getByRole("button", { name: "Сохранить" }));

    expect(await screen.findByText("Не удалось сохранить. Проверьте токен. monobank сейчас недоступен.")).toBeVisible();
    expect(screen.queryByText(/temporarily unavailable/)).not.toBeInTheDocument();
  });
});
