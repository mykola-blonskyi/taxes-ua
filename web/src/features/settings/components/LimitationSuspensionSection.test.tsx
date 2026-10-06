import { describe, expect, it } from "vitest";
import { renderApp, reply, screen, stubFetch } from "@/test/harness";
import { LimitationSuspensionSection } from "./LimitationSuspensionSection";

const me = "GET /api/auth/me" as const;
const read = "GET /api/limitation-suspension" as const;
const write = "PUT /api/limitation-suspension" as const;

const admin = { id: "u1", email: "owner@example.com", displayName: null, createdAt: "2026-01-01T00:00:00Z", isAdmin: true };
const member = { ...admin, id: "u2", isAdmin: false };
const open = { start: "2022-03-17", end: null, source: "Податковий кодекс, п. 69.36" };

const copy = {
  uk: { start: "Перший день", end: "Останній день", source: "Джерело", save: "Зберегти", title: "Воєнний стан: призупинення строків давності" },
  ru: { start: "Первый день", end: "Последний день", source: "Источник", save: "Сохранить", title: "Военное положение: приостановление сроков давности" },
} as const;

describe.each(["uk", "ru"] as const)("LimitationSuspensionSection in %s", (locale) => {
  const words = copy[locale];

  it("shows the stored suspension, the end empty while it lasts", async () => {
    stubFetch({ [me]: admin, [read]: open });
    renderApp(<LimitationSuspensionSection />, { locale });

    expect(await screen.findByLabelText(words.start)).toHaveValue("2022-03-17");
    expect(screen.getByRole("heading", { name: words.title })).toBeVisible();
    expect(screen.getByLabelText(words.end)).toHaveValue("");
    expect(screen.getByLabelText(words.source)).toHaveValue("Податковий кодекс, п. 69.36");
  });

  it("saves the edited stretch, an empty end as null", async () => {
    const api = stubFetch({ [me]: admin, [read]: open, [write]: { ...open, end: "2027-06-30" } });
    const { user } = renderApp(<LimitationSuspensionSection />, { locale });

    await user.type(await screen.findByLabelText(words.end), "2027-06-30");
    await user.click(screen.getByRole("button", { name: words.save }));

    await screen.findByRole("button", { name: words.save });
    expect(api.requestsTo(write).map((request) => request.body)).toEqual([{ ...open, end: "2027-06-30" }]);

    await user.clear(screen.getByLabelText(words.end));
    await user.click(screen.getByRole("button", { name: words.save }));
    await screen.findByRole("button", { name: words.save });
    expect(api.requestsTo(write)[1]?.body).toEqual({ ...open, end: null });
  });
});

describe("LimitationSuspensionSection", () => {
  it("shows the field error when the end is before the start", async () => {
    stubFetch({
      [me]: admin,
      [read]: open,
      [write]: reply(400, { code: "validation_failed", errorCodes: { end: ["suspension_end_before_start"] } }),
    });
    const { user } = renderApp(<LimitationSuspensionSection />);

    await user.type(await screen.findByLabelText("Останній день"), "2022-01-01");
    await user.click(screen.getByRole("button", { name: "Зберегти" }));

    expect(await screen.findByText("Кінець не може бути раніше за початок.")).toBeVisible();
  });

  it("shows the refusal to a member who is not an admin, and offers no save", async () => {
    stubFetch({ [me]: member, [read]: open });
    renderApp(<LimitationSuspensionSection />);

    expect(await screen.findByLabelText("Перший день")).toBeDisabled();
    expect(screen.queryByRole("button", { name: "Зберегти" })).toBeNull();
    expect(screen.getByText(/Змінює лише адміністратор/)).toBeVisible();
  });

  it("shows the 403 the same way the tax years do", async () => {
    stubFetch({ [me]: admin, [read]: open, [write]: reply(403, { code: "admin_required" }) });
    const { user } = renderApp(<LimitationSuspensionSection />);

    await user.click(await screen.findByRole("button", { name: "Зберегти" }));

    expect(await screen.findByText(/Не вдалося зберегти/)).toBeVisible();
  });
});
