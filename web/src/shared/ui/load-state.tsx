"use client";

import { useId } from "react";
import { useTranslations } from "next-intl";
import { Button } from "./button";

// The words and the retry of a screen that is waiting or has failed, with no idea what a query is. A
// screen that holds a TanStack query uses the adapter in @/data/api (LoadState); one that has only a
// refetch function, such as the pay panel, uses this directly.
//
// Loading and offline are polite statuses. A failure is an alert whose retry stays in place while it runs:
// the button is aria-disabled, not disabled, so keyboard focus is not lost to the page body mid-retry.
export type LoadStateViewProps =
  | { state: "loading"; text?: string }
  | { state: "offline"; text?: string }
  | { state: "failed"; text: string; retrying: boolean; onRetry: () => void };

export function LoadStateView(props: LoadStateViewProps) {
  const t = useTranslations("loadState");
  const textId = useId();

  if (props.state !== "failed") {
    return (
      <p role="status" className="text-sm text-muted-foreground">
        {props.text ?? (props.state === "offline" ? t("offline") : t("loading"))}
      </p>
    );
  }

  const { retrying, onRetry } = props;

  return (
    <div role="alert" className="flex min-w-0 flex-col items-start gap-2">
      <p id={textId} className="break-words text-sm text-destructive">
        {props.text}
      </p>
      <Button
        type="button"
        variant="outline"
        size="sm"
        aria-disabled={retrying}
        aria-describedby={textId}
        className="aria-disabled:pointer-events-none aria-disabled:opacity-50"
        onClick={() => {
          if (!retrying) {
            onRetry();
          }
        }}
      >
        {retrying ? t("retrying") : t("retry")}
      </Button>
    </div>
  );
}
