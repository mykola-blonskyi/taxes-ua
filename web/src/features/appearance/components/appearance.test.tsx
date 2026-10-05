import { ThemeProvider } from "next-themes";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { meQueryKey } from "@/data/auth/useMe";
import { resolve } from "@/data/settings/appearance";
import { settingsQueryKey } from "@/data/settings/useSettings";
import { act, renderApp, router, screen, stubFetch, waitFor } from "@/test/harness";
import { AppearanceSync } from "./AppearanceSync";
import { LanguageToggle } from "./LanguageToggle";
import { ThemeToggle } from "./ThemeToggle";

const save = "PUT /api/settings/appearance" as const;
const read = "GET /api/settings" as const;

const earlier = "2026-10-05T09:00:00.000Z";
const later = "2026-10-05T10:00:00.000Z";

const stored = { locale: "uk", localeChosenAt: earlier, theme: "system", themeChosenAt: earlier };

function signedIn(queryClient: { setQueryData: (key: readonly string[], data: unknown) => void }) {
  queryClient.setQueryData(meQueryKey, { email: "owner@example.com" });
}

function inTheme(node: React.ReactElement) {
  return <ThemeProvider attribute="class" defaultTheme="system" enableSystem>{node}</ThemeProvider>;
}

function cookie(name: string) {
  return document.cookie.split("; ").find((entry) => entry.startsWith(`${name}=`))?.slice(name.length + 1);
}

function chooseLocaleInBrowser(value: string, chosenAt?: string) {
  document.cookie = `locale=${value}; path=/`;
  if (chosenAt) {
    document.cookie = `locale-chosen-at=${encodeURIComponent(chosenAt)}; path=/`;
  }
}

beforeEach(() => {
  window.localStorage.clear();
  document.cookie = "locale=; path=/; max-age=0";
  document.cookie = "locale-chosen-at=; path=/; max-age=0";
  vi.stubGlobal(
    "matchMedia",
    (query: string) => ({ matches: false, media: query, addListener: vi.fn(), removeListener: vi.fn(), addEventListener: vi.fn(), removeEventListener: vi.fn() }),
  );
});

describe("which choice stands", () => {
  const server = { value: "uk", chosenAt: earlier };

  it.each([
    ["a browser with no choice takes the server's", null, "take"],
    ["a browser choice without a time is the oldest", { value: "ru", chosenAt: null }, "take"],
    ["an older browser choice takes the server's", { value: "ru", chosenAt: "2026-10-05T08:00:00.000Z" }, "take"],
    ["a newer browser choice is sent", { value: "ru", chosenAt: later }, "send"],
    ["the same moment on both sides is left alone", { value: "uk", chosenAt: earlier }, "keep"],
  ] as const)("%s", (_name, browser, expected) => {
    expect(resolve(browser, server)).toBe(expected);
  });

  it("leaves two choices without a time alone, as before the times were kept", () => {
    expect(resolve({ value: "ru", chosenAt: null }, { value: "uk", chosenAt: null })).toBe("keep");
  });
});

describe("the language menu", () => {
  it("sets the cookie with its time and, signed in, saves both without waiting", async () => {
    const fetchStub = stubFetch({ [save]: (request) => ({ ...stored, locale: "ru", localeChosenAt: (request.body as { chosenAt: string }).chosenAt }) });
    const { user, queryClient } = renderApp(<LanguageToggle />);
    signedIn(queryClient);

    await user.click(screen.getByRole("button", { name: "Мова" }));
    await user.click(await screen.findByRole("menuitemradio", { name: "Русский" }));

    expect(cookie("locale")).toBe("ru");
    const chosenAt = decodeURIComponent(cookie("locale-chosen-at")!);
    expect(Date.now() - Date.parse(chosenAt)).toBeLessThan(5_000);
    expect(router.refresh).toHaveBeenCalled();
    await waitFor(() => expect(fetchStub.requestsTo(save)).toHaveLength(1));
    expect(fetchStub.requestsTo(save)[0].body).toEqual({ locale: "ru", chosenAt });
  });

  it("works signed out: the cookie and its time change and nothing is sent", async () => {
    const fetchStub = stubFetch({});
    const { user } = renderApp(<LanguageToggle />);

    await user.click(screen.getByRole("button", { name: "Мова" }));
    await user.click(await screen.findByRole("menuitemradio", { name: "Русский" }));

    expect(cookie("locale")).toBe("ru");
    expect(cookie("locale-chosen-at")).toBeDefined();
    expect(fetchStub.requests).toHaveLength(0);
  });

  it("takes the server's time when it holds the choice at another one, so the next load does not send it again", async () => {
    stubFetch({ [save]: { ...stored, locale: "ru", localeChosenAt: later } });
    const { user, queryClient } = renderApp(<LanguageToggle />);
    signedIn(queryClient);

    await user.click(screen.getByRole("button", { name: "Мова" }));
    await user.click(await screen.findByRole("menuitemradio", { name: "Русский" }));

    await waitFor(() => expect(decodeURIComponent(cookie("locale-chosen-at")!)).toBe(later));
  });
});

