import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { PeriodsScreen } from "@/features/periods";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("periods");

  return { title: t("title") };
}

export default async function PeriodsPage() {
  const t = await getTranslations("periods");

  return (
    <section className="flex flex-col gap-4">
      <h2 className="text-lg font-semibold md:text-xl">{t("title")}</h2>
      <PeriodsScreen />
    </section>
  );
}
