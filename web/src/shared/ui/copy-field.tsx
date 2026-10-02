"use client";

import { useEffect, useRef, useState } from "react";
import { CheckIcon, CopyIcon } from "lucide-react";
import { Button } from "@/shared/ui/button";

const feedbackMs = 2000;

function useCopy(copyValue: string | null) {
  const [state, setState] = useState<"idle" | "copied" | "failed">("idle");
  const timer = useRef<ReturnType<typeof setTimeout>>(undefined);

  useEffect(() => () => clearTimeout(timer.current), []);

  async function copy() {
    if (copyValue === null) {
      return;
    }

    try {
      await navigator.clipboard.writeText(copyValue);
      setState("copied");
    } catch {
      setState("failed");
    }

    clearTimeout(timer.current);
    timer.current = setTimeout(() => setState("idle"), feedbackMs);
  }

  return { state, copy };
}

// `copyValue` defaults to what is shown; null disables the button.
export function CopyField({
  label,
  value,
  copyValue = value,
  copyLabel,
  copiedLabel,
  failedLabel,
}: {
  label: string;
  value: string;
  copyValue?: string | null;
  copyLabel: string;
  copiedLabel: string;
  failedLabel: string;
}) {
  const { state, copy } = useCopy(copyValue);

  return (
    <div className="grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-2 gap-y-0.5">
      <span className="text-xs text-muted-foreground">{label}</span>
      <span className="col-start-1 min-w-0 text-sm wrap-anywhere font-medium">{value}</span>
      <Button
        type="button"
        variant="outline"
        size="icon"
        aria-label={copyLabel}
        disabled={copyValue === null}
        onClick={copy}
        className="col-start-2 row-span-2 row-start-1"
      >
        {state === "copied" ? <CheckIcon /> : <CopyIcon />}
      </Button>
      <span role="status" className={state === "failed" ? "col-start-1 text-xs text-destructive" : "col-start-1 text-xs text-muted-foreground"}>
        {state === "copied" ? copiedLabel : state === "failed" ? failedLabel : null}
      </span>
    </div>
  );
}

// A button that shows a small label over the value and copies it: three fit side by side on a phone.
export function CopyChip({
  label,
  value,
  copyLabel,
  copiedLabel,
  failedLabel,
}: {
  label: string;
  value: string;
  copyLabel: string;
  copiedLabel: string;
  failedLabel: string;
}) {
  const { state, copy } = useCopy(value);

  return (
    <div className="min-w-0">
      <Button
        type="button"
        variant="outline"
        aria-label={copyLabel}
        onClick={copy}
        className="h-auto w-full min-w-0 flex-col items-start gap-0.5 px-2 py-1.5 text-left"
      >
        <span className="flex w-full items-center justify-between gap-1 text-xs font-normal text-muted-foreground">
          {label}
          {state === "copied" ? <CheckIcon className="size-3" /> : <CopyIcon className="size-3" />}
        </span>
        <span className="text-sm font-medium wrap-anywhere">{value}</span>
      </Button>
      <span role="status" className={state === "failed" ? "text-xs text-destructive" : "sr-only"}>
        {state === "copied" ? copiedLabel : state === "failed" ? failedLabel : null}
      </span>
    </div>
  );
}