describe("the theme toggle", () => {
  it("saves the theme with its time, including the literal system choice, when signed in", async () => {
    const fetchStub = stubFetch({ [save]: (request) => ({ ...stored, theme: "dark", themeChosenAt: (request.body as { chosenAt: string }).chosenAt }) });
    const { user, queryClient } = renderApp(inTheme(<ThemeToggle />));
    signedIn(queryClient);

    await user.click(screen.getByRole("button", { name: "Тема" }));
    await user.click(await screen.findByRole("menuitemradio", { name: "Темна" }));

    await waitFor(() => expect(fetchStub.requestsTo(save)).toHaveLength(1));
    const chosenAt = window.localStorage.getItem("theme-chosen-at");
    expect(fetchStub.requestsTo(save)[0].body).toEqual({ theme: "dark", chosenAt });
    expect(window.localStorage.getItem("theme")).toBe("dark");
  });
});

describe("AppearanceSync", () => {
  // The server as the api keeps it: a field changes only for a later time, and every answer is the whole state.
  function mount(initial: typeof stored) {
    let server = { ...initial };
    const fetchStub = stubFetch({
      [read]: () => server,
      [save]: (request) => {
        const body = request.body as { locale?: string; theme?: string; chosenAt: string };
        if (body.locale && Date.parse(body.chosenAt) > Date.parse(server.localeChosenAt)) {
          server = { ...server, locale: body.locale, localeChosenAt: body.chosenAt };
        }
        if (body.theme && Date.parse(body.chosenAt) > Date.parse(server.themeChosenAt)) {
          server = { ...server, theme: body.theme, themeChosenAt: body.chosenAt };
        }
        return server;
      },
    });
    const rendered = renderApp(inTheme(<AppearanceSync />));
    signedIn(rendered.queryClient);

    return { ...rendered, fetchStub };
  }

  it("applies a newer server choice over the browser's own and refreshes the language once", async () => {
    chooseLocaleInBrowser("uk", earlier);
    window.localStorage.setItem("theme", "light");
    window.localStorage.setItem("theme-chosen-at", earlier);

    const { fetchStub } = mount({ locale: "ru", localeChosenAt: later, theme: "dark", themeChosenAt: later });

    await waitFor(() => expect(cookie("locale")).toBe("ru"));
    expect(decodeURIComponent(cookie("locale-chosen-at")!)).toBe(later);
    await waitFor(() => expect(window.localStorage.getItem("theme")).toBe("dark"));
    expect(window.localStorage.getItem("theme-chosen-at")).toBe(later);
    expect(router.refresh).toHaveBeenCalledTimes(1);
    expect(fetchStub.requestsTo(save)).toHaveLength(0);
  });

  it("seeds a new device, which has no choice, from the server", async () => {
    mount({ locale: "ru", localeChosenAt: later, theme: "dark", themeChosenAt: later });

    await waitFor(() => expect(cookie("locale")).toBe("ru"));
    expect(router.refresh).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(window.localStorage.getItem("theme")).toBe("dark"));
  });

  it("does not refresh when the server's newer choice is the language already shown", async () => {
    chooseLocaleInBrowser("uk");

    const { fetchStub } = mount({ ...stored, localeChosenAt: later });

    await waitFor(() => expect(decodeURIComponent(cookie("locale-chosen-at") ?? "")).toBe(later));
    expect(router.refresh).not.toHaveBeenCalled();
    expect(fetchStub.requestsTo(save)).toHaveLength(0);
  });

  it("sends a newer browser choice, such as one made signed out on the sign-in page", async () => {
    chooseLocaleInBrowser("ru", later);
    window.localStorage.setItem("theme", "dark");
    window.localStorage.setItem("theme-chosen-at", later);

    const { fetchStub } = mount(stored);

    await waitFor(() => expect(fetchStub.requestsTo(save)).toHaveLength(2));
    expect(fetchStub.requestsTo(save).map((request) => request.body)).toEqual([
      { locale: "ru", chosenAt: later },
      { theme: "dark", chosenAt: later },
    ]);
    expect(cookie("locale")).toBe("ru");
    expect(router.refresh).not.toHaveBeenCalled();
  });

  it("settles once both sides agree: a later answer from the server neither refreshes nor sends again", async () => {
    chooseLocaleInBrowser("uk", earlier);

    const { fetchStub, queryClient } = mount({ ...stored, locale: "ru", localeChosenAt: later });
    await waitFor(() => expect(router.refresh).toHaveBeenCalledTimes(1));

    await act(async () => {
      await queryClient.invalidateQueries({ queryKey: settingsQueryKey });
    });
    await waitFor(() => expect(fetchStub.requestsTo(read)).toHaveLength(2));
    await act(async () => {});

    expect(router.refresh).toHaveBeenCalledTimes(1);
    expect(fetchStub.requestsTo(save)).toHaveLength(0);
  });

  it("leaves a device alone when it already matches the server", async () => {
    chooseLocaleInBrowser("uk", earlier);
    window.localStorage.setItem("theme", "system");
    window.localStorage.setItem("theme-chosen-at", earlier);

    const { fetchStub } = mount(stored);

    await waitFor(() => expect(fetchStub.requestsTo(read)).toHaveLength(1));
    await act(async () => {});
    expect(router.refresh).not.toHaveBeenCalled();
    expect(fetchStub.requestsTo(save)).toHaveLength(0);
  });
});
