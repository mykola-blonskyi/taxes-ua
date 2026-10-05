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
// renders it in place of its content while the query has no data, and never when it holds data, so a failed
// background refetch leaves the content, and what the owner typed in it, on screen:
//
//   if (!query.data) return <LoadState query={query} failed={t("loadFailed")} />;
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
  resetKey,
}: {
  query: QueryStatus | QueryStatus[];
  loading?: string;
  failed: string;
  // For a side query a screen works without: shows nothing while it loads or waits for the network, and
  // the failure with its retry only if it fails. Render it always, not only on error, so a retry keeps it.
  quiet?: boolean;
  // What the query is about (a year, a filter). When it changes the screen is asking for something new, so an
  // earlier failure is forgotten and the load reads as loading, not as a retry.
  resetKey?: string | number;
}) {
  const apiText = useApiErrorText();
  const [retrying, setRetrying] = useState(false);
  const [seenKey, setSeenKey] = useState(resetKey);
  // Once this instance has shown a failure it keeps showing it until a refetch succeeds and the screen
  // unmounts it. The refetch's promise can settle a render before the query leaves pending, and falling
  // back to the loading line in that gap would swap the button for a new element and drop focus.
  const [sawFailure, setSawFailure] = useState(false);
  const queries = Array.isArray(query) ? query : [query];
  const broken = queries.filter((q) => q.isError);
  const [firstBroken] = broken;

  if (seenKey !== resetKey) {
    setSeenKey(resetKey);
    setSawFailure(false);
    setRetrying(false);
  }

  if (broken.length > 0 && !sawFailure) {
    setSawFailure(true);
  }

  if (queries.some((q) => q.isPaused)) {
    return quiet ? null : <LoadStateView state="offline" />;
  }

  const settled = broken.length === 0 && !retrying && !queries.some((q) => q.isLoading || q.isFetching);

  if (quiet && settled) {
    // A side query that settled without failing: nothing to show, and the next failure starts afresh.
    if (sawFailure) {
      setSawFailure(false);
    }

    return null;
  }

  if (!sawFailure && !retrying && broken.length === 0) {
    // Nothing failed. While a query is on its way that is loading; when none is and the data is still
    // missing, "loading" would last forever, so the screen gets the failure and a retry instead.
    if (queries.some((q) => q.isLoading || q.isFetching)) {
      return quiet ? null : <LoadStateView state="loading" text={loading} />;
    }

    if (quiet) {
      return null;
    }
  }

  const busy = retrying || (sawFailure && queries.some((q) => q.isFetching));

  return (
    <LoadStateView
      state="failed"
      text={apiText.withReason(failed, firstBroken?.error)}
      retrying={busy}
      onRetry={() => {
        setRetrying(true);
        void Promise.allSettled((broken.length > 0 ? broken : queries).map((q) => q.refetch())).finally(() =>
          setRetrying(false),
        );
      }}
    />
  );
}
