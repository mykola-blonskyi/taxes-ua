"use client";

import type { ReactNode } from "react";
import { XIcon } from "lucide-react";
import { Dialog } from "radix-ui";

// A bottom sheet on phones, a centered dialog from `sm` up.
export function Sheet({
  open,
  onOpenChange,
  title,
  description,
  closeLabel,
  children,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: ReactNode;
  description: string;
  closeLabel: string;
  children: ReactNode;
}) {
  return (
    <Dialog.Root open={open} onOpenChange={onOpenChange}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-50 bg-black/50" />
        <Dialog.Content className="fixed inset-x-0 bottom-0 z-50 flex max-h-[90dvh] flex-col gap-4 overflow-y-auto rounded-t-xl border bg-background p-4 shadow-lg outline-none sm:inset-x-auto sm:bottom-auto sm:left-1/2 sm:top-1/2 sm:w-full sm:max-w-lg sm:-translate-x-1/2 sm:-translate-y-1/2 sm:rounded-xl sm:p-6">
          <div className="flex items-start justify-between gap-2">
            <Dialog.Title className="min-w-0 text-lg font-semibold">{title}</Dialog.Title>
            <Dialog.Close
              aria-label={closeLabel}
              className="-m-1 shrink-0 rounded-lg p-1 text-muted-foreground outline-none hover:bg-muted hover:text-foreground focus-visible:ring-3 focus-visible:ring-ring/50"
            >
              <XIcon className="size-5" />
            </Dialog.Close>
          </div>
          <Dialog.Description className="text-sm text-muted-foreground">{description}</Dialog.Description>
          {children}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
