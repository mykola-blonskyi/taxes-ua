"use client";

import { useTranslations } from "next-intl";
import { problemOf } from "./client";

// The codes are data the api sends, so next-intl's static key typing cannot check them. Every lookup goes
// through `has` and reads the generic text for a code this build has no words for.
type CodeTranslator = {
  (code: string): string;
  has(code: string): boolean;
};

// The one place an api failure becomes words (ADR-028). The api's `title`, `detail` and per-field messages
// are English for logs and are never shown: a screen asks for the text of a code, in the owner's language,
// and an unknown code, or a failure with none, reads as the generic sentence. A failure that never got an
// answer from the api (a dropped connection) reads as the network sentence.
export function useApiErrorText() {
  const t = useTranslations("apiErrors") as unknown as CodeTranslator;

  function ofCode(code: string | null | undefined): string {
    return code && t.has(code) ? t(code) : t("unknown");
  }

  return {
    ofCode,
    // The sentence for a failed call of any kind.
    describe: (error: unknown): string => {
      const problem = problemOf(error);

      return problem ? ofCode(problem.code) : t("network");
    },
    // A screen's own "could not save" followed by the reason: the api's code when this build can word it,
    // the network sentence when the call never got an answer.
    withReason: (prefix: string, error: unknown): string => {
      const problem = problemOf(error);

      if (!problem) {
        return `${prefix} ${t("network")}`;
      }

      return problem.code && t.has(problem.code) ? `${prefix} ${t(problem.code)}` : prefix;
    },
    // The sentences of each rejected field, keyed by the field path the api used.
    fieldTexts: (error: unknown): Record<string, string[]> =>
      Object.fromEntries(
        Object.entries(problemOf(error)?.fieldCodes ?? {}).map(([field, codes]) => [field, codes.map(ofCode)]),
      ),
  };
}
