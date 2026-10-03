import type { ReactNode } from "react";

// The pieces every notification channel shows the same way: whether it is on, and why the last
// delivery did not happen.
export function ChannelStatusBadge({ on, children }: { on: boolean; children: ReactNode }) {
  return (
    <span
      className={
        on
          ? "rounded-full bg-emerald-500/15 px-2.5 py-0.5 text-xs font-medium text-emerald-700 dark:text-emerald-400"
          : "rounded-full bg-muted px-2.5 py-0.5 text-xs font-medium text-muted-foreground"
      }
    >
      {children}
    </span>
  );
}

export function ChannelFailureNotice({ children }: { children: ReactNode }) {
  return (
    <p role="alert" className="min-w-0 break-words rounded-lg border border-destructive/40 bg-destructive/10 p-3 text-sm text-destructive">
      {children}
    </p>
  );
}
