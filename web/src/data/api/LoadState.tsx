"use client";

import { useTranslations } from "next-intl";
import { useApiErrorText } from "./useApiErrorText";
import { Button } from "@/shared/ui/button";

// The slice of a TanStack Query result this component reads, so a screen passes its query as it is.
export type QueryStatus = {
  isLoading: boolean;
  isError: boolean;
  error: unknown;
  isFetching: boolean;
  refetch: () => unknown;
};

// The one loading and failure state of every data screen. A screen renders it in place of its content
// while the query loads or has failed, and never when it holds data:
//
//   if (query.isLoading || query.isError || !query.data) return <LoadState query={query} failed={t("loadFailed")} />;
//
// Loading is a polite status; a failure is an alert, worded with the api's coded reason when this build
// has words for it (ADR-028), and always offers a retry that refetches the failed query. Several queries
// may be passed when one screen needs all of them.
export function LoadState({
  query,
  loading,
  failed,
}: {
  query: QueryStatus | QueryStatus[];
  loading?: string;
  failed: string;
}) {
  const t = useTranslations("loadState");
  const apiText = useApiErrorText();
  const queries = Array.isArray(query) ? query : [query];

  if (queries.some((q) => q.isLoading)) {
    return (
      <p role="status" className="text-sm text-muted-foreground">
        {loading ?? t("loading")}
      </p>
    );
  }

  const broken = queries.filter((q) => q.isError);
  const retrying = broken.some((q) => q.isFetching);

  return (
    <div role="alert" className="flex min-w-0 flex-col items-start gap-2">
      <p className="break-words text-sm text-destructive">{apiText.withReason(failed, broken[0]?.error)}</p>
      <Button
        type="button"
        variant="outline"
        size="sm"
        disabled={retrying}
        onClick={() => {
          for (const q of broken.length > 0 ? broken : queries) {
            void q.refetch();
          }
        }}
      >
        {retrying ? t("retrying") : t("retry")}
      </Button>
    </div>
  );
}
