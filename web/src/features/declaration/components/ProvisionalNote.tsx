"use client";

import Link from "next/link";
import { useTranslations } from "next-intl";

// ADR-023: until the owner marks group 3 confirmed the figures are provisional, but the file and the
// filed mark stay open, because the owner can see the DPS register and the app cannot.
export function ProvisionalNote({ text }: { text: string }) {
  const t = useTranslations("declaration.provisional");

  return (
    <p
      role="note"
      className="break-words rounded-lg border border-amber-500/40 bg-amber-500/10 p-3 text-sm text-amber-800 dark:text-amber-300"
    >
      {text}{" "}
      <Link href="/settings?tab=dps" className="font-medium text-primary underline-offset-4 hover:underline">
        {t("checkStatus")}
      </Link>
    </p>
  );
}
