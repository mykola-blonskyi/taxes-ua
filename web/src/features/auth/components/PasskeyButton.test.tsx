import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { dashboardQueryKey } from "@/data/dashboard/useDashboard";
import { renderApp, reply, router, screen, stubFetch, waitFor } from "@/test/harness";
import { PasskeyButton } from "./PasskeyButton";

describe("PasskeyButton", () => {
  beforeEach(() => {
    vi.stubGlobal("PublicKeyCredential", {
      parseRequestOptionsFromJSON: () => ({}),
      parseCreationOptionsFromJSON: () => ({}),
    });
    vi.stubGlobal("navigator", { ...navigator, credentials: { get: async () => ({ id: "key" }) } });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("starts a signed-in session from an empty cache, so another owner's data never shows", async () => {
    stubFetch({
      "POST /api/auth/passkey/login/options": {},
      "POST /api/auth/passkey/login": reply(204),
    });
    const { queryClient, user } = renderApp(<PasskeyButton mode="signIn" />);
    queryClient.setQueryData(["me"], { id: "u1" });
    queryClient.setQueryData(dashboardQueryKey, { year: 2026 });

    await user.click(await screen.findByRole("button", { name: "Увійти за допомогою passkey" }));

    await waitFor(() => expect(router.replace).toHaveBeenCalledWith("/"));
    expect(queryClient.getQueryCache().getAll()).toHaveLength(0);
    expect(queryClient.getQueryData(dashboardQueryKey)).toBeUndefined();
  });

  it.each([
    ["uk", "Увійти за допомогою passkey", "Не вдалося виконати дію з passkey. Спробуйте ще раз. Немає зв'язку з сервером. Перевірте мережу й спробуйте ще раз."],
    ["ru", "Войти с помощью passkey", "Не удалось выполнить действие с passkey. Попробуйте снова. Нет связи с сервером. Проверьте сеть и попробуйте ещё раз."],
  ] as const)("says so in %s when the api cannot be reached", async (locale, name, message) => {
    stubFetch({
      "POST /api/auth/passkey/login/options": () => {
        throw new TypeError("Failed to fetch");
      },
    });
    const { user } = renderApp(<PasskeyButton mode="signIn" />, { locale });

    await user.click(await screen.findByRole("button", { name }));

    expect(await screen.findByText(message)).toBeVisible();
  });
});
