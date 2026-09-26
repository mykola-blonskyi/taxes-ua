import { getTranslations } from "next-intl/server";
import type { NavKey } from "@/shared/constants/navigation";

export async function ScreenPlaceholder({ screen }: { screen: NavKey }) {
  const t = await getTranslations(screen);

  return (
    <section className="flex flex-col gap-2">
      <h2 className="text-lg font-semibold md:text-xl">{t("title")}</h2>
      <p className="text-sm text-muted-foreground">{t("empty")}</p>
    </section>
  );
}
