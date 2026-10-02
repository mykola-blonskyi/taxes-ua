import { describe, expect, it } from "vitest";
import type { NotificationChannel, TelegramConnect } from "@/data/notifications/useNotificationChannels";
import { reply, renderApp, screen, stubFetch, useFakeTimers, waitFor } from "@/test/harness";
import { NotificationsSection } from "./NotificationsSection";

const email: NotificationChannel = {
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
const unlinked: NotificationChannel = { ...email, kind: "Telegram" };
const linked: NotificationChannel = { ...unlinked, linked: true, confirmed: true, enabled: true, linkedAt: "2026-09-01T09:00:00Z" };
const link: TelegramConnect = { url: "https://t.me/taxes_ua_bot?start=abc", expiresAt: "2026-10-02T12:00:00Z" };

const channels = "GET /api/notifications/channels" as const;
const connect = "POST /api/notifications/channels/telegram/connect" as const;
const toggle = "PUT /api/notifications/channels/telegram" as const;
const test = "POST /api/notifications/channels/telegram/test" as const;
const disconnect = "DELETE /api/notifications/channels/telegram" as const;

function renderSection(locale: "uk" | "ru" = "uk", userOptions = {}) {
  return renderApp(<NotificationsSection onEmailTokenSpent={() => {}} />, { locale }, userOptions);
}

describe("NotificationsSection: Telegram", () => {
  it("says Telegram is unavailable without a bot token on the server, and offers no connect", async () => {
    stubFetch({ [channels]: [{ ...unlinked, available: false }, email] });
    renderSection();

    expect(await screen.findByText("Telegram недоступний: на цьому сервері не задано токен бота.")).toBeVisible();
    expect(screen.queryByRole("button", { name: "Підключити Telegram" })).not.toBeInTheDocument();
  });

  it("says the channels could not be loaded", async () => {
    stubFetch({ [channels]: reply(500, { title: "Boom" }) });
    renderSection();

    expect(await screen.findByText("Не вдалося завантажити канали сповіщень.")).toBeVisible();
  });

  it("asks for a link when the owner connects, and shows it with its expiry", async () => {
    const api = stubFetch({ [channels]: [unlinked, email], [connect]: link });
    const { user } = renderSection();

    expect(await screen.findByText(/^Підключіть Telegram, і нагадування/)).toBeVisible();
    await user.click(screen.getByRole("button", { name: "Підключити Telegram" }));

    const open = await screen.findByRole("link", { name: "Відкрити бота в Telegram" });
    expect(open).toHaveAttribute("href", link.url);
    expect(open).toHaveAttribute("target", "_blank");
    expect(screen.getByText("Відкрийте посилання в Telegram і натисніть Start.")).toBeVisible();
    expect(screen.getByText(/^Посилання діє до .+ і спрацьовує один раз\.$/)).toBeVisible();
    expect(screen.getByRole("status")).toHaveTextContent("Чекаємо, поки ви натиснете Start…");
    expect(screen.getByRole("button", { name: "Отримати нове посилання" })).toBeVisible();
    expect(api.requestsTo(connect)).toHaveLength(1);
  });

  it("shows the channel linked once the owner presses Start in Telegram", async () => {
    // The channels are polled on an interval while the owner is away in Telegram.
    const timers = useFakeTimers(["setTimeout", "clearTimeout", "setInterval", "clearInterval", "Date"]);
    let stored = unlinked;
    const api = stubFetch({ [channels]: () => [stored, email], [connect]: link });
    const { user } = renderSection("uk", timers.userOptions);
    const settle = async (done: () => boolean) => {
      for (let step = 0; step < 200 && !done(); step++) {
        await timers.advance(1);
      }
    };
    await settle(() => screen.queryByRole("button", { name: "Підключити Telegram" }) !== null);
    await user.click(screen.getByRole("button", { name: "Підключити Telegram" }));
    await settle(() => screen.queryByRole("status") !== null);
    const reads = api.requestsTo(channels).length;

    stored = linked;
    await timers.advance(3_000);
    await settle(() => screen.queryByRole("checkbox", { name: "Надсилати нагадування в Telegram" }) !== null);

    expect(api.requestsTo(channels).length).toBeGreaterThan(reads);
    expect(screen.getByRole("checkbox", { name: "Надсилати нагадування в Telegram" })).toBeChecked();
    expect(screen.queryByRole("link", { name: "Відкрити бота в Telegram" })).not.toBeInTheDocument();
  });

  it("says the link could not be made", async () => {
    stubFetch({ [channels]: [unlinked, email], [connect]: reply(500, { title: "Boom" }) });
    const { user } = renderSection();

    await user.click(await screen.findByRole("button", { name: "Підключити Telegram" }));

    expect(await screen.findByText("Не вдалося отримати посилання. Спробуйте ще раз.")).toBeVisible();
    expect(screen.queryByRole("link", { name: "Відкрити бота в Telegram" })).not.toBeInTheDocument();
  });

  it("shows a linked channel on and switches it off", async () => {
    const api = stubFetch({ [channels]: [linked, email], [toggle]: {} });
    const { user } = renderSection();

    expect(await screen.findByText("Увімкнено")).toBeVisible();
    expect(screen.getByText(/^Підключено /)).toBeVisible();
    await user.click(screen.getByRole("checkbox", { name: "Надсилати нагадування в Telegram" }));

    await waitFor(() => expect(api.requestsTo(toggle)).toHaveLength(1));
    expect(api.requests.find((request) => request.method === "PUT")?.body).toEqual({ enabled: false });
  });

  it("shows a disabled channel as off, and turns it on", async () => {
    const api = stubFetch({ [channels]: [{ ...linked, enabled: false }, email], [toggle]: {} });
    const { user } = renderSection();

    expect(await screen.findByText("Вимкнено")).toBeVisible();
    const reminders = screen.getByRole("checkbox", { name: "Надсилати нагадування в Telegram" });
    expect(reminders).not.toBeChecked();
    await user.click(reminders);

    await waitFor(() => expect(api.requestsTo(toggle)).toHaveLength(1));
    expect(api.requests.find((request) => request.method === "PUT")?.body).toEqual({ enabled: true });
  });

  it("says a change was not saved when the switch fails", async () => {
    stubFetch({ [channels]: [linked, email], [toggle]: reply(500, { title: "Boom" }) });
    const { user } = renderSection();

    await user.click(await screen.findByRole("checkbox", { name: "Надсилати нагадування в Telegram" }));

    expect(await screen.findByText("Не вдалося зберегти зміну.")).toBeVisible();
  });

  it("shows the recorded failure and the hint to unblock the bot", async () => {
    stubFetch({ [channels]: [{ ...linked, lastFailure: "Blocked", lastFailureAt: "2026-09-02T10:00:00Z" }, email] });
    renderSection();

    const failure = await screen.findByRole("alert");
    expect(failure).toHaveTextContent(/^Доставка не вдалася .+: бота заблоковано\. Розблокуйте бота в Telegram, натисніть Start і знову ввімкніть канал\.$/);
  });

  it("shows another failure without the unblock hint", async () => {
    stubFetch({ [channels]: [{ ...linked, lastFailure: "RateLimited", lastFailureAt: "2026-09-02T10:00:00Z" }, email] });
    renderSection();

    const failure = await screen.findByRole("alert");
    expect(failure).toHaveTextContent(/: Telegram просить зачекати\.$/);
    expect(failure).not.toHaveTextContent("Розблокуйте бота");
  });

  it("sends a test message and says so, then reads the channel back", async () => {
    const api = stubFetch({ [channels]: [linked, email], [test]: {} });
    const { user } = renderSection();

    await user.click(await screen.findByRole("button", { name: "Надіслати тестове повідомлення" }));

    expect(await screen.findByText("Тестове повідомлення надіслано.")).toBeVisible();
    expect(api.requestsTo(test)).toHaveLength(1);
    await waitFor(() => expect(api.requestsTo(channels)).toHaveLength(2));
  });

  it("points to the recorded failure when the test message fails", async () => {
    stubFetch({ [channels]: [linked, email], [test]: reply(502, { title: "Bad gateway" }) });
    const { user } = renderSection();

    await user.click(await screen.findByRole("button", { name: "Надіслати тестове повідомлення" }));

    expect(await screen.findByText("Тестове повідомлення не надіслано. Причина показана вище.")).toBeVisible();
    expect(screen.queryByText("Тестове повідомлення надіслано.")).not.toBeInTheDocument();
  });

  it("disconnects, and offers to connect again once the channel is read back", async () => {
    let stored = linked;
    const api = stubFetch({
      [channels]: () => [stored, email],
      [disconnect]: () => {
        stored = unlinked;

        return reply(204);
      },
    });
    const { user } = renderSection();

    await user.click(await screen.findByRole("button", { name: "Відключити" }));

    expect(await screen.findByRole("button", { name: "Підключити Telegram" })).toBeVisible();
    expect(api.requestsTo(disconnect)).toHaveLength(1);
  });

  it("shows a linked channel and its failure in Russian", async () => {
    stubFetch({ [channels]: [{ ...linked, lastFailure: "Blocked", lastFailureAt: "2026-09-02T10:00:00Z" }, email] });
    renderSection("ru");

    expect(await screen.findByRole("checkbox", { name: "Отправлять напоминания в Telegram" })).toBeChecked();
    expect(screen.getByRole("alert")).toHaveTextContent(/: бот заблокирован\. Разблокируйте бота в Telegram, нажмите Start и снова включите канал\.$/);
    expect(screen.getByRole("button", { name: "Отправить тестовое сообщение" })).toBeVisible();
    expect(screen.getByRole("button", { name: "Отключить" })).toBeVisible();
  });

  it("offers to connect and says it is unavailable in Russian", async () => {
    stubFetch({ [channels]: [unlinked, { ...email, available: false }] });
    renderSection("ru");

    expect(await screen.findByRole("button", { name: "Подключить Telegram" })).toBeVisible();
    expect(screen.getByText("Почта недоступна: на этом сервере не заданы настройки SMTP.")).toBeVisible();
  });
});
