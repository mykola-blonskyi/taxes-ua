import { getTranslations } from "next-intl/server";
import type { ReactNode } from "react";
import { ThemeToggle } from "@/shared/theme/ThemeToggle";
import { LanguageToggle } from "./LanguageToggle";

export async function AppHeader({ actions }: { actions?: ReactNode }) {
  const t = await getTranslations("app");

  return (
    <header className="flex items-center gap-1 border-b px-3 py-2 md:px-6">
      <h1 className="min-w-0 flex-1 truncate text-sm font-semibold md:text-base">{t("title")}</h1>
      <LanguageToggle />
      <ThemeToggle />
      {actions}
    </header>
  );
}
