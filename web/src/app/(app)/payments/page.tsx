import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { PaymentsScreen } from "@/features/payments";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("payments");

  return { title: t("title") };
}

export default async function PaymentsPage() {
  const t = await getTranslations("payments");

  return (
    <section className="flex flex-col gap-4">
      <h2 className="text-lg font-semibold md:text-xl">{t("title")}</h2>
      <PaymentsScreen />
    </section>
  );
}
