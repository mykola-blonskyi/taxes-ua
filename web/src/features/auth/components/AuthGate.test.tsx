import { describe, expect, it } from "vitest";
import { reply, renderApp, router, screen, stubFetch, waitFor } from "@/test/harness";
import { AuthGate } from "./AuthGate";

const me = { id: "u1", email: "owner@example.com", displayName: null, createdAt: "2026-01-01T00:00:00Z" };

describe("AuthGate", () => {
  it("shows a loading screen, not a blank page, while the session is being checked", async () => {
    const { promise, resolve: release } = Promise.withResolvers<typeof me>();
    stubFetch({ "GET /api/auth/me": () => promise });
    renderApp(
      <AuthGate>
        <p>Кабінет</p>
      </AuthGate>,
    );

    expect(screen.getByRole("status")).toHaveTextContent("Завантаження…");
    expect(screen.queryByText("Кабінет")).not.toBeInTheDocument();

    release(me);
    expect(await screen.findByText("Кабінет")).toBeVisible();
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });

  it("says so in Russian", () => {
    stubFetch({ "GET /api/auth/me": () => new Promise<typeof me>(() => {}) });
    renderApp(<AuthGate>x</AuthGate>, { locale: "ru" });

    expect(screen.getByRole("status")).toHaveTextContent("Загрузка…");
  });

  it("keeps the loading screen up while a signed-out owner is sent to the login page", async () => {
    stubFetch({ "GET /api/auth/me": reply(401) });
    renderApp(<AuthGate>x</AuthGate>);

    await waitFor(() => expect(router.replace).toHaveBeenCalledWith("/login"));
    expect(screen.getByRole("status")).toHaveTextContent("Завантаження…");
  });
});
