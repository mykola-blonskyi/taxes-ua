import type { ReactElement } from "react";
import { QueryClientProvider } from "@tanstack/react-query";
import { render } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { NextIntlClientProvider } from "next-intl";
import ru from "../../messages/ru.json";
import uk from "../../messages/uk.json";
import { createQueryClient } from "@/data/queryClient";
import type { Locale } from "@/i18n/locales";
import { router } from "./navigation";

const catalogs = { uk, ru } satisfies Record<Locale, object>;

function freshQueryClient() {
  const client = createQueryClient((path) => router.replace(path));
  client.setDefaultOptions({ queries: { retry: false }, mutations: { retry: false } });

  return client;
}

/**
 * Renders a client component as the app mounts it: inside the i18n provider with the real catalog of the
 * owner's language (Ukrainian unless told otherwise) and a fresh query client that never retries.
 * Returns Testing Library's result and a user-event instance; pass `userOptions` such as
 * `{ advanceTimers: vi.advanceTimersByTime }` when the test uses fake timers.
 */
export function renderApp(
  ui: ReactElement,
  { locale = "uk" }: { locale?: Locale } = {},
  userOptions: Parameters<typeof userEvent.setup>[0] = {},
) {
  const queryClient = freshQueryClient();
  const wrap = (node: ReactElement) => (
    <NextIntlClientProvider locale={locale} messages={catalogs[locale]} timeZone="Europe/Kyiv">
      <QueryClientProvider client={queryClient}>{node}</QueryClientProvider>
    </NextIntlClientProvider>
  );
  const result = render(wrap(ui));

  return {
    ...result,
    queryClient,
    user: userEvent.setup(userOptions),
    rerender: (next: ReactElement) => result.rerender(wrap(next)),
  };
}

export { act, screen, within, waitFor } from "@testing-library/react";
export { stubFetch, reply, type Routes } from "./fetch-stub";
export { router, setLocation } from "./navigation";
export { useFakeTimers } from "./timers";
