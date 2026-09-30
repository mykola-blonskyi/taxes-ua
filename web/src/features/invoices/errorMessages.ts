import { useTranslations } from "next-intl";
import type { FieldErrors } from "./form";

// The api keys its rejections by field path (`invoicing.rnokpp`, `lines[1].descriptionEn`), so the
// message keys are computed from data and next-intl's static key typing cannot check them. Every
// lookup goes through `has` and falls back to the api's own text.
type DynamicTranslator = {
  (key: string, values?: Record<string, string>): string;
  has(key: string): boolean;
};

export type IssueProblem = { key: string; message: string; href: "/settings" | null };

const settingsPrefixes = ["invoicing.", "client."] as const;

const linePattern = /^lines\[(\d+)\]\.(\w+)$/;

export function useErrorMessages(errors: FieldErrors) {
  const t = useTranslations("invoices.errors") as unknown as DynamicTranslator;

  function messageKey(key: string): { path: string; values?: Record<string, string> } {
    const line = linePattern.exec(key);

    if (line) {
      return { path: `line.${line[2]}` };
    }

    if (key.startsWith("invoicing.paymentDetails.")) {
      return { path: "paymentDetails", values: { currency: key.slice("invoicing.paymentDetails.".length) } };
    }

    if (key.startsWith("invoicing.")) {
      return { path: `setup.${key.slice("invoicing.".length)}` };
    }

    if (key.startsWith("client.")) {
      return { path: `clientData.${key.slice("client.".length)}` };
    }

    return { path: key };
  }

  function describe(key: string): string {
    const { path, values } = messageKey(key);

    return t.has(path) ? t(path, values) : (errors[key]?.[0] ?? t("invalid"));
  }

  // The messages a field shows, localised; a key the api rejected and this UI has no words for keeps
  // the api's own text.
  function forField(key: string): string[] | undefined {
    return errors[key] ? [describe(key)] : undefined;
  }

  function issueProblems(): IssueProblem[] {
    return Object.keys(errors)
      .filter((key) => settingsPrefixes.some((prefix) => key.startsWith(prefix)) || key === "totalMinor")
      .map((key) => ({
        key,
        message: describe(key),
        href: settingsPrefixes.some((prefix) => key.startsWith(prefix)) ? "/settings" : null,
      }));
  }

  return { forField, issueProblems };
}
