import { describe, expect, it } from "vitest";
import { renderApp, screen, stubFetch, waitFor } from "@/test/harness";
import { CalendarFeedSection } from "./CalendarFeedSection";

const feed = "GET /api/calendar/feed" as const;
const rotate = "POST /api/calendar/feed/rotate" as const;

const secret = "a".repeat(64);
const link = { path: `/api/calendar/feed/${secret}.ics`, createdAt: "2026-10-05T09:00:00Z" };

describe("CalendarFeedSection", () => {
  it("shows a created link once, with a note to copy it now", async () => {
    const api = stubFetch({ [feed]: { createdAt: null }, [rotate]: link });
    const { user } = renderApp(<CalendarFeedSection />);

    await user.click(await screen.findByRole("button", { name: "Створити посилання" }));

    expect(await screen.findByText(`${window.location.origin}${link.path}`)).toBeVisible();
    expect(screen.getByText(/показується лише один раз/)).toBeVisible();
    expect(screen.getByRole("link", { name: "Підписатися в календарі" })).toHaveAttribute(
      "href",
      expect.stringMatching(new RegExp(`^webcal:.*${secret}\\.ics$`)),
    );
    expect(api.requestsTo(rotate)).toHaveLength(1);
  });

  it("says when an existing link was made and offers to rotate it, without ever showing it", async () => {
    stubFetch({ [feed]: { createdAt: "2026-10-01T09:00:00Z" }, [rotate]: link });
    const { user } = renderApp(<CalendarFeedSection />);

    expect(await screen.findByText(/^Посилання створено .*Щоб побачити посилання знову, оновіть його/)).toBeVisible();
    expect(screen.queryByRole("link", { name: "Підписатися в календарі" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Створити посилання" })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Оновити посилання" }));
    await user.click(screen.getByRole("button", { name: "Так, оновити" }));

    expect(await screen.findByText(`${window.location.origin}${link.path}`)).toBeVisible();
    await waitFor(() => expect(screen.queryByText(/^Посилання створено/)).not.toBeInTheDocument());
  });

  it("speaks Russian", async () => {
    stubFetch({ [feed]: { createdAt: "2026-10-01T09:00:00Z" } });
    renderApp(<CalendarFeedSection />, { locale: "ru" });

    expect(await screen.findByText(/^Ссылка создана .*Чтобы увидеть ссылку снова, обновите её/)).toBeVisible();
  });
});
