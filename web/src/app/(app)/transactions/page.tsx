import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { TransactionsScreen } from "@/features/transactions";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("transactions");

  return { title: t("title") };
}

export default async function TransactionsPage() {
  const t = await getTranslations("transactions");

  return (
    <section className="flex flex-col gap-4">
      <h2 className="text-lg font-semibold md:text-xl">{t("title")}</h2>
      <TransactionsScreen />
    </section>
  );
}
