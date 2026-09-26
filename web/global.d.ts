import type messages from "./messages/uk.json";
import type { Locale } from "./src/i18n/locales";

declare module "next-intl" {
  interface AppConfig {
    Locale: Locale;
    Messages: typeof messages;
  }
}
