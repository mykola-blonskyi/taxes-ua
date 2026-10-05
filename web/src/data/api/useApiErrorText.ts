"use client";

import { useTranslations } from "next-intl";
import { isNetworkFailure, problemOf } from "./client";

// The codes are data the api sends, so next-intl's static key typing cannot check them. Every lookup goes
// through `has` and reads the generic text for a code this build has no words for.
type CodeTranslator = {
  (code: string): string;
  has(code: string): boolean;
};

// The one place an api failure becomes words (ADR-028). The api's `title`, `detail` and per-field messages
// are English for logs and are never shown: a screen asks for the text of a code, in the owner's language,
// and an unknown code, or a failure with none, reads as the generic sentence. A failure that never got an
// answer from the api (a dropped connection) reads as the network sentence, and anything else that was
// thrown (a browser or passkey error, a bug) as the unexpected one: the network is not to blame for it.
export function useApiErrorText() {
  const t = useTranslations("apiErrors") as unknown as CodeTranslator;

  function ofCode(code: string | null | undefined): string {
    return code && t.has(code) ? t(code) : t("unknown");
  }

  function describe(error: unknown): string {
    const problem = problemOf(error);

    if (problem) {
      return ofCode(problem.code);
    }

    return t(isNetworkFailure(error) ? "network" : "unexpected");
  }

  return {
    ofCode,
    // The sentence for a failed call of any kind.
    describe,
    // A screen's own words followed by the reason: the api's code when this build can word it, otherwise the
    // network or unexpected sentence. A prefix that ends in a colon promises a reason, so the api's answer
    // with nothing to word reads as the unexpected sentence; any other prefix stands alone, and a colon with
    // no error to read becomes a full stop.
    withReason: (prefix: string, error: unknown): string => {
      const problem = problemOf(error);

      if (problem?.code && t.has(problem.code)) {
        return `${prefix} ${t(problem.code)}`;
      }

      if (error == null) {
        return prefix.replace(/:$/, ".");
      }

      return problem && !prefix.endsWith(":") ? prefix : `${prefix} ${problem ? t("unexpected") : describe(error)}`;
    },
    // The sentences of each rejected field, keyed by the field path the api used.
    fieldTexts: (error: unknown): Record<string, string[]> =>
      Object.fromEntries(
        Object.entries(problemOf(error)?.fieldCodes ?? {}).map(([field, codes]) => [field, codes.map(ofCode)]),
      ),
  };
}
