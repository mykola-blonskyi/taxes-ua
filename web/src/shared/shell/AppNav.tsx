"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useTranslations } from "next-intl";
import { navItems } from "@/shared/constants/navigation";
import { cn } from "@/shared/lib/utils";

export function AppNav() {
  const t = useTranslations("nav");
  const pathname = usePathname();

  return (
    <nav
      aria-label={t("label")}
      className="sticky bottom-0 order-last flex border-t bg-background md:static md:order-none md:w-52 md:shrink-0 md:flex-col md:gap-1 md:border-t-0 md:border-r md:p-3"
    >
      {navItems.map(({ href, key, icon: Icon }) => {
        const current = pathname === href;

        return (
          <Link
            key={key}
            href={href}
            aria-current={current ? "page" : undefined}
            className={cn(
              "flex min-w-0 flex-1 flex-col items-center gap-0.5 px-1 py-2 text-[11px] md:flex-none md:flex-row md:gap-2 md:rounded-lg md:px-3 md:py-2 md:text-sm",
              current ? "text-foreground md:bg-muted" : "text-muted-foreground hover:text-foreground",
            )}
          >
            <Icon className="size-5 shrink-0 md:size-4" />
            <span className="w-full truncate text-center md:text-start">{t(key)}</span>
          </Link>
        );
      })}
    </nav>
  );
}
