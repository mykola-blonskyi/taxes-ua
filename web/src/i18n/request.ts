import { cookies } from "next/headers";
import { getRequestConfig } from "next-intl/server";
import { localeCookieName, parseLocale } from "./locales";

export default getRequestConfig(async () => {
  const store = await cookies();
  const locale = parseLocale(store.get(localeCookieName)?.value);

  return { locale, messages: (await import(`../../messages/${locale}.json`)).default };
});
