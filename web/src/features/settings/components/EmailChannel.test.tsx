import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { NotificationChannel } from "@/data/notifications/useNotificationChannels";
import { reply, renderApp, screen, stubFetch, waitFor, within } from "@/test/harness";
import { EmailChannel } from "./EmailChannel";
import { NotificationsSection } from "./NotificationsSection";
import { SettingsTabs } from "./SettingsTabs";

const none: NotificationChannel = {
  kind: "Email",
  available: true,
  linked: false,
  confirmed: false,
  enabled: false,
  address: null,
  linkedAt: null,
  lastDeliveryAt: null,
  lastFailure: null,
  lastFailureAt: null,
};
const pending: NotificationChannel = { ...none, linked: true, address: "owner@example.com" };
const confirmed: NotificationChannel = { ...pending, confirmed: true, enabled: true, linkedAt: "2026-09-01T09:00:00Z" };
const telegram: NotificationChannel = { ...none, kind: "Telegram" };

const add = "POST /api/notifications/channels/email" as const;
const confirm = "POST /api/notifications/channels/email/confirm" as const;
const resend = "POST /api/notifications/channels/email/resend" as const;
const test = "POST /api/notifications/channels/email/test" as const;
const toggle = "PUT /api/notifications/channels/email" as const;
const remove = "DELETE /api/notifications/channels/email" as const;
const channels = "GET /api/notifications/channels" as const;

function renderChannel(channel: NotificationChannel, confirmToken?: string, locale: "uk" | "ru" = "uk") {
  const onTokenSpent = vi.fn();
  const view = renderApp(<EmailChannel channel={channel} confirmToken={confirmToken} onTokenSpent={onTokenSpent} />, { locale });

  return { ...view, onTokenSpent };
}

