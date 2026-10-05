import { ThemeProvider } from "next-themes";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { meQueryKey } from "@/data/auth/useMe";
import { act, renderApp, router, screen, stubFetch, waitFor } from "@/test/harness";
import { ThemeToggle } from "./ThemeToggle";

import { AppearanceSync } from "./AppearanceSync";
import { LanguageToggle } from "./LanguageToggle";

const save = "PUT /api/settings/appearance" as const;
const read = "GET /api/settings" as const;

const stored = { locale: "uk", theme: "system" };

function signedIn(queryClient: { setQueryData: (key: readonly string[], data: unknown) => void }) {
  queryClient.setQueryData(meQueryKey, { email: "owner@example.com" });
}

function inTheme(node: React.ReactElement) {
  return <ThemeProvider attribute="class" defaultTheme="system" enableSystem>{node}</ThemeProvider>;
}

beforeEach(() => {
  window.localStorage.clear();
  document.cookie = "locale=; path=/; max-age=0";
  vi.stubGlobal(
    "matchMedia",
    (query: string) => ({ matches: false, media: query, addListener: vi.fn(), removeListener: vi.fn(), addEventListener: vi.fn(), removeEventListener: vi.fn() }),
  );
});

describe("the language menu", () => {
  it("sets the cookie and, signed in, saves the choice to the server without waiting for it", async () => {
    const fetchStub = stubFetch({ [save]: { ...stored, locale: "ru" } });
    const { user, queryClient } = renderApp(<LanguageToggle />);
    signedIn(queryClient);

    await user.click(screen.getByRole("button", { name: "Мова" }));
    await user.click(await screen.findByRole("menuitemradio", { name: "Русский" }));

    expect(document.cookie).toContain("locale=ru");
    expect(router.refresh).toHaveBeenCalled();
    await waitFor(() => expect(fetchStub.requestsTo(save)).toHaveLength(1));
    expect(fetchStub.requestsTo(save)[0].body).toEqual({ locale: "ru" });
  });

  it("works signed out: the cookie changes and nothing is sent", async () => {
    const fetchStub = stubFetch({});
    const { user } = renderApp(<LanguageToggle />);

    await user.click(screen.getByRole("button", { name: "Мова" }));
    await user.click(await screen.findByRole("menuitemradio", { name: "Русский" }));

    expect(document.cookie).toContain("locale=ru");
    expect(fetchStub.requests).toHaveLength(0);
  });

  it("keeps a choice the server did not take, to send it again on the next load", async () => {
    stubFetch({ [save]: { status: 500 } });
    const { user, queryClient } = renderApp(<LanguageToggle />);
    signedIn(queryClient);

    await user.click(screen.getByRole("button", { name: "Мова" }));
    await user.click(await screen.findByRole("menuitemradio", { name: "Русский" }));

    await waitFor(() => expect(window.localStorage.getItem("appearance-pending")).toBe('{"locale":"ru"}'));
  });
});

describe("the theme toggle", () => {
  it("saves the theme, including the literal system choice, when signed in", async () => {
    const fetchStub = stubFetch({ [save]: { ...stored, theme: "dark" } });
    const { user, queryClient } = renderApp(inTheme(<ThemeToggle />));
    signedIn(queryClient);

    await user.click(screen.getByRole("button", { name: "Тема" }));
    await user.click(await screen.findByRole("menuitemradio", { name: "Темна" }));

    await waitFor(() => expect(fetchStub.requestsTo(save)).toHaveLength(1));
    expect(fetchStub.requestsTo(save)[0].body).toEqual({ theme: "dark" });
    expect(window.localStorage.getItem("theme")).toBe("dark");
  });
});

describe("AppearanceSync", () => {
  function mount(settings: typeof stored) {
    const fetchStub = stubFetch({ [read]: settings, [save]: settings });
    const rendered = renderApp(inTheme(<AppearanceSync />));
    signedIn(rendered.queryClient);

    return { ...rendered, fetchStub };
  }

  it("seeds a new device from the server: its language and theme", async () => {
    mount({ locale: "ru", theme: "dark" });

    await waitFor(() => expect(document.cookie).toContain("locale=ru"));
    expect(router.refresh).toHaveBeenCalled();
    await waitFor(() => expect(window.localStorage.getItem("theme")).toBe("dark"));
  });

  it("leaves a device alone when it already matches the server", async () => {
    const { fetchStub } = mount(stored);

    await waitFor(() => expect(fetchStub.requestsTo(read)).toHaveLength(1));
    expect(router.refresh).not.toHaveBeenCalled();
    expect(fetchStub.requestsTo(save)).toHaveLength(0);
  });

  it("keeps a browser's own choice: nothing is taken from the server and nothing is sent", async () => {
    document.cookie = "locale=ru; path=/";
    window.localStorage.setItem("theme", "dark");

    const { fetchStub } = mount(stored);

    await waitFor(() => expect(fetchStub.requestsTo(read)).toHaveLength(1));
    await act(async () => {});
    expect(document.cookie).toContain("locale=ru");
    expect(window.localStorage.getItem("theme")).toBe("dark");
    expect(router.refresh).not.toHaveBeenCalled();
    expect(fetchStub.requestsTo(save)).toHaveLength(0);
  });

  it("sends a choice the server never acknowledged instead of taking the server's value", async () => {
    window.localStorage.setItem("appearance-pending", '{"locale":"ru"}');

    const { fetchStub } = mount(stored);

    await waitFor(() => expect(fetchStub.requestsTo(save)).toHaveLength(1));
    expect(fetchStub.requestsTo(save)[0].body).toEqual({ locale: "ru" });
    expect(document.cookie).not.toContain("locale=uk");
    await act(async () => {});
    expect(router.refresh).not.toHaveBeenCalled();
  });
});
