"use client";

import { useState } from "react";
import { LoadStateView } from "@/shared/ui/load-state";
import { useApiErrorText } from "./useApiErrorText";

// The slice of a TanStack Query result this component reads, so a screen passes its query as it is.
export type QueryStatus = {
  isLoading: boolean;
  isError: boolean;
  error: unknown;
  isFetching: boolean;
  isPaused?: boolean;
  refetch: () => unknown;
};

// The one loading and failure state of every data screen: the query adapter over LoadStateView. A screen
// renders it in place of its content while the query loads or has failed, and never when it holds data:
//
//   if (query.isLoading || query.isError || !query.data) return <LoadState query={query} failed={t("loadFailed")} />;
//
// A failure is worded with the api's coded reason when this build has words for it (ADR-028), and its retry
// refetches the failed queries. TanStack resets a failed query that has no data to pending while it
// refetches, which would unmount the alert and drop focus, so the adapter remembers it is retrying and keeps
// the alert (and its button) on screen until the refetch settles. A query that cannot run because the device
// is offline reads as a status line, not as a retry that would do nothing. Several queries may be passed
// when one screen needs all of them.
export function LoadState({
  query,
  loading,
  failed,
  quiet = false,
}: {
  query: QueryStatus | QueryStatus[];
  loading?: string;
  failed: string;
  // For a side query a screen works without: shows nothing while it loads or waits for the network, and
  // the failure with its retry only if it fails. Render it always, not only on error, so a retry keeps it.
  quiet?: boolean;
}) {
  const apiText = useApiErrorText();
  const [retrying, setRetrying] = useState(false);
  const queries = Array.isArray(query) ? query : [query];
  const broken = queries.filter((q) => q.isError);
  const [firstBroken] = broken;

  if (queries.some((q) => q.isPaused)) {
    return quiet ? null : <LoadStateView state="offline" />;
  }

  if (!retrying && broken.length === 0) {
    return quiet ? null : <LoadStateView state="loading" text={loading} />;
  }

  return (
    <LoadStateView
      state="failed"
      text={apiText.withReason(failed, firstBroken?.error)}
      retrying={retrying}
      onRetry={() => {
        setRetrying(true);
        void Promise.allSettled((broken.length > 0 ? broken : queries).map((q) => q.refetch())).finally(() =>
          setRetrying(false),
        );
      }}
    />
  );
}
