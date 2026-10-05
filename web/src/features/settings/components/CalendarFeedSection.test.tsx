import { describe, expect, it } from "vitest";
import { act, renderApp, reply, screen, stubFetch, waitFor } from "@/test/harness";
import { CalendarFeedSection } from "./CalendarFeedSection";

const feed = "GET /api/calendar/feed" as const;

describe("CalendarFeedSection", () => {
  it("keeps the create button when a background refetch fails after the feed loaded without a link", async () => {
    let calls = 0;
    stubFetch({ [feed]: () => (++calls === 1 ? { path: null } : reply(500, { title: "Boom" })) });
    const { queryClient } = renderApp(<CalendarFeedSection />);
    expect(await screen.findByRole("button", { name: "Створити посилання" })).toBeVisible();

    await act(() => queryClient.invalidateQueries());
    await waitFor(() => expect(queryClient.isFetching()).toBe(0));

    expect(calls).toBe(2);
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Створити посилання" })).toBeVisible();
  });

  it("shows the failure when the feed never loaded", async () => {
    stubFetch({ [feed]: reply(500, { title: "Boom" }) });
    renderApp(<CalendarFeedSection />);

    expect(await screen.findByRole("alert")).toHaveTextContent("Не вдалося завантажити посилання на календар.");
    expect(screen.queryByRole("button", { name: "Створити посилання" })).not.toBeInTheDocument();
  });
});
