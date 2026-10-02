import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { DashboardScreen } from "@/features/dashboard";
import { TaxYearVerificationWarning } from "@/features/settings";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("dashboard");

  return { title: t("title") };
}

export default async function DashboardPage() {
  const t = await getTranslations("dashboard");

  return (
    <section className="flex flex-col gap-4">
      <h2 className="text-lg font-semibold md:text-xl">{t("title")}</h2>
      <TaxYearVerificationWarning />
      <DashboardScreen />
    </section>
  );
}