describe("EmailChannel", () => {
  describe("without a server mail setup", () => {
    it("says mail is unavailable and offers no form", () => {
      stubFetch({});
      renderChannel({ ...none, available: false });

      expect(screen.getByText("Пошта недоступна: на цьому сервері не задано налаштування SMTP.")).toBeVisible();
      expect(screen.queryByLabelText("Адреса електронної пошти")).not.toBeInTheDocument();
    });
  });

  describe("adding an address", () => {
    it("sends the address typed, only once there is one to send", async () => {
      const api = stubFetch({ [add]: {} });
      const { user } = renderChannel(none);
      const send = screen.getByRole("button", { name: "Надіслати підтвердження" });
      expect(send).toBeDisabled();

      await user.type(screen.getByLabelText("Адреса електронної пошти"), "owner@example.com");
      await user.click(send);

      await waitFor(() => expect(api.requestsTo(add)).toHaveLength(1));
      expect(api.requests[0]).toMatchObject({ method: "POST", path: "/api/notifications/channels/email", body: { address: "owner@example.com" } });
    });

    it("shows the address as waiting for confirmation once the channel is read back", async () => {
      let stored: NotificationChannel = none;
      const api = stubFetch({
        [channels]: () => [telegram, stored],
        [add]: (request) => {
          stored = { ...pending, address: (request.body as { address: string }).address };

          return {};
        },
      });
      const { user } = renderApp(<NotificationsSection onEmailTokenSpent={() => {}} />);

      await user.type(await screen.findByLabelText("Адреса електронної пошти"), "owner@example.com");
      await user.click(screen.getByRole("button", { name: "Надіслати підтвердження" }));

      expect(await screen.findByText("Очікує підтвердження")).toBeVisible();
      expect(screen.getByText("owner@example.com")).toBeVisible();
      expect(screen.getByText(/Ми надіслали лист із посиланням для підтвердження/)).toBeVisible();
      expect(screen.queryByLabelText("Адреса електронної пошти")).not.toBeInTheDocument();
      expect(api.requestsTo(channels).length).toBeGreaterThan(1);
    });

    it("says what is wrong with an address the api rejects", async () => {
      stubFetch({ [add]: reply(400, { title: "Invalid" }) });
      const { user } = renderChannel(none);

      await user.type(screen.getByLabelText("Адреса електронної пошти"), "owner@localhost");
      await user.click(screen.getByRole("button", { name: "Надіслати підтвердження" }));

      expect(await screen.findByText("Вкажіть звичайну адресу, наприклад name@example.com.")).toBeVisible();
      expect(screen.getByLabelText("Адреса електронної пошти")).toHaveValue("owner@localhost");
    });

    it("says the letter could not be sent after any other failure", async () => {
      stubFetch({ [add]: reply(500, { title: "Boom" }) });
      const { user } = renderChannel(none);

      await user.type(screen.getByLabelText("Адреса електронної пошти"), "owner@example.com");
      await user.click(screen.getByRole("button", { name: "Надіслати підтвердження" }));

      expect(
        await screen.findByText("Не вдалося надіслати лист із підтвердженням. Причина показана нижче, коли адресу збережено."),
      ).toBeVisible();
      expect(screen.queryByText("Вкажіть звичайну адресу, наприклад name@example.com.")).not.toBeInTheDocument();
    });
  });

  describe("an address waiting for confirmation", () => {
    it("resends the link and says so", async () => {
      const api = stubFetch({ [resend]: {} });
      const { user } = renderChannel(pending);

      await user.click(screen.getByRole("button", { name: "Надіслати посилання ще раз" }));

      expect(await screen.findByRole("status")).toHaveTextContent("Лист із новим посиланням надіслано.");
      expect(api.requestsTo(resend)).toHaveLength(1);
    });

    it("says when the resend fails", async () => {
      stubFetch({ [resend]: reply(500, { title: "Boom" }) });
      const { user } = renderChannel(pending);

      await user.click(screen.getByRole("button", { name: "Надіслати посилання ще раз" }));

      expect(await screen.findByText("Не вдалося надіслати лист. Причина показана вище.")).toBeVisible();
    });

    it("shows why the last letter did not arrive", () => {
      stubFetch({});
      renderChannel({ ...pending, lastFailure: "Rejected", lastFailureAt: "2026-09-02T10:00:00Z" });

      expect(screen.getByRole("alert")).toHaveTextContent(/^Доставка не вдалася .+: сервер пошти відхилив лист або адресу\.$/);
    });

    it("removes the address", async () => {
      const api = stubFetch({ [remove]: reply(204) });
      const { user } = renderChannel(pending);

      await user.click(screen.getByRole("button", { name: "Видалити адресу" }));

      await waitFor(() => expect(api.requestsTo(remove)).toHaveLength(1));
    });
  });

  describe("a confirmed address", () => {
    it("shows the address on and switches it off", async () => {
      const api = stubFetch({ [toggle]: {} });
      const { user } = renderChannel(confirmed);

      expect(screen.getByText("Увімкнено")).toBeVisible();
      expect(screen.getByText("owner@example.com")).toBeVisible();
      const reminders = screen.getByRole("checkbox", { name: "Надсилати нагадування на пошту" });
      expect(reminders).toBeChecked();

      await user.click(reminders);

      await waitFor(() => expect(api.requestsTo(toggle)).toHaveLength(1));
      expect(api.requests[0].body).toEqual({ enabled: false });
    });

    it("shows a disabled address as off", () => {
      stubFetch({});
      renderChannel({ ...confirmed, enabled: false });

      expect(screen.getByText("Вимкнено")).toBeVisible();
      expect(screen.getByRole("checkbox", { name: "Надсилати нагадування на пошту" })).not.toBeChecked();
    });

    it("sends a test letter and says so", async () => {
      const api = stubFetch({ [test]: {} });
      const { user } = renderChannel(confirmed);

      await user.click(screen.getByRole("button", { name: "Надіслати тестовий лист" }));

      expect(await screen.findByRole("status")).toHaveTextContent("Тестовий лист надіслано.");
      expect(api.requestsTo(test)).toHaveLength(1);
    });

    it("points to the recorded failure when the test letter fails", async () => {
      stubFetch({ [test]: reply(502, { title: "Bad gateway" }) });
      const { user } = renderChannel({ ...confirmed, lastFailure: "Authentication", lastFailureAt: "2026-09-02T10:00:00Z" });

      await user.click(screen.getByRole("button", { name: "Надіслати тестовий лист" }));

      expect(await screen.findByText("Тестовий лист не надіслано. Причина показана вище.")).toBeVisible();
      expect(screen.getByRole("alert")).toHaveTextContent(/сервер пошти не прийняв логін або пароль\.$/);
      expect(screen.queryByText("Тестовий лист надіслано.")).not.toBeInTheDocument();
    });

    it("says a change was not saved when the switch fails", async () => {
      stubFetch({ [toggle]: reply(500, { title: "Boom" }) });
      const { user } = renderChannel(confirmed);

      await user.click(screen.getByRole("checkbox", { name: "Надсилати нагадування на пошту" }));

      expect(await screen.findByText("Не вдалося зберегти зміну.")).toBeVisible();
    });

    it("shows the last delivery and the last failure", () => {
      stubFetch({});
      renderChannel({
        ...confirmed,
        lastDeliveryAt: "2026-09-01T10:00:00Z",
        lastFailure: "Timeout",
        lastFailureAt: "2026-09-02T10:00:00Z",
      });

      expect(screen.getByText(/^Остання доставка: /)).toBeVisible();
      expect(screen.getByRole("alert")).toHaveTextContent(
        /сервер пошти не відповів вчасно, лист міг дійти, тож його не надсилали повторно\.$/,
      );
    });
  });

  describe("the confirmation link", () => {
    // jsdom has no scrollIntoView, which the settings tabs call when the active tab changes.
    beforeEach(() => {
      Element.prototype.scrollIntoView = () => {};
    });
    afterEach(() => {
      window.history.replaceState(null, "", "/");
      Reflect.deleteProperty(Element.prototype, "scrollIntoView");
    });

    it("posts the token once, tells the parent it is spent and takes it out of the address bar", async () => {
      window.history.replaceState(null, "", "/settings?tab=notifications&confirmEmail=tok-1");
      const api = stubFetch({ [confirm]: { address: "owner@example.com" } });
      const { onTokenSpent } = renderChannel(pending, "tok-1");

      expect(await screen.findByRole("status")).toHaveTextContent("Адресу owner@example.com підтверджено. Нагадування надходитимуть на неї.");
      expect(api.requestsTo(confirm)).toHaveLength(1);
      expect(api.requests[0].body).toEqual({ token: "tok-1" });
      expect(onTokenSpent).toHaveBeenCalledTimes(1);
      expect(window.location.search).toBe("?tab=notifications");
    });

    it("posts the token once even when the parent re-renders with a new callback", async () => {
      const api = stubFetch({ [confirm]: { address: "owner@example.com" } });
      const { rerender } = renderChannel(pending, "tok-1");
      await screen.findByRole("status");

      rerender(<EmailChannel channel={pending} confirmToken="tok-1" onTokenSpent={() => {}} />);
      await screen.findByRole("status");

      expect(api.requestsTo(confirm)).toHaveLength(1);
    });

    it("says the address is being confirmed while the api answers", async () => {
      let answer: (value: object) => void = () => {};
      stubFetch({ [confirm]: () => new Promise<object>((resolve) => (answer = resolve)) });
      renderChannel(pending, "tok-1");

      expect(await screen.findByText("Підтверджуємо адресу…")).toBeVisible();
      answer({ address: "owner@example.com" });

      await waitFor(() => expect(screen.queryByText("Підтверджуємо адресу…")).not.toBeInTheDocument());
    });

    it.each([
      [400, "Посилання недійсне. Відкрийте його з листа повністю або надішліть нове."],
      [410, "Посилання застаріло або адресу вже змінено. Надішліть нове посилання нижче."],
      [500, "Не вдалося підтвердити адресу. Спробуйте ще раз."],
    ])("explains a %i answer", async (status, message) => {
      stubFetch({ [confirm]: reply(status, { title: "Nope" }) });
      renderChannel(pending, "tok-1");

      expect(await screen.findByRole("alert")).toHaveTextContent(message);
    });

    it("spends a token once even after the owner switches settings tabs and comes back", async () => {
      const api = stubFetch({
        [channels]: [telegram, pending],
        [confirm]: { address: "owner@example.com" },
        "GET /api/settings/treasury-accounts": [],
        "GET /api/calendar/feed": {},
      });
      const { user } = renderApp(<SettingsTabs initialTab="notifications" confirmEmailToken="tok-1" />);
      await waitFor(() => expect(screen.getByRole("status")).toHaveTextContent("Адресу owner@example.com підтверджено"));

      await user.click(screen.getByRole("tab", { name: "Рахунки казначейства" }));
      await user.click(screen.getByRole("tab", { name: "Сповіщення" }));
      expect(await screen.findByText("Очікує підтвердження")).toBeVisible();

      expect(api.requestsTo(confirm)).toHaveLength(1);
      expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    });
  });

  describe("in Russian", () => {
    it("shows the form, the failures and the confirmation in Russian", async () => {
      stubFetch({ [add]: reply(400, { title: "Invalid" }), [confirm]: reply(410, { title: "Gone" }) });
      const { user } = renderChannel(none, "tok-1", "ru");

      expect(await screen.findByRole("alert")).toHaveTextContent(
        "Ссылка устарела или адрес уже изменён. Отправьте новую ссылку ниже.",
      );
      await user.type(screen.getByLabelText("Адрес электронной почты"), "owner@localhost");
      await user.click(screen.getByRole("button", { name: "Отправить подтверждение" }));

      expect(await screen.findByText("Укажите обычный адрес, например name@example.com.")).toBeVisible();
    });

    it("shows a confirmed address and its recorded failure in Russian", () => {
      stubFetch({});
      renderChannel({ ...confirmed, lastFailure: "Blocked", lastFailureAt: "2026-09-02T10:00:00Z" }, undefined, "ru");

      expect(screen.getByRole("checkbox", { name: "Отправлять напоминания на почту" })).toBeChecked();
      expect(screen.getByRole("button", { name: "Отправить тестовое письмо" })).toBeVisible();
      const failure = screen.getByRole("alert");
      expect(within(failure).getByText(/^Доставка не удалась .+: адрес заблокирован\.$/)).toBeVisible();
    });
  });
});
